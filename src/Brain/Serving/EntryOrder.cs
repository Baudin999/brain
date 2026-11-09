using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace Brain.Serving;

/// <summary>
/// When a node's file entered its graph's repo, and the edges it entered with. The place is that of the commit that first
/// added the file, counted from the repo's first commit. The edges are those the file held in the first commit in which
/// it reads as a node: the commit that added it, or for a file added in the old format the commit that moved it to the
/// new one. Both are fixed history: a new commit only appends, and a later link changes neither.
/// </summary>
/// <param name="Place">The place of the commit that first added the file.</param>
/// <param name="Edges">The edge targets the file entered with, or null while no commit has it in a form that reads.</param>
internal sealed record Entry(int Place, IReadOnlyList<string>? Edges);

/// <summary>
/// The entries of every file under a graph's directory. Walking the history costs too much for every visit to the sky,
/// so they are cached in the brain's cache with the commit they were read at, and a later HEAD walks only the commits
/// after that one. A cache that is gone or that HEAD no longer descends from is read again from the whole history.
/// </summary>
internal static class EntryOrder
{
    private const string Dir = "sky";
    private const string Extension = ".tsv";
    private const char Tab = '\t';
    private const char Colon = ':';
    private const string Placed = "=";
    private const string Waiting = "?";
    private const int KeyLength = 16;

    /// <summary>The entry of each node file git ever added under <paramref name="root"/>, by its path relative to root.</summary>
    public static IReadOnlyDictionary<string, Entry> Of(string home, string root)
    {
        if (Git.Head(root) is not { } head)
        {
            return new Dictionary<string, Entry>(StringComparer.Ordinal);
        }

        var path = Path.Combine(home, Layout.CacheDir, Dir, Key(root) + Extension);
        var (read, entries) = Read(path);
        if (read == head)
        {
            return entries;
        }

        var since = read is not null && Git.IsAncestor(root, read, head) ? read : null;
        if (since is null)
        {
            entries.Clear();
        }

        if (Git.Prefix(root) is not { } prefix)
        {
            return entries;
        }

        var commits = Git.Changes(root, since);

        // Each file still waiting for edges, and every commit since that added or changed it, oldest first.
        var tries = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        var next = entries.Count == 0 ? 0 : entries.Values.Max(entry => entry.Place) + 1;
        foreach (var (commit, files) in commits)
        {
            foreach (var (status, file) in files.Where(file => file.Path.StartsWith(prefix, StringComparison.Ordinal) && file.Path.EndsWith(Layout.NodeExtension, StringComparison.Ordinal)))
            {
                var key = file[prefix.Length..];
                if (status == 'A')
                {
                    entries.TryAdd(key, new Entry(next, null));
                }

                if (status is 'A' or 'M' && entries.TryGetValue(key, out var entry) && entry.Edges is null)
                {
                    (tries.TryGetValue(key, out var list) ? list : tries[key] = []).Add(commit);
                }
            }

            next++;
        }

        Settle(root, prefix, entries, tries);

        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        AtomicFile.Write(path, string.Join(NodeSyntax.NewLine, entries.Select(pair => Line(pair.Key, pair.Value)).Prepend(head)) + NodeSyntax.NewLine);
        return entries;
    }

    /// <summary>
    /// Gives each waiting file the edges of the first of its commits in which it reads as a node, reading one commit of
    /// every waiting file per git call, until each has edges or has no commit left.
    /// </summary>
    private static void Settle(string root, string prefix, Dictionary<string, Entry> entries, Dictionary<string, List<string>> tries)
    {
        var at = tries.Keys.ToDictionary(key => key, _ => 0, StringComparer.Ordinal);
        while (at.Count > 0)
        {
            var round = at.Select(pair => (Key: pair.Key, Commit: tries[pair.Key][pair.Value])).ToList();
            var texts = Git.Blobs(root, [.. round.Select(one => one.Commit + Colon + prefix + one.Key)]);
            for (var i = 0; i < round.Count; i++)
            {
                var key = round[i].Key;
                var node = texts[i] is { } text ? NodeParser.Parse(Path.GetFileNameWithoutExtension(key), text) : null;
                if (node is not null && !node.Malformed.Contains(NodeParser.OldFormat, StringComparer.Ordinal))
                {
                    entries[key] = entries[key] with { Edges = [.. node.Edges.Select(edge => edge.Target)] };
                    at.Remove(key);
                }
                else if (++at[key] == tries[key].Count)
                {
                    at.Remove(key);
                }
            }
        }
    }

    /// <summary>One file's line: its place, its path, then = and its edge targets, or ? while it waits for them.</summary>
    private static string Line(string key, Entry entry) =>
        string.Join(Tab, [entry.Place.ToString(CultureInfo.InvariantCulture), key, entry.Edges is null ? Waiting : Placed, .. entry.Edges ?? []]);

    /// <summary>The commit the cache was read at and its entries, or no commit and nothing when it is missing or unreadable.</summary>
    private static (string? Head, Dictionary<string, Entry> Entries) Read(string path)
    {
        var entries = new Dictionary<string, Entry>(StringComparer.Ordinal);
        if (!File.Exists(path))
        {
            return (null, entries);
        }

        var lines = File.ReadAllLines(path);
        foreach (var line in lines.Skip(1))
        {
            var parts = line.Split(Tab);
            if (parts.Length < 3 || !int.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out var place) || parts[2] is not (Placed or Waiting))
            {
                return (null, new Dictionary<string, Entry>(StringComparer.Ordinal));
            }

            entries[parts[1]] = new Entry(place, parts[2] == Placed ? parts[3..] : null);
        }

        return (lines.FirstOrDefault(), entries);
    }

    private static string Key(string root) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(Path.GetFullPath(root))))[..KeyLength];
}
