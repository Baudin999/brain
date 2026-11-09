using System.Text.Json;
using Brain.Serving;
using Brain.Stats;

namespace Brain.Tests;

/// <summary>
/// The Navarone tab's reading: every problem's story from the events column of sources.tsv, the code under a hash its
/// story names from the code column, and a lens on a listed problem from the context column, run in its worktree.
/// </summary>
public sealed class NavaroneTabTests : IDisposable
{
    private const string Before = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
    private const string After = "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb";
    private const string Unknown = "cccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccc";
    private const string Classify = "p::N.DeterministicProblemClassifier.Classify(ProblemCase)";

    private readonly TempBrain brain = new();
    private readonly string repo;
    private readonly string worktree;

    public NavaroneTabTests()
    {
        repo = Directory.CreateDirectory(Path.Combine(brain.Root, "studio")).FullName;
        worktree = Directory.CreateDirectory(Path.Combine(repo, ".claude", "worktrees", "slice")).FullName;
        File.WriteAllText(Path.Combine(brain.Home, "projects.tsv"), $"name\tpath\tremote\tdate\nstudio\t{repo}\t\t2026-10-03\n");
    }

    private NavaroneTab Tab => new(brain.Env(TextReader.Null, TextWriter.Null, TextWriter.Null));

    [Fact]
    public void WithoutANavaroneLineThereIsNoProblem()
    {
        Assert.Empty(Tab.List());
    }

    [Fact]
    public void EachProblemTellsItsStoryAndWhereItStandsTheLatestFirst()
    {
        Write(
            Event("raised", worktree, ("id", "1"), ("check", "cyclomatic"), ("symbol", Classify)),
            Event("run", worktree, ("tokens", 40)),
            Event("raised", "/elsewhere", ("id", "1"), ("check", "lcom4"), ("symbol", "p::N.Shop")),
            Event("fixed", worktree, ("id", "1"), ("check", "cyclomatic"), ("symbol", Classify), ("before", Before), ("after", After)),
            Event("refused-fix", worktree, ("id", "1"), ("finding", "still a chain"), ("before", Before), ("after", After)));

        var problems = Tab.List();

        Assert.Equal([(worktree, "1"), ("/elsewhere", "1")], problems.Select(problem => (problem.Worktree, problem.Id)));
        var first = problems[0];
        Assert.Equal("DeterministicProblemClassifier.Classify", first.Method);
        Assert.Equal("cyclomatic", first.Check);
        Assert.Equal("refused", first.State);
        Assert.Equal("slice", first.Branch);
        Assert.Equal(["raised", "fixed", "refused-fix"], first.Story.Select(step => step.Type));
        Assert.Equal("still a chain", first.Story[2].Finding);
        Assert.Equal(["studio"], first.Graphs.Select(graph => graph.Name));
        Assert.Null(problems[1].Method);
        Assert.Equal("Shop", problems[1].Name);
        Assert.Equal("open", problems[1].State);
    }

