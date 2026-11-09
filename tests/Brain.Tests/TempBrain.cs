using System.Diagnostics;
using System.Globalization;
using System.Text.RegularExpressions;
using Brain.Search;
using Brain.Staging;

namespace Brain.Tests;

/// <summary>A throwaway brain home, transcripts dir, user home and optional project repo under the temp dir. Never the real graph.</summary>
internal sealed partial class TempBrain : IDisposable
{
    public static readonly DateOnly Today = new(2026, 10, 3);

    private const string Asks = "brain asks before it writes:";

    public TempBrain()
    {
        Root = Path.Combine(Path.GetTempPath(), "brain-tests-" + Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture));
        Home = Path.Combine(Root, "home");
        Transcripts = Path.Combine(Root, "transcripts");
        Cwd = Root;
        Directory.CreateDirectory(Path.Combine(Home, "graph", "global"));
        Directory.CreateDirectory(Path.Combine(Home, "graph", "personal"));
        Directory.CreateDirectory(Transcripts);
    }

    /// <summary>The subject a test's node write carries, as every node write must carry one.</summary>
    public const string Subject = "test";

    public string Root { get; }

    public string Home { get; }

    public string Transcripts { get; }

    public string Cwd { get; set; }

    public DateOnly Date { get; set; } = Today;

    public string? Session { get; set; }

    /// <summary>
    /// True answers yes to every pending write a command stages, as brain's window does: it asks the write, answers yes
    /// with its digest, and runs <c>land</c>, which returns the answer. Every test but the gate's says yes, so it reads
    /// what it wrote at once; the gate's tests set it false and answer themselves.
    /// </summary>
    public bool SaysYes { get; set; } = true;

    /// <summary>The judge every command meets. It passes everything until a test gives it a refusal.</summary>
    public FakeJudge Judge { get; } = new();

    /// <summary>The embedder lookup meets. Its index lands under the temp home's cache/.</summary>
    public FakeEmbedder Embedder { get; } = new();

    /// <summary>The embedder commands meet in place of <see cref="Embedder"/>, when a test sets one.</summary>
    public IEmbedder? Running { get; set; }

    /// <summary>The models a dream asks. None answers until a test gives it an answer.</summary>
    public FakeModels Models { get; } = new();

    /// <summary>The program that runs the open agent: none on PATH, so nothing starts claude until a test names a stand-in.</summary>
    public string Claude { get; set; } = "brain-tests-no-such-claude";

    public string IndexPath => Path.Combine(Home, "cache", "search.sqlite");

    public string LogPath => Path.Combine(Home, "cache", "traversals", "global.tsv");

    public (int Code, string Out, string Err) Run(params string[] arguments) => RunWithInput(string.Empty, arguments);

    /// <summary>
    /// Runs one command. When <see cref="SaysYes"/> holds and the command staged a pending write, it lands the write and
    /// returns what the command printed before the prompt, then the lines the write printed, as the command line prints
    /// them once the user said yes.
    /// </summary>
    public (int Code, string Out, string Err) RunWithInput(string input, params string[] arguments)
    {
        var (code, output, errors) = Once(input, arguments);
        var pending = PendingId().Match(output);
        if (!SaysYes || code != 0 || !pending.Success)
        {
            return (code, output, errors);
        }

        var id = pending.Groups[1].Value;
        var store = new PendingStore(Home);
        store.Ask(id);
        store.Answer(id, store.Asked().Single(asked => asked.Write.Id == id).Digest, yes: true, reason: null);
        var landed = Once(string.Empty, "land", id);
        var before = output[..output.IndexOf(Asks, StringComparison.Ordinal)];
        var end = landed.Out.LastIndexOf("landed ", StringComparison.Ordinal);
        var lines = end < 0 ? landed.Out : landed.Out[..end];
        return (landed.Code, before + lines, errors + landed.Err);
    }

    /// <summary>Makes <paramref name="directory"/> a git repo with a local identity and commits everything in it.</summary>
    public static void CommitAll(string directory)
    {
        if (!Directory.Exists(Path.Combine(directory, ".git")))
        {
            Git(directory, "init", "-q");
        }

        Git(directory, "config", "user.name", "t");
        Git(directory, "config", "user.email", "t@t");
        Git(directory, "config", "commit.gpgsign", "false");

        Git(directory, "add", "-A");
        Git(directory, "commit", "-q", "--allow-empty", "-m", "start");
    }

    public string Ok(params string[] arguments)
    {
        var (code, output, errors) = Run(arguments);
        Assert.True(code == 0, $"brain {string.Join(' ', arguments)} exited {code}: {errors}");
        return output;
    }

    /// <summary>Makes a git repo with a project brain and one commit, and moves the cwd into it.</summary>
    public string MakeProject()
    {
        var repo = Path.Combine(Root, "repo");
        Directory.CreateDirectory(Path.Combine(repo, "docs", "brain", "graph"));
        Git(repo, "init", "-q");
        Git(repo, "-c", "user.name=t", "-c", "user.email=t@t", "commit", "-q", "--allow-empty", "-m", "first");
        Cwd = repo;
        return Git(repo, "rev-parse", "HEAD").Trim();
    }

    /// <summary>Runs <c>brain init</c> in the cwd's repo.</summary>
    public string Init(params string[] arguments) => Ok(["init", .. arguments]);

    /// <summary>Makes an empty git repo named <paramref name="name"/> under the temp root and moves the cwd into it.</summary>
    public string MakeRepo(string name)
    {
        var repo = Path.Combine(Root, name);
        Directory.CreateDirectory(repo);
        Git(repo, "init", "-q");
        Cwd = repo;
        return repo;
    }

    /// <summary>Writes a node file by hand: the frontmatter lines, the title and the edge lines, with no body and no why.</summary>
    public void WriteNode(string scope, string slug, string front, string title, string edges = "") =>
        File.WriteAllText(Path.Combine(Home, "graph", scope, slug + ".md"), $"---\n{front}---\n# {title}\n\n## Edges\n{edges}");

    public string NodeText(string scope, string slug) => File.ReadAllText(Path.Combine(Home, "graph", scope, slug + ".md"));

    public void Dispose() => Directory.Delete(Root, recursive: true);

    private (int Code, string Out, string Err) Once(string input, params string[] arguments)
    {
        using var reader = new StringReader(input);
        using var output = new StringWriter(CultureInfo.InvariantCulture);
        using var errors = new StringWriter(CultureInfo.InvariantCulture);
        var env = Env(reader, output, errors);
        var code = Cli.Guard(arguments, Home, Session, errors, () => Cli.Run(env, arguments));
        return (code, output.ToString(), errors.ToString());
    }

    /// <summary>The world a command of this brain runs in, with the given streams.</summary>
    public BrainEnv Env(TextReader input, TextWriter output, TextWriter errors) =>
        new(Home, Transcripts, Root, Cwd, Date, input, output, errors, Session, Judge, Running ?? Embedder, Models, Claude: Claude);

    [GeneratedRegex("pending ([0-9a-f]{12}): nothing is written yet")]
    private static partial Regex PendingId();

    public static string Git(string directory, params string[] arguments)
    {
        var start = new ProcessStartInfo("git") { RedirectStandardOutput = true, UseShellExecute = false, WorkingDirectory = directory };
        foreach (var argument in arguments)
        {
            start.ArgumentList.Add(argument);
        }

        using var process = Process.Start(start)!;
        var output = process.StandardOutput.ReadToEnd();
        process.WaitForExit();
        Assert.Equal(0, process.ExitCode);
        return output;
    }
}
