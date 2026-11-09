using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using System.Text.RegularExpressions;
using Brain.Usage;

namespace Brain.Staging;

/// <summary>
/// The pending writes of one brain, in its gitignored <c>cache/pending</c>: working state, never graph content. A staged
/// write waits as <c>&lt;id&gt;.json</c>. <c>brain land</c> puts it before the user: it moves to <c>asked/&lt;id&gt;.json</c>,
/// which brain's window lists. Only the window answers it, through <c>brain serve</c>: the answer claims the asked write,
/// lands it on a yes, and leaves <c>answers/&lt;id&gt;.json</c> for <c>brain land</c> to return. Every answer is also kept
/// for two days in <c>history/&lt;id&gt;.json</c>, the write exactly as it was asked with its answer and its time, so the
/// user can find it in the window and undo it. An undone yes drops its answer and commits what it changed. Every file is
/// written whole through a rename.
/// </summary>
internal sealed partial class PendingStore
{
    private const string JsonExtension = ".json";
    private const string ClaimExtension = ".claim";
    private const string AskedDir = "asked";
    private const string AnswersDir = "answers";
    private const string HistoryDir = "history";
    private const string PendingDir = "pending";
    private const string GraphDir = "graph";
    private const int IdBytes = 6;
    private static readonly TimeSpan Kept = TimeSpan.FromDays(1);
    private static readonly TimeSpan Remembered = TimeSpan.FromDays(2);

    private readonly string home;
    private readonly string root;

    public PendingStore(string home)
    {
        this.home = home;
        root = Path.Combine(home, Layout.CacheDir, PendingDir);
    }

    /// <summary>The directory the store keeps its files in.</summary>
    public string Root => root;

    private string AskedRoot => Path.Combine(root, AskedDir);

    private string Answers => Path.Combine(root, AnswersDir);

    private string History => Path.Combine(root, HistoryDir);

