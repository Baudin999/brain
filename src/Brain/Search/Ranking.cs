using System.Text.RegularExpressions;
using Microsoft.Data.Sqlite;

namespace Brain.Search;

/// <summary>
/// A node the ranking found, and the FTS5 snippet of its best matching column when the full text matched it.
/// </summary>
internal sealed record Found(PlacedNode Node, string? Snippet);

/// <summary>A node a search found, its snippet, and its tier, sure or likely.</summary>
internal sealed record Hit(PlacedNode Node, string? Snippet, Tier Tier);

/// <summary>
/// The hits, best first: a search's <see cref="Hit"/>s, or the nearest entries as <see cref="Found"/>. KeywordsOnly is
/// true when the embedder did not answer, so no hit came by meaning.
/// </summary>
internal sealed record Ranked<THit>(IReadOnlyList<THit> Hits, bool KeywordsOnly);

/// <summary>
/// The one ranking engine, behind <c>brain lookup</c>, the window's search box, Home's sky and the nearest entries. A
/// node whose slug, ref or title is the query is pinned to the top. Below it three rankings fuse by
/// <see cref="RankFusion"/>: structure, title or slug prefix then subject matches; full text, FTS5 BM25 over slug,
/// title, subjects, why and body; and meaning, cosine between the query's vector and each node's. Then the edge
/// neighbours of the top hits rise a little, by edge weight. A search weighs each hit's <see cref="Evidence"/> for its
/// tier, and a weak hit never comes back; the nearest entries are not tiered. It refreshes the index from the graph
/// first, and writes no graph file.
/// </summary>
internal static partial class Ranking
{
    public const int Limit = 10;

    /// <summary>How deep each ranking reaches before fusion.</summary>
    private const int Depth = 20;

    /// <summary>The top hits whose edge neighbours rise.</summary>
    private const int Lifted = 3;

    /// <summary>
    /// A neighbour gains at most this share of the hit's score, scaled by w / (w + 1) for edge weight w: enough to pass
    /// a node or two in one ranking, never a node two rankings found. 0.3 and 0.1 lowered the test set's scores.
    /// </summary>
    private const double NeighbourShare = 0.05;

    private const int MinPrefix = 3;
    private const char Hyphen = '-';
    private const char Space = ' ';
    private const string Or = " OR ";
    private const string Quote = "\"";
    private const string Prefix = "*";

    /// <summary>
    /// The top <see cref="Limit"/> sure or likely hits among the nodes of <paramref name="graph"/> that are
    /// <paramref name="visible"/>.
    /// </summary>
    public static Ranked<Hit> Rank(BrainEnv env, Graph graph, Func<PlacedNode, bool> visible, string query)
    {
        var weigher = new EdgeWeigher(env.Today);
        return Rank(env, graph.Scopes, graph.Nodes, [.. graph.Nodes.Where(visible)], query, hit => Neighbours.Of(graph, weigher, hit)
            .Where(neighbour => neighbour.Other is not null)
            .Select(neighbour => (neighbour.Other!, neighbour.Edge.Weight)), Depth, Limit);
    }

    /// <summary>
    /// The same search over <paramref name="nodes"/> of <paramref name="scopes"/>, any graphs at all, whose edge
    /// neighbours and their weights <paramref name="neighbours"/> gives, in or out; but every sure or likely hit and
    /// not the top ones. The window's Home lights them all, so a broad search shows how far it spreads.
    /// </summary>
    public static Ranked<Hit> Spread(
        BrainEnv env,
        IReadOnlyList<Scope> scopes,
        IReadOnlyCollection<PlacedNode> nodes,
        string query,
        Func<PlacedNode, IEnumerable<(PlacedNode Node, double Weight)>> neighbours) =>
        Rank(env, scopes, nodes, [.. nodes], query, neighbours, nodes.Count, nodes.Count);

    /// <summary>
    /// The nodes nearest to <paramref name="document"/>, a whole node, best first, at most <paramref name="limit"/>.
    /// The same fusion as <see cref="Rank"/>, with the document as the query: a node with its slug or title
    /// pinned to the top, then nodes that share its subjects, BM25 over every word of its title, body and why, and the
    /// cosine between its passage and each node's, both embedded alike. <paramref name="candidate"/> says which of
    /// <paramref name="nodes"/> may come back. No neighbour rises: an edge says two nodes are related, not alike. The
    /// snippets carry no marks, since nearly every word of a document matches somewhere. The entries carry no tier: a
    /// node's cosine to another node runs higher than a query's, so a query's tiers do not fit them.
    /// </summary>
    public static Ranked<Found> Nearest(
        BrainEnv env, IReadOnlyList<Scope> scopes, IReadOnlyCollection<PlacedNode> nodes, Func<PlacedNode, bool> candidate, Node document, int limit)
    {
        var candidates = nodes.Where(candidate).ToList();
        var words = Words(string.Join(Space, document.Title, document.Body, document.Why));
        var probe = new Probe(Alike(candidates, document), Sharing(candidates, document), Terms(words, prefix: false), env.Embedder.Profile.Passage(NodeDocument.Passage(document)), Marked: false, Depth);
        return Fuse(env, scopes, nodes, candidates, probe, lift: null, (order, _) => order.Take(limit));
    }

