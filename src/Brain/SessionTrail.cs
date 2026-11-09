using Microsoft.Data.Sqlite;

namespace Brain;

/// <summary>
/// The SQLite file under the brain home's cache/ that remembers, per Claude Code session, which nodes were opened and
/// which edges were already travelled. It is derived and throwaway: deleting it forgets only today's open sessions.
/// </summary>
internal sealed class SessionTrail : IDisposable
{
    public const string FileName = "trail.sqlite";

    private const string Schema =
        "CREATE TABLE IF NOT EXISTS opened (session TEXT NOT NULL, node TEXT NOT NULL, PRIMARY KEY (session, node));"
        + "CREATE TABLE IF NOT EXISTS travelled (session TEXT NOT NULL, edge TEXT NOT NULL, PRIMARY KEY (session, edge))";

    private const string SelectOpened = "SELECT 1 FROM opened WHERE session = $session AND node = $key";
    private const string InsertOpened = "INSERT OR IGNORE INTO opened (session, node) VALUES ($session, $key)";
    private const string InsertTravelled = "INSERT OR IGNORE INTO travelled (session, edge) VALUES ($session, $key)";

    private readonly SqliteConnection connection;
    private readonly string session;

    private SessionTrail(SqliteConnection connection, string session)
    {
        this.connection = connection;
        this.session = session;
    }

    public static string PathIn(string home) => Path.Combine(home, Layout.CacheDir, FileName);

    public static SessionTrail Open(string path, string session)
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
            return new SessionTrail(connection, session);
        }
        catch
        {
            connection.Dispose();
            throw;
        }
    }

    public bool WasOpened(string node)
    {
        using var command = Keyed(node);
        command.CommandText = SelectOpened;
        return command.ExecuteScalar() is not null;
    }

    public void MarkOpened(string node)
    {
        using var command = Keyed(node);
        command.CommandText = InsertOpened;
        command.ExecuteNonQuery();
    }

    /// <summary>Marks the edge travelled in this session; false when it already was.</summary>
    public bool MarkTravelled(string edge)
    {
        using var command = Keyed(edge);
        command.CommandText = InsertTravelled;
        return command.ExecuteNonQuery() == 1;
    }

    public void Dispose() => connection.Dispose();

    /// <summary>A command bound to this session and <paramref name="key"/>; the caller sets its constant text.</summary>
    private SqliteCommand Keyed(string key)
    {
        var command = connection.CreateCommand();
        command.Parameters.AddWithValue("$session", session);
        command.Parameters.AddWithValue("$key", key);
        return command;
    }
}