    /// <summary>Keeps a new pending write under a fresh id, and sweeps away what waited longer than a day.</summary>
    public PendingWrite Stage(IReadOnlyList<string> lines, IReadOnlyList<StagedChange> changes, WriteOrigin origin)
    {
        Sweep();
        Directory.CreateDirectory(root);
        var write = new PendingWrite(Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(IdBytes)), lines, changes, origin);
        AtomicFile.Write(Waiting(write.Id), JsonSerializer.Serialize(write, PendingJson.Default.PendingWrite));
        return write;
    }

    /// <summary>True when the write was staged and nobody put it before the user yet.</summary>
    public bool IsWaiting(string id) => IsId(id) && File.Exists(Waiting(id));

    /// <summary>True while the write is before the user or an answer to it is landing.</summary>
    public bool IsAsked(string id) => IsId(id) && (File.Exists(AskedPath(id)) || File.Exists(ClaimPath(id)));

    /// <summary>Puts a waiting write before the user. A write already before the user stays there.</summary>
    public void Ask(string id)
    {
        if (IsAsked(id))
        {
            return;
        }

        if (!IsWaiting(id))
        {
            throw new BrainException($"no pending write {id}: it landed, the user said no, or it was never staged; stage it again");
        }

        Directory.CreateDirectory(AskedRoot);
        File.Move(Waiting(id), AskedPath(id), overwrite: false);
    }

    /// <summary>
    /// Every write before the user, oldest first, each with the digest an answer must name. Only a file named for its own
    /// id counts, since an answer reads <c>asked/&lt;id&gt;.json</c>; anything else, such as an old asked write kept under a
    /// tool use id, is not before the user.
    /// </summary>
    public IReadOnlyList<AskedWrite> Asked()
    {
        if (!Directory.Exists(AskedRoot))
        {
            return [];
        }

        var asked = new List<AskedWrite>();
        foreach (var path in Directory.EnumerateFiles(AskedRoot, "*" + JsonExtension))
        {
            if (ReadText(path) is { } text && Parse(text, PendingJson.Default.PendingWrite) is { } write && IsId(write.Id)
                && string.Equals(Path.GetFileName(path), write.Id + JsonExtension, StringComparison.Ordinal))
            {
                asked.Add(new AskedWrite(write, Digest(text), File.GetLastWriteTimeUtc(path)));
            }
        }

        return [.. asked.OrderBy(write => write.AskedAt).ThenBy(write => write.Write.Id, StringComparer.Ordinal)];
    }

    /// <summary>The user's answer to the write, once one landed or was refused; null while it waits.</summary>
    public PendingAnswer? AnswerOf(string id)
    {
        if (!IsId(id) || ReadText(AnswerPath(id)) is not { } text)
        {
            return null;
        }

        return Parse(text, PendingJson.Default.PendingAnswer);
    }

    /// <summary>
    /// Answers the write before the user whose text hashes to <paramref name="digest"/>, so what the user read is what
    /// lands. A yes lands it when every file still holds the text it held when it was staged; a file that changed refuses
    /// the whole write. A yes lands every file or none: one that cannot be written refuses the whole write, leaves the
    /// graph as it was, and is an error in the usage log whose line the refusal carries. A no drops it, with the user's
    /// own words when they said what to change. Every answer is written to the usage log here, the one place an answer
    /// is received, so it is logged once.
    /// </summary>
    public PendingAnswer Answer(string id, string digest, bool yes, string? reason)
    {
        BrainException.ThrowIf(IsId(id) ? null : $"{id} is not a pending write id");
        var asked = AskedPath(id);
        var text = ReadText(asked) ?? throw new BrainException($"pending write {id} is not before the user: it was answered, or never asked");
        BrainException.ThrowIf(Shows(text, digest) ? null : $"pending write {id} is not the text the window showed; nothing landed");
        var write = Parse(text, PendingJson.Default.PendingWrite) ?? throw new BrainException($"pending write {id} cannot be read");
        Directory.CreateDirectory(Answers);
        var claim = ClaimPath(id);
        try
        {
            File.Move(asked, claim, overwrite: false);
        }
        catch (IOException)
        {
            throw new BrainException($"pending write {id} is being answered already");
        }

        PendingAnswer answer;
        try
        {
            answer = yes ? Land(write) : new PendingAnswer(id, PendingAnswer.No, string.IsNullOrWhiteSpace(reason) ? null : reason.Trim(), [], null);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            var told = Failures.Record(home, $"pending write {id}", error, write.Origin?.Session, Console.Error);
            answer = new PendingAnswer(id, PendingAnswer.Refused, null, [], told);
        }

        AtomicFile.Write(AnswerPath(id), JsonSerializer.Serialize(answer, PendingJson.Default.PendingAnswer));
        Remember(new AnswerRecord(write, answer, DateTime.UtcNow));
        File.Delete(claim);
        UsageLog.Append(home, new UsageEvent(UsageLog.Now(), UsageEvent.Answer, write.Origin?.Session)
        {
            Id = id,
            Verdict = yes ? PendingAnswer.Yes : answer.Reason is null ? PendingAnswer.No : UsageEvent.Change,
            Landed = yes ? answer.Verdict == PendingAnswer.Yes : null,
            Title = TitleOf(write),
            Words = answer.Reason,
        }, Console.Error);
        return answer;
    }

    /// <summary>The title of the first node <paramref name="write"/> changes, as it leaves it or, for a forget, as it read.</summary>
    private static string? TitleOf(PendingWrite write) =>
        write.Changes is [var change, ..] && (change.After ?? change.Before) is { } text
            ? NodeParser.Parse(Path.GetFileNameWithoutExtension(change.Path), text).Title
            : null;

    private static PendingAnswer Land(PendingWrite write)
    {
        foreach (var change in write.Changes)
        {
            if (OutsideGraph(change) is { } problem)
            {
                return new PendingAnswer(write.Id, PendingAnswer.Refused, null, [], problem);
            }

            if (!Holds(change))
            {
                return new PendingAnswer(write.Id, PendingAnswer.Refused, null, [], $"{change.Ref} changed since pending write {write.Id} was staged; nothing landed, stage it again");
            }
        }

        Apply(write);
        return new PendingAnswer(write.Id, PendingAnswer.Yes, null, write.Lines, null);
    }

    /// <summary>
    /// Every write the user answered in the last two days, the newest first, each with the digest an undo must name. Only
    /// a record named for its own write's id counts, as in <see cref="Asked"/>.
    /// </summary>
    public IReadOnlyList<AnsweredWrite> Answered()
    {
        if (!Directory.Exists(History))
        {
            return [];
        }

        var since = DateTime.UtcNow - Remembered;
        var answered = new List<AnsweredWrite>();
        foreach (var path in Directory.EnumerateFiles(History, "*" + JsonExtension))
        {
            if (ReadText(path) is { } text && Parse(text, PendingJson.Default.AnswerRecord) is { } record && record.AnsweredAt >= since && IsId(record.Write.Id)
                && string.Equals(Path.GetFileName(path), record.Write.Id + JsonExtension, StringComparison.Ordinal))
            {
                answered.Add(new AnsweredWrite(record, Digest(text)));
            }
        }

        return [.. answered.OrderByDescending(write => write.Record.AnsweredAt).ThenBy(write => write.Record.Write.Id, StringComparer.Ordinal)];
    }

    /// <summary>
    /// Undoes the user's answer to the write whose record hashes to <paramref name="digest"/>, so what the user read is
    /// what is undone, and says what it undid. A yes is reversed exactly: each node the write changed gets back the text
    /// it held before, a node it added goes and one it forgot comes back, every one or none. It refuses when a node no
    /// longer holds the text the yes left, or when a node outside the write links to a node the undo removes, since that
    /// edge would lead nowhere. An undone yes drops the answer <c>brain land</c> returns, so the write no longer reads as
    /// landed, and commits the node files it changed and nothing else, in each repo that holds them. A commit that fails
    /// leaves the undo standing and says why. A no puts the write before the user again, as it was asked, and commits
    /// nothing. A yes that was refused landed nothing and has nothing to undo. The record keeps the undo, so an answer is
    /// undone once.
    /// </summary>
    public Undone Undo(string id, string digest, Graph graph)
    {
        BrainException.ThrowIf(IsId(id) ? null : $"{id} is not a pending write id");
        var path = HistoryPath(id);
        var text = ReadText(path) ?? throw new BrainException($"no answer to {id} is kept: it was never answered, or two days passed");
        BrainException.ThrowIf(Shows(text, digest) ? null : $"the answer to {id} is not the one the window showed; nothing was undone");
        var record = Parse(text, PendingJson.Default.AnswerRecord) ?? throw new BrainException($"the answer to {id} cannot be read");
        BrainException.ThrowIf(record.UndoneAt is null ? null : $"the answer to {id} was undone already");
        switch (record.Answer.Verdict)
        {
            case PendingAnswer.Yes:
                Unland(record.Write, graph);
                break;
            case PendingAnswer.No:
                AskAgain(record.Write);
                break;
            default:
                throw new BrainException($"nothing landed on your yes to {id}, so there is nothing to undo");
        }

        Remember(record with { UndoneAt = DateTime.UtcNow });
        if (record.Answer.Verdict == PendingAnswer.No)
        {
            return new Undone(PendingAnswer.No, [], null);
        }

        File.Delete(AnswerPath(id));
        return Commit(record.Write);
    }

    /// <summary>
    /// Commits the node files an undone <paramref name="write"/> changed, in each repo that holds one, under one message
    /// that names the write. The message is never a node's title, since a later title may not repeat a commit subject.
    /// The first repo that cannot commit stops the rest, and the failure is said, never thrown: the undo stands.
    /// </summary>
    private static Undone Commit(PendingWrite write)
    {
        var message = $"Undo of brain write {write.Id}";
        var commits = new List<string>();
        try
        {
            var places = new List<(string Repo, string Path)>();
            foreach (var change in write.Changes)
            {
                var place = Git.Place(change.Path) ?? throw new BrainException($"{change.Ref} lies in no git repo");
                places.Add(place);
            }

            foreach (var repo in places.GroupBy(place => place.Repo, StringComparer.Ordinal))
            {
                if (Git.CommitFiles(repo.Key, [.. repo.Select(place => place.Path).Distinct(StringComparer.Ordinal)], message) is { } commit)
                {
                    commits.Add(commit);
                }
            }
        }
        catch (BrainException failure)
        {
            return new Undone(PendingAnswer.Yes, commits, failure.Message);
        }

        return new Undone(PendingAnswer.Yes, commits, null);
    }

    /// <summary>
    /// Keeps <paramref name="record"/> as its write's history. The file keeps the time of the answer as its own, so the
    /// sweep counts two days from the answer, also once it was undone.
    /// </summary>
    private void Remember(AnswerRecord record)
    {
        Directory.CreateDirectory(History);
        var path = HistoryPath(record.Write.Id);
        AtomicFile.Write(path, JsonSerializer.Serialize(record, PendingJson.Default.AnswerRecord));
        File.SetLastWriteTimeUtc(path, record.AnsweredAt);
    }

    /// <summary>
    /// Reverses every change <paramref name="write"/> made on the user's yes, through the same all-or-nothing landing a
    /// yes takes, once every node still holds the text the yes left and no other node links to one the undo removes.
    /// </summary>
    private void Unland(PendingWrite write, Graph graph)
    {
        var back = write with { Changes = [.. write.Changes.Reverse().Select(change => change with { Before = change.After, After = change.Before })] };
        foreach (var change in back.Changes)
        {
            BrainException.ThrowIf(OutsideGraph(change));
        }

        var changed = back.Changes.Where(change => !Holds(change)).Select(change => change.Ref).ToList();
        BrainException.ThrowIf(changed.Count == 0 ? null : $"{string.Join(", ", changed)} changed since your yes to {write.Id}; nothing was undone");
        var links = LinksInto(graph, back.Changes);
        BrainException.ThrowIf(
            links.Count == 0 ? null : $"{string.Join(", ", links)} since your yes to {write.Id}, and undoing it would leave that edge leading nowhere; nothing was undone");
        try
        {
            Apply(back);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            throw new BrainException(Failures.Record(home, $"undo of {write.Id}", error, write.Origin?.Session, Console.Error));
        }
    }

    /// <summary>The edges of nodes outside <paramref name="changes"/> into a node the changes remove, each written as faded writes an edge.</summary>
    private static List<string> LinksInto(Graph graph, IReadOnlyList<StagedChange> changes)
    {
        var removed = changes.Where(change => change.After is null).Select(change => Path.GetFullPath(change.Path)).ToHashSet(StringComparer.Ordinal);
        if (removed.Count == 0)
        {
            return [];
        }

        var touched = changes.Select(change => Path.GetFullPath(change.Path)).ToHashSet(StringComparer.Ordinal);
        return
        [
            .. InboundEdges.Every(graph, node => removed.Contains(PathOf(node)))
                .Where(edge => !touched.Contains(PathOf(edge.From)))
                .Select(edge => edge.Text),
        ];
    }

    private static string PathOf(PlacedNode node) => Path.GetFullPath(NodeStore.PathOf(node.Scope, node.Node.Slug));

    /// <summary>Puts a write the user said no to before the user again, as it was asked, and drops the answer that was returned.</summary>
    private void AskAgain(PendingWrite write)
    {
        BrainException.ThrowIf(IsAsked(write.Id) || IsWaiting(write.Id) ? $"pending write {write.Id} waits already" : null);
        Directory.CreateDirectory(AskedRoot);
        AtomicFile.Write(AskedPath(write.Id), JsonSerializer.Serialize(write, PendingJson.Default.PendingWrite));
        File.Delete(AnswerPath(write.Id));
    }

    /// <summary>True when the node file holds the text the change starts from: its before, or no file for none.</summary>
    private static bool Holds(StagedChange change)
    {
        var now = File.Exists(change.Path) ? File.ReadAllText(change.Path) : null;
        return string.Equals(now, change.Before, StringComparison.Ordinal);
    }

    /// <summary>
    /// Lands every change of <paramref name="write"/> or none. Each new text is written to a temp file beside its node
    /// first, so a full disk or a refused directory stops the write before any node changed. Only then does each temp file
    /// replace its node and each forget delete one; when one of those fails, the nodes already changed get the text they
    /// held back, which every change carries, and the failure goes on. A directory made for the write goes again with it.
    /// </summary>
    private static void Apply(PendingWrite write)
    {
        var changes = write.Changes;
        var temps = new string?[changes.Count];
        var made = new List<string>();
        var done = new List<StagedChange>();
        try
        {
            for (var i = 0; i < changes.Count; i++)
            {
                if (changes[i].After is { } after)
                {
                    Make(Path.GetDirectoryName(changes[i].Path)!, made);
                    temps[i] = AtomicFile.TempFor(changes[i].Path);
                    File.WriteAllText(temps[i]!, after);
                }
            }

            for (var i = 0; i < changes.Count; i++)
            {
                if (temps[i] is { } temp)
                {
                    File.Move(temp, changes[i].Path, overwrite: true);
                }
                else
                {
                    File.Delete(changes[i].Path);
                }

                done.Add(changes[i]);
            }
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            Restore(write, done, error);
            throw;
        }
        finally
        {
            foreach (var temp in temps.OfType<string>())
            {
                File.Delete(temp);
            }

            if (done.Count < changes.Count)
            {
                foreach (var directory in Enumerable.Reverse(made).Where(directory => !Directory.EnumerateFileSystemEntries(directory).Any()))
                {
                    Directory.Delete(directory);
                }
            }
        }
    }

    /// <summary>Makes <paramref name="directory"/> and its missing parents, and keeps each one it made, outermost first.</summary>
    private static void Make(string directory, List<string> made)
    {
        var missing = new List<string>();
        for (var at = directory; !Directory.Exists(at); at = Path.GetDirectoryName(at)!)
        {
            missing.Insert(0, at);
        }

        Directory.CreateDirectory(directory);
        made.AddRange(missing);
    }

    /// <summary>
    /// Gives each node in <paramref name="done"/> back the text it held when the write was staged. When even that fails,
    /// the failure says which nodes hold what, so the user is told the graph is half-way and where.
    /// </summary>
    private static void Restore(PendingWrite write, List<StagedChange> done, Exception cause)
    {
        foreach (var change in Enumerable.Reverse(done))
        {
            try
            {
                if (change.Before is null)
                {
                    File.Delete(change.Path);
                }
                else
                {
                    AtomicFile.Write(change.Path, change.Before);
                }
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException)
            {
                var stuck = string.Join(", ", done.Select(one => one.Ref));
                throw new IOException($"pending write {write.Id} failed while it landed ({cause.Message}), and {change.Ref} could not get its old text back ({error.Message}); {stuck} may hold the new text, so check them with brain node", cause);
            }
        }
    }

    private static bool IsId(string id) => IdPattern().IsMatch(id);

    private static string Digest(string text) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text)));

    /// <summary>True when <paramref name="text"/> hashes to <paramref name="digest"/>: it is what the window showed.</summary>
    private static bool Shows(string text, string digest) =>
        CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(Digest(text)), Encoding.UTF8.GetBytes(digest));

    /// <summary>A pending write changes node files only: a .md file in a directory named graph or in one of its scopes.</summary>
    private static string? OutsideGraph(StagedChange change)
    {
        var parent = Path.GetDirectoryName(change.Path);
        var node = change.Path.EndsWith(Layout.NodeExtension, StringComparison.Ordinal)
            && (IsGraphDir(parent) || IsGraphDir(Path.GetDirectoryName(parent)));
        return node ? null : $"{change.Path} is not a node file; nothing landed";
    }

    private static bool IsGraphDir(string? directory) =>
        string.Equals(Path.GetFileName(directory), GraphDir, StringComparison.Ordinal);

    /// <summary>Removes every pending write, asked write and answer older than a day, and every answer record older than two.</summary>
    private void Sweep()
    {
        foreach (var dir in new[] { root, AskedRoot, Answers })
        {
            SweepOlder(dir, Kept);
        }

        SweepOlder(History, Remembered);
    }

    private static void SweepOlder(string dir, TimeSpan age)
    {
        if (!Directory.Exists(dir))
        {
            return;
        }

        var cutoff = DateTime.UtcNow - age;
        foreach (var file in Directory.EnumerateFiles(dir).Where(file => File.GetLastWriteTimeUtc(file) < cutoff))
        {
            File.Delete(file);
        }
    }

    private string Waiting(string id) => Path.Combine(root, id + JsonExtension);

    private string AskedPath(string id) => Path.Combine(AskedRoot, id + JsonExtension);

    private string ClaimPath(string id) => Path.Combine(Answers, id + ClaimExtension);

    private string AnswerPath(string id) => Path.Combine(Answers, id + JsonExtension);

    private string HistoryPath(string id) => Path.Combine(History, id + JsonExtension);

    private static string? ReadText(string path)
    {
        try
        {
            return File.ReadAllText(path);
        }
        catch (Exception error) when (error is FileNotFoundException or DirectoryNotFoundException)
        {
            return null;
        }
    }

    private static T? Parse<T>(string text, JsonTypeInfo<T> type)
    {
        try
        {
            return JsonSerializer.Deserialize(text, type);
        }
        catch (JsonException)
        {
            return default;
        }
    }

    [GeneratedRegex("^[0-9a-f]{12}$", RegexOptions.CultureInvariant)]
    private static partial Regex IdPattern();
}