    /// <summary>
    /// The search over <paramref name="candidates"/>, each ranking reaching <paramref name="depth"/> deep, the top
    /// <paramref name="limit"/> sure or likely hits back.
    /// </summary>
    private static Ranked<Hit> Rank(
        BrainEnv env,
        IReadOnlyList<Scope> scopes,
        IReadOnlyCollection<PlacedNode> nodes,
        List<PlacedNode> candidates,
        string query,
        Func<PlacedNode, IEnumerable<(PlacedNode Node, double Weight)>> neighbours,
        int depth,
        int limit)
    {
        var probe = new Probe(Exact(candidates, query), Structure(candidates, query, depth), Terms(Words(query), prefix: true), env.Embedder.Profile.Query(query), Marked: true, depth);
        return Fuse(env, scopes, nodes, candidates, probe, fused => Lift(neighbours, fused), (order, weigh) => order
            .Select(found => new Hit(found.Node, found.Snippet, weigh(found.Node).Tier))
            .Where(hit => hit.Tier != Tier.Weak)
            .Take(limit));
    }

    public static float Cosine(float[] left, float[] right)
    {
        if (left.Length != right.Length)
        {
            return 0;
        }

        float dot = 0, leftNorm = 0, rightNorm = 0;
        for (var i = 0; i < left.Length; i++)
        {
            dot += left[i] * right[i];
            leftNorm += left[i] * left[i];
            rightNorm += right[i] * right[i];
        }

        return leftNorm == 0 || rightNorm == 0 ? 0 : dot / MathF.Sqrt(leftNorm * rightNorm);
    }

    /// <summary>
    /// Refreshes the index from <paramref name="nodes"/> of <paramref name="scopes"/>, fuses the probe's three rankings
    /// of <paramref name="candidates"/>, lets <paramref name="lift"/> raise some, puts the pinned nodes first, and lets
    /// <paramref name="pick"/> take the hits from that order, with a way to weigh a node's evidence. A node's word
    /// share is counted only when the pick weighs one.
    /// </summary>
    private static Ranked<THit> Fuse<THit>(
        BrainEnv env,
        IReadOnlyList<Scope> scopes,
        IReadOnlyCollection<PlacedNode> nodes,
        List<PlacedNode> candidates,
        Probe probe,
        Action<Dictionary<string, (PlacedNode Node, double Score)>>? lift,
        Func<IEnumerable<Found>, Func<PlacedNode, Evidence>, IEnumerable<THit>> pick)
    {
        var path = SearchIndex.PathIn(env.Home);
        try
        {
            using var index = SearchIndex.Open(path);
            IndexRefresh.Texts(index, scopes, nodes);
            var byKey = candidates.ToDictionary(NodeDocument.Key, StringComparer.Ordinal);
            List<(string Key, string? Snippet, double Score)> text = probe.Terms.Count > 0
                ? [.. index.Match(string.Join(Or, probe.Terms), probe.Depth * 2).Where(hit => byKey.ContainsKey(hit.Key)).Take(probe.Depth)]
                : [];
            var snippets = text.ToDictionary(hit => hit.Key, hit => probe.Marked ? hit.Snippet : Unmarked(hit.Snippet), StringComparer.Ordinal);
            var bars = env.Embedder.Profile.Bars;
            var cosines = Cosines(env, index, scopes, nodes, candidates, probe.Embedded);
            var fused = RankFusion.Fuse([probe.Structure, [.. text.Select(hit => byKey[hit.Key])], cosines is null ? [] : [.. ByMeaning(candidates, cosines, bars.Floor, probe.Depth)]]);
            lift?.Invoke(fused);
            var order = probe.Exact
                .Concat(fused.Values.OrderByDescending(hit => hit.Score).ThenBy(hit => hit.Node.Ref, StringComparer.Ordinal).Select(hit => hit.Node))
                .DistinctBy(node => node.Ref, StringComparer.Ordinal)
                .Select(node => new Found(node, snippets.GetValueOrDefault(NodeDocument.Key(node))));
            var pinned = probe.Exact.Select(node => node.Ref).ToHashSet(StringComparer.Ordinal);
            var structure = probe.Structure.Select(node => node.Ref).ToHashSet(StringComparer.Ordinal);
            var words = new Lazy<Func<string, double>>(() => WordShares(index, probe.Terms, [.. byKey.Keys]));
            Evidence Weigh(PlacedNode node)
            {
                var key = NodeDocument.Key(node);
                return new Evidence(pinned.Contains(node.Ref), structure.Contains(node.Ref), words.Value(key), cosines is null ? null : new Meaning(cosines[key], bars));
            }

            return new Ranked<THit>([.. pick(order, Weigh)], cosines is null);
        }
        catch (SqliteException error)
        {
            throw new BrainException($"the search index {path} failed: {error.Message}. It is a cache: delete it and search rebuilds it");
        }
    }

