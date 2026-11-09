namespace Brain.Search;

/// <summary>
/// Brings the index level with the graph files of the scopes in play: it writes only the rows whose node changed or
/// that are missing, and drops the rows of those scopes whose node is gone. Rows of scopes not in play stay.
/// </summary>
internal static class IndexRefresh
{
    /// <summary>A full re-embed writes this many vectors at a time, so an interrupted one keeps what it did.</summary>
    private const int Chunk = 32;

    public static void Texts(SearchIndex index, IReadOnlyList<Scope> scopes, IReadOnlyCollection<PlacedNode> nodes)
    {
        var rows = index.Texts().ToDictionary(row => row.Key, StringComparer.Ordinal);
        var wanted = nodes.Select(node => (Node: node, Columns: NodeDocument.Columns(node.Node))).ToList();
        var put = wanted
            .Select(text => (Row: new IndexRow(NodeDocument.Key(text.Node), NodeDocument.ScopeKey(text.Node.Scope), NodeDocument.Hash(text.Columns)), text.Columns))
            .Where(text => !rows.TryGetValue(text.Row.Key, out var row) || row.Hash != text.Row.Hash)
            .ToList();
        var dropped = Dropped(rows.Values.Select(row => (row.Key, row.Scope)), scopes, nodes);
        if (put.Count > 0 || dropped.Count > 0)
        {
            index.ApplyTexts(put, dropped);
        }
    }

    /// <summary>
    /// Every node's vector by its key, as the embedder's model made it, or null when a node needed embedding and the
    /// embedder did not answer. Only that model's rows are read and written, so another model's stay as they are.
    /// <paramref name="progress"/> hears how many of the nodes have a vector, of how many, before the first chunk and
    /// after each.
    /// </summary>
    public static Dictionary<string, float[]>? Vectors(
        SearchIndex index, IEmbedder embedder, IReadOnlyList<Scope> scopes, IReadOnlyCollection<PlacedNode> nodes, Action<int, int>? progress = null)
    {
        var model = embedder.Profile.Name;
        var rows = index.Vectors(model).ToDictionary(entry => entry.Key, StringComparer.Ordinal);
        var wanted = nodes.Select(node => Wanted.Of(embedder.Profile, node)).ToList();
        var dropped = Dropped(rows.Values.Select(row => (row.Key, row.Scope)), scopes, nodes);
        if (dropped.Count > 0)
        {
            index.ApplyVectors(model, [], dropped);
        }

        var missing = wanted.Where(entry => !rows.TryGetValue(entry.Key, out var row) || row.Hash != entry.Hash).ToList();
        var done = wanted.Count - missing.Count;
        progress?.Invoke(done, wanted.Count);
        foreach (var chunk in missing.Chunk(Chunk))
        {
            var vectors = embedder.Embed([.. chunk.Select(entry => entry.Passage)]);
            if (vectors is null || vectors.Count != chunk.Length)
            {
                return null;
            }

            List<IndexEntry> fresh = [.. chunk.Zip(vectors, (entry, vector) => new IndexEntry(entry.Key, entry.Scope, entry.Hash, vector))];
            index.ApplyVectors(model, fresh, []);
            foreach (var entry in fresh)
            {
                rows[entry.Key] = entry;
            }

            done += chunk.Length;
            progress?.Invoke(done, wanted.Count);
        }

        return wanted.ToDictionary(entry => entry.Key, entry => rows[entry.Key].Vector, StringComparer.Ordinal);
    }

    private static List<string> Dropped(IEnumerable<(string Key, string Scope)> rows, IReadOnlyList<Scope> scopes, IReadOnlyCollection<PlacedNode> nodes)
    {
        var inPlay = scopes.Select(NodeDocument.ScopeKey).ToHashSet(StringComparer.Ordinal);
        var keys = nodes.Select(NodeDocument.Key).ToHashSet(StringComparer.Ordinal);
        return [.. rows.Where(row => inPlay.Contains(row.Scope) && !keys.Contains(row.Key)).Select(row => row.Key)];
    }

    /// <summary>
    /// A vector row the graph asks for: the node's keys, the hash of its model's name and its passage as that model
    /// embeds it, and that passage to embed when the hash is new.
    /// </summary>
    private sealed record Wanted(string Key, string Scope, string Hash, string Passage)
    {
        public static Wanted Of(EmbedderProfile profile, PlacedNode node)
        {
            var passage = profile.Passage(NodeDocument.Passage(node.Node));
            return new(NodeDocument.Key(node), NodeDocument.ScopeKey(node.Scope), NodeDocument.Hash(profile.Name, passage), passage);
        }
    }
}
