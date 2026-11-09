using Microsoft.Data.Sqlite;

namespace Brain.Search;

/// <summary>
/// The SQLite file search reads under the brain home's cache/: an FTS5 table of every node's text and a table of node
/// vectors, each row under the name of the embedding model that made it, so a switch builds the new model's vectors
/// beside the ones search reads. It is derived: deleting it costs one rebuild from the graph and one re-embed, and
/// nothing else reads it.
/// </summary>
internal sealed class SearchIndex : IDisposable
{
    public const string FileName = "search.sqlite";

    /// <summary>The snippet's marks around a matched word; control characters, so no node text holds them.</summary>
    public const char MarkOpen = '\u0002';

    public const char MarkClose = '\u0003';

    /// <summary>The table of one vector per node, from before each model had rows of its own, goes: its nodes are embedded again.</summary>
    private const string Schema = """
        DROP TABLE IF EXISTS embedding;
        CREATE TABLE IF NOT EXISTS vector (
            key TEXT NOT NULL, model TEXT NOT NULL, scope TEXT NOT NULL, hash TEXT NOT NULL, vector BLOB NOT NULL, PRIMARY KEY (key, model));
        CREATE VIRTUAL TABLE IF NOT EXISTS fulltext USING fts5(
            key UNINDEXED, scope UNINDEXED, hash UNINDEXED, slug, title, subjects, why, body, tokenize = 'porter unicode61');
        """;

    private const string SelectVectors = "SELECT key, scope, hash, vector FROM vector WHERE model = $model";
    private const string UpsertVector = "INSERT OR REPLACE INTO vector (key, model, scope, hash, vector) VALUES ($key, $model, $scope, $hash, $vector)";
    private const string DeleteVector = "DELETE FROM vector WHERE key = $key";
    private const string DeleteOtherModels = "DELETE FROM vector WHERE model <> $model";
    private const string SelectTexts = "SELECT key, scope, hash FROM fulltext";
    private const string InsertText =
        "INSERT INTO fulltext (key, scope, hash, slug, title, subjects, why, body) VALUES ($key, $scope, $hash, $slug, $title, $subjects, $why, $body)";

    private const string DeleteText = "DELETE FROM fulltext WHERE key = $key";
    private const string MatchKeys = "SELECT key FROM fulltext WHERE fulltext MATCH $query";

    /// <summary>
    /// BM25 with a weight per column, in table order: slug and title above subjects, above why, above body. Each hit
    /// brings a snippet of its body and of its why, about a dozen words around the matches, and its BM25, lower better.
    /// </summary>
    private const string MatchText = """
        SELECT key, snippet(fulltext, 7, char(2), char(3), '…', 12), snippet(fulltext, 6, char(2), char(3), '…', 12),
            bm25(fulltext, 0, 0, 0, 8, 8, 4, 2, 1) AS score FROM fulltext
        WHERE fulltext MATCH $query ORDER BY score LIMIT $limit
        """;

    private static readonly string[] TextColumns = ["$slug", "$title", "$subjects", "$why", "$body"];

    private readonly SqliteConnection connection;

    private SearchIndex(SqliteConnection connection) => this.connection = connection;

    public static string PathIn(string home) => Path.Combine(home, Layout.CacheDir, FileName);

    public static SearchIndex Open(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var source = new SqliteConnectionStringBuilder { DataSource = path, Pooling = false }.ToString();
        var connection = new SqliteConnection(source);
        try
        {
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = Schema;
            command.ExecuteNonQuery();
            return new SearchIndex(connection);
        }
        catch
        {
            connection.Dispose();
            throw;
        }
    }

    /// <summary>The vectors <paramref name="model"/> made.</summary>
    public List<IndexEntry> Vectors(string model)
    {
        using var command = connection.CreateCommand();
        command.CommandText = SelectVectors;
        command.Parameters.AddWithValue("$model", model);
        using var reader = command.ExecuteReader();
        var entries = new List<IndexEntry>();
        while (reader.Read())
        {
            entries.Add(new IndexEntry(reader.GetString(0), reader.GetString(1), reader.GetString(2), VectorBlob.FromBytes((byte[])reader[3])));
        }

        return entries;
    }

    public List<IndexRow> Texts()
    {
        using var command = connection.CreateCommand();
        command.CommandText = SelectTexts;
        using var reader = command.ExecuteReader();
        var rows = new List<IndexRow>();
        while (reader.Read())
        {
            rows.Add(new IndexRow(reader.GetString(0), reader.GetString(1), reader.GetString(2)));
        }

        return rows;
    }