    /// <summary>
    /// The share of the query's weight each key's text holds. A term weighs ln(1 + (n - df + 0.5) / (df + 0.5)), BM25's
    /// idf, for df of the <paramref name="keys"/> that hold it out of n, so a term no candidate holds weighs most.
    /// </summary>
    private static Func<string, double> WordShares(SearchIndex index, IReadOnlyList<string> terms, HashSet<string> keys)
    {
        var holders = terms.Select(term => index.Keys(term).Where(keys.Contains).ToHashSet(StringComparer.Ordinal)).ToList();
        var weights = holders.Select(held => Math.Log(1 + ((keys.Count - held.Count + 0.5) / (held.Count + 0.5)))).ToList();
        var total = weights.Sum();
        return key => total == 0 ? 0 : holders.Select((held, i) => held.Contains(key) ? weights[i] : 0).Sum() / total;
    }

    private static string? Unmarked(string? snippet) =>
        snippet?.Replace(SearchIndex.MarkOpen.ToString(), string.Empty, StringComparison.Ordinal).Replace(SearchIndex.MarkClose.ToString(), string.Empty, StringComparison.Ordinal);

    /// <summary>The nodes with the document's slug or title, ignoring case: the likeliest duplicates.</summary>
    private static List<PlacedNode> Alike(List<PlacedNode> candidates, Node document) =>
        [.. candidates
            .Where(node => (document.Slug.Length > 0 && node.Node.Slug == document.Slug)
                           || (!string.IsNullOrWhiteSpace(document.Title) && string.Equals(node.Node.Title, document.Title, StringComparison.OrdinalIgnoreCase)))
            .OrderBy(node => node.Ref, StringComparer.Ordinal)];

    /// <summary>The nodes that carry one of the document's subjects, the most shared first.</summary>
    private static List<PlacedNode> Sharing(List<PlacedNode> candidates, Node document)
    {
        var subjects = Subjects.Of(document).ToHashSet(StringComparer.Ordinal);
        return [.. candidates
            .Select(node => (Node: node, Count: Subjects.Of(node.Node).Count(subjects.Contains)))
            .Where(hit => hit.Count > 0)
            .OrderByDescending(hit => hit.Count)
            .ThenBy(hit => hit.Node.Ref, StringComparer.Ordinal)
            .Take(Depth)
            .Select(hit => hit.Node)];
    }

    /// <summary>The nodes whose ref, slug or title is the query, ignoring case; a query's spaces stand for a slug's hyphens.</summary>
    private static List<PlacedNode> Exact(List<PlacedNode> candidates, string query)
    {
        var trimmed = query.Trim();
        var slug = AsSlug(query);
        return [.. candidates
            .Where(node => node.Node.Slug == slug
                           || string.Equals(node.Ref, trimmed, StringComparison.OrdinalIgnoreCase)
                           || string.Equals(node.Node.Title, trimmed, StringComparison.OrdinalIgnoreCase))
            .OrderBy(node => node.Ref, StringComparer.Ordinal)];
    }