    [Fact]
    public void CodeIsReadOnlyForAHashAShownStoryNames()
    {
        Write(Event("fixed", worktree, ("id", "1"), ("check", "cc"), ("symbol", Classify), ("before", Before), ("after", After)));
        var tab = Tab;

        var unlisted = Assert.Throws<BrainException>(() => tab.Code(Before));
        tab.List();
        var code = tab.Code(After);
        var unknown = Assert.Throws<BrainException>(() => tab.Code(Unknown));
        var word = Assert.Throws<BrainException>(() => tab.Code("--help"));

        Assert.Contains("refresh the Navarone tab", unlisted.Message, StringComparison.Ordinal);
        Assert.Equal("code of " + After, code.Code);
        Assert.Contains("no problem in this window names code", unknown.Message, StringComparison.Ordinal);
        Assert.Contains("is no hash", word.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void TheLensRunsInTheProblemsWorktreeWithTheQueryWordByWordAndLightsTheEscalatedLine()
    {
        Write(Event("escalated", worktree, ("id", "1"), ("check", "cc"), ("symbol", Classify), ("text", "A chain."), ("path", "src/Navarone.Classifiers/Deterministic/DeterministicProblemClassifier.cs"), ("line", 27)));
        var tab = Tab;

        var unlisted = Assert.Throws<BrainException>(() => tab.Lens(worktree, "1", "method DeterministicProblemClassifier.Classify class:short"));
        tab.List();
        var lens = tab.Lens(worktree, "1", "method DeterministicProblemClassifier.Classify class:short");

        Assert.Contains("refresh the Navarone tab", unlisted.Message, StringComparison.Ordinal);
        var where = lens.Sections.Single(section => section.Section.Id == "where");
        Assert.Equal(worktree, where.Section.Blocks[0].Text);
        var words = lens.Sections.Single(section => section.Section.Id == "words").Section.Blocks[0].Facts!;
        Assert.Equal(["method", "DeterministicProblemClassifier.Classify", "class:short"], words.Select(fact => fact.Value));
        Assert.Equal([27], lens.Sections.Single(section => section.Section.Id == "s1").Section.Blocks[0].Highlights);
    }

    [Fact]
    public void TheLensRunsInTheRepoOnceTheWorktreeIsGone()
    {
        Write(Event("raised", Path.Combine(repo, "gone"), ("id", "1"), ("check", "cc"), ("symbol", Classify)));
        var tab = Tab;
        tab.List();

        var lens = tab.Lens(Path.Combine(repo, "gone"), "1", "method DeterministicProblemClassifier.Classify");

        Assert.Equal(repo, lens.Sections.Single(section => section.Section.Id == "where").Section.Blocks[0].Text);
    }

    [Theory]
    [InlineData("navarone::Navarone.Cli.Commands.RunAsync(string[], Coverage)", "Commands.RunAsync", "Commands.RunAsync")]
    [InlineData("brain::Brain.Questions.Drift.Check(CodeSpan, (CodeMark Mark, int From, int To)?, List<string>)", "Drift.Check", "Drift.Check")]
    [InlineData("p::N.Box<T>.Map<U>(Func<T, U>)", "Box.Map", "Box.Map")]
    [InlineData("brain::Brain.Search.Evidence.Tier.get", null, "Tier.get")]
    [InlineData("p::N.Shop", null, "Shop")]
    public void ASymbolIsAMethodAsALensNamesItOrItsOwnName(string symbol, string? method, string name)
    {
        Assert.Equal(method, NavaroneSymbol.Method(symbol));
        Assert.Equal(name, NavaroneSymbol.Name(symbol));
    }

    public void Dispose() => brain.Dispose();

    private string Event(string type, string at, params (string Name, object Value)[] fields)
    {
        var one = new Dictionary<string, object>
        {
            ["type"] = type,
            ["at"] = "2026-11-05T10:00:00Z",
            ["repo"] = repo,
            ["worktree"] = at,
            ["branch"] = "slice",
        };
        foreach (var (name, value) in fields)
        {
            one[name] = value;
        }

        return JsonSerializer.Serialize(one);
    }

    private void Write(params string[] events)
    {
        var fixtures = Path.Combine(AppContext.BaseDirectory, "Fixtures");
        var json = Path.Combine(brain.Root, "events.json");
        File.WriteAllText(json, """{"source":"navarone","events":[""" + string.Join(',', events) + "]}");
        var code = Path.Combine(brain.Root, "code.sh");
        File.WriteAllText(code, "#!/bin/sh\nprintf 'code of %s' \"$1\"\n");
        File.WriteAllText(Path.Combine(brain.Home, "sources.tsv"), $"navarone\ttrue\tCONTEXT_PROBE=1 bash '{fixtures}/context-source.sh'\tcat '{json}'\tsh '{code}'\n");
    }
}