    /// <summary>
    /// Writes <paramref name="put"/> as vectors of <paramref name="model"/> and removes every model's vector of the keys
    /// <paramref name="dropped"/>, in one transaction.
    /// </summary>
    public void ApplyVectors(string model, IReadOnlyList<IndexEntry> put, IReadOnlyList<string> dropped)
    {
        using var transaction = connection.BeginTransaction();
        using (var delete = connection.CreateCommand())
        {
            delete.CommandText = DeleteVector;
            delete.Transaction = transaction;
            foreach (var key in dropped)
            {
                Run(delete, ("$key", key));
            }
        }

        using (var upsert = connection.CreateCommand())
        {
            upsert.CommandText = UpsertVector;
            upsert.Transaction = transaction;
            foreach (var entry in put)
            {
                Run(upsert, ("$key", entry.Key), ("$model", model), ("$scope", entry.Scope), ("$hash", entry.Hash), ("$vector", VectorBlob.ToBytes(entry.Vector)));
            }
        }

        transaction.Commit();
    }

    /// <summary>Removes the vectors of every model but <paramref name="model"/>.</summary>
    public void KeepVectorsOf(string model)
    {
        using var command = connection.CreateCommand();
        command.CommandText = DeleteOtherModels;
        command.Parameters.AddWithValue("$model", model);
        command.ExecuteNonQuery();
    }

    /// <summary>Replaces the rows of <paramref name="put"/>, whose columns follow <see cref="NodeDocument.Columns"/>, and removes <paramref name="dropped"/>.</summary>
    public void ApplyTexts(IReadOnlyList<(IndexRow Row, string[] Columns)> put, IReadOnlyList<string> dropped)
    {
        using var transaction = connection.BeginTransaction();
        using (var delete = connection.CreateCommand())
        {
            delete.CommandText = DeleteText;
            delete.Transaction = transaction;
            foreach (var key in dropped.Concat(put.Select(text => text.Row.Key)))
            {
                Run(delete, ("$key", key));
            }
        }

        using (var insert = connection.CreateCommand())
        {
            insert.CommandText = InsertText;
            insert.Transaction = transaction;
            foreach (var (row, columns) in put)
            {
                Run(insert, [("$key", row.Key), ("$scope", row.Scope), ("$hash", row.Hash), .. TextColumns.Zip(columns, (name, value) => (name, (object)value))]);
            }
        }

        transaction.Commit();
    }

    /// <summary>
    /// The keys that match an FTS5 query, best first, each with its snippet: the body's when the body matched, else the
    /// why's when the why did, else the start of the body, or of the why when the body is empty, or null for a node
    /// with neither. The title is shown beside it already. Score is how well it matched, higher better.
    /// </summary>
    public List<(string Key, string? Snippet, double Score)> Match(string query, int limit)
    {
        using var command = connection.CreateCommand();
        command.CommandText = MatchText;
        command.Parameters.AddWithValue("$query", query);
        command.Parameters.AddWithValue("$limit", limit);
        using var reader = command.ExecuteReader();
        var hits = new List<(string, string?, double)>();
        while (reader.Read())
        {
            hits.Add((reader.GetString(0), Snippet(reader.GetString(1), reader.GetString(2)), -reader.GetDouble(3)));
        }

        return hits;
    }

    /// <summary>Every key that matches an FTS5 query, in no order.</summary>
    public HashSet<string> Keys(string query)
    {
        using var command = connection.CreateCommand();
        command.CommandText = MatchKeys;
        command.Parameters.AddWithValue("$query", query);
        using var reader = command.ExecuteReader();
        var keys = new HashSet<string>(StringComparer.Ordinal);
        while (reader.Read())
        {
            keys.Add(reader.GetString(0));
        }

        return keys;
    }

    public void Dispose() => connection.Dispose();

    private static string? Snippet(string body, string why)
    {
        var chosen = body.Contains(MarkOpen, StringComparison.Ordinal) ? body
            : why.Contains(MarkOpen, StringComparison.Ordinal) || body.Length == 0 ? why
            : body;
        return chosen.Length == 0 ? null : chosen;
    }

    /// <summary>Runs <paramref name="command"/> once with these values bound, replacing the values of its last run.</summary>
    private static void Run(SqliteCommand command, params (string Name, object Value)[] values)
    {
        foreach (var (name, value) in values)
        {
            if (command.Parameters.Contains(name))
            {
                command.Parameters[name].Value = value;
            }
            else
            {
                command.Parameters.AddWithValue(name, value);
            }
        }

        command.ExecuteNonQuery();
    }
}