    /// <summary>The nodes whose title or slug starts with the query, then those that carry the query or one of its words as a subject.</summary>
    private static List<PlacedNode> Structure(List<PlacedNode> candidates, string query, int depth)
    {
        var trimmed = query.Trim();
        var slug = AsSlug(query);
        var prefixed = slug.Length < MinPrefix
            ? []
            : candidates
                .Where(node => (node.Node.Title?.StartsWith(trimmed, StringComparison.OrdinalIgnoreCase) ?? false) || node.Node.Slug.StartsWith(slug, StringComparison.Ordinal))
                .OrderBy(node => node.Ref, StringComparer.Ordinal)
                .ToList();
        var subjects = Words(query).Select(word => word.ToLowerInvariant()).Append(slug).ToHashSet(StringComparer.Ordinal);
        var tagged = candidates
            .Select(node => (Node: node, Count: Subjects.Of(node.Node).Count(subjects.Contains)))
            .Where(hit => hit.Count > 0)
            .OrderByDescending(hit => hit.Count)
            .ThenBy(hit => hit.Node.Ref, StringComparer.Ordinal)
            .Select(hit => hit.Node);
        return [.. prefixed.Concat(tagged).DistinctBy(node => node.Ref, StringComparer.Ordinal).Take(depth)];
    }

    /// <summary>
    /// The distinct words, each quoted as an FTS5 term, which the full text joins by OR so BM25 ranks a node by how
    /// many and how rare; with <paramref name="prefix"/> the last word also matches as a prefix, as a query being
    /// typed.
    /// </summary>
    private static List<string> Terms(List<string> words, bool prefix)
    {
        var distinct = words.Select(word => word.ToLowerInvariant()).Distinct(StringComparer.Ordinal).ToList();
        return [.. distinct.Select((word, i) => Quote + word + Quote + (prefix && i == distinct.Count - 1 && word.Length >= MinPrefix ? Prefix : string.Empty))];
    }

    /// <summary>
    /// Each candidate's cosine to the vector of <paramref name="embedded"/>, by key, or null when the embedder did not
    /// answer.
    /// </summary>
    private static Dictionary<string, float>? Cosines(
        BrainEnv env, SearchIndex index, IReadOnlyList<Scope> scopes, IReadOnlyCollection<PlacedNode> nodes, List<PlacedNode> candidates, string embedded)
    {
        if (env.Embedder.Embed([embedded]) is not [var probe] || IndexRefresh.Vectors(index, env.Embedder, scopes, nodes) is not { } vectors)
        {
            return null;
        }

        return candidates.Select(NodeDocument.Key).ToDictionary(key => key, key => Cosine(probe, vectors[key]), StringComparer.Ordinal);
    }

    /// <summary>
    /// The <paramref name="depth"/> candidates nearest in meaning, each at or above the model's <paramref name="floor"/>.
    /// </summary>
    private static IEnumerable<PlacedNode> ByMeaning(List<PlacedNode> candidates, Dictionary<string, float> cosines, float floor, int depth) =>
        candidates
            .Select(node => (Node: node, Score: cosines[NodeDocument.Key(node)]))
            .Where(hit => hit.Score >= floor)
            .OrderByDescending(hit => hit.Score)
            .ThenBy(hit => hit.Node.Ref, StringComparer.Ordinal)
            .Take(depth)
            .Select(hit => hit.Node);

    /// <summary>Raises each fused node that an edge joins to one of the top hits, in or out, by a share of that hit's score.</summary>
    private static void Lift(Func<PlacedNode, IEnumerable<(PlacedNode Node, double Weight)>> neighbours, Dictionary<string, (PlacedNode Node, double Score)> fused)
    {
        var top = fused.Values.OrderByDescending(hit => hit.Score).ThenBy(hit => hit.Node.Ref, StringComparer.Ordinal).Take(Lifted).ToList();
        var gains = new Dictionary<string, double>(StringComparer.Ordinal);
        foreach (var (hit, score) in top)
        {
            foreach (var (neighbour, weight) in neighbours(hit).Where(pair => fused.ContainsKey(pair.Node.Ref) && pair.Node.Ref != hit.Ref))
            {
                gains[neighbour.Ref] = gains.GetValueOrDefault(neighbour.Ref) + (NeighbourShare * score * weight / (weight + 1));
            }
        }

        foreach (var (reference, gain) in gains)
        {
            fused[reference] = (fused[reference].Node, fused[reference].Score + gain);
        }
    }

    private static string AsSlug(string query) => string.Join(Hyphen, Words(query)).ToLowerInvariant();

    private static List<string> Words(string query) => [.. WordPattern().Matches(query).Select(match => match.Value)];

    [GeneratedRegex(@"[\p{L}\p{N}]+")]
    private static partial Regex WordPattern();
}

/// <summary>
/// What one search asks of each ranking: the nodes pinned on top, the structure ranking, the FTS5 terms, the text to
/// embed, whether snippets keep their marks, and how deep the full text and meaning rankings reach.
/// </summary>
internal sealed record Probe(IReadOnlyList<PlacedNode> Exact, List<PlacedNode> Structure, IReadOnlyList<string> Terms, string Embedded, bool Marked, int Depth);
