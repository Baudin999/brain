using System.Text.Json;
using Brain.Questions;
using Brain.Serving;

namespace Brain.Tests;

/// <summary>
/// The inboxes beside node review: brain runs every source in sources.tsv, shows its questions with the project of
/// their repo, shows a failing source with the reason and lists the others anyway, and writes the user's answer once,
/// after which the question stays gone while the source still lists it.
/// </summary>
public sealed class QuestionSourceTests : IDisposable
{
    private const string Source = "disputes";
    private const string Split = "0000019";
    private const string Flat = "0000023";
    private const string Score = "0000031";
    private const string Off = "0000032";

    private static readonly string Fixture = Path.Combine(AppContext.BaseDirectory, "Fixtures", "dispute-source.sh");

    private readonly TempBrain brain = new();
    private readonly string repo;

    public QuestionSourceTests()
    {
        repo = Path.Combine(brain.Root, "studio");
        File.WriteAllText(Path.Combine(brain.Home, ProjectRegistry.FileName), $"{ProjectRegistry.Header}\nstudio\t~/studio\t\t2026-10-03\n");
    }

    public void Dispose() => brain.Dispose();

    private string Answers => Path.Combine(brain.Home, "cache", "answers", Source);

    [Fact]
    public void ASourceShowsItsQuestionsInTheProjectOfTheirRepo()
    {
        WriteSources($"{Source}\tbash '{Fixture}' '{repo}/'");

        var source = Assert.Single(Inboxes().List());

        Assert.Null(source.Error);
        Assert.Equal([Split, Flat, Score, Off], source.Questions.Select(view => view.Question.Id));
        var split = source.Questions[0];
        Assert.Equal([new GraphView("studio", ReviewViews.Hue("studio"))], split.Graphs);
        Assert.Equal(["split-complexity", "cyclomatic"], split.Question.Tags);
        Assert.Equal(["code", "diff", "text", "text", "claims", "facts"], split.Question.Blocks.Select(block => block.Type));
        var diff = split.Question.Blocks[1];
        Assert.Equal(string.Empty, diff.Files![1].Before);
        Assert.Contains("IsValueClass", diff.Files[1].After, StringComparison.Ordinal);
        Assert.Equal([true, true, false], split.Question.Blocks[4].Claims!.Select(claim => claim.Holds));
        var flat = source.Questions[1];
        Assert.Empty(flat.Graphs);
        Assert.Equal([new BlockAgreement([2], true, [new ValueCount("keep", 2)])], flat.Agreements, SameAgreement);
        Assert.Equal([new BlockAgreement([5], false, [new ValueCount("keep", 1), new ValueCount("replace", 1)])], split.Agreements, SameAgreement);
        var unknown = flat.Question.Blocks[1];
        Assert.Equal("image", unknown.Type);
        Assert.Equal("screenshot.png", unknown.Rest!["src"].GetString());
    }

    [Fact]
    public void AScoreShowsWhereItComesFromAndHowItGrew()
    {
        WriteSources($"{Source}\tbash '{Fixture}' '{repo}'");

        var score = Assert.Single(Inboxes().List()).Questions[2];

        Assert.Empty(score.Drift);
        Assert.Equal([new BlockSum([1], 8, 9, true), new BlockSum([2, 0, 0], 4, 5, true)], score.Sums, SameSum);
        Assert.Equal(["facts", "code", "timeline"], score.Question.Blocks.Select(block => block.Type));
        Assert.Equal(["bad", "neutral", "good", null], score.Question.Blocks[0].Facts!.Select(fact => fact.Tone));
        var code = score.Question.Blocks[1];
        Assert.Equal(new Tally("cyclomatic", 1, 9), code.Tally);
        Assert.Equal(["if", "if", "foreach", "if", "arm", "arm", "default arm"], code.Marks!.Select(mark => mark.Label));
        var compound = code.Marks![1];
        Assert.Equal(["||", "&&"], compound.Marks!.Select(mark => mark.Label));
        Assert.Equal(new CodePosition(20, 32), compound.Marks![0].Start);
        Assert.Equal(0, code.Marks![6].Weight);
        var steps = score.Question.Blocks[2].Steps!;
        Assert.Equal(["a1b2c3d, the method before", "e4f5a6b, coupons and bulk lines"], steps.Select(step => step.Label));
        Assert.Equal(5, steps[0].Blocks[0].Tally!.Value);
        Assert.Equal(["diff", "text", "facts", "facts"], steps[1].Blocks.Select(block => block.Type));
        Assert.Equal(
            [new BlockAgreement([2, 1, 2], true, [new ValueCount("keep", 2)]), new BlockAgreement([2, 1, 3], false, [new ValueCount("keep", 1), new ValueCount("replace", 1)])],
            score.Agreements,
            SameAgreement);
    }

    [Fact]
    public void ATallyThatDoesNotAddUpSaysSo()
    {
        WriteSources($"{Source}\tbash '{Fixture}' '{repo}'");

        var off = Assert.Single(Inboxes().List()).Questions[3];

        Assert.Equal(
            [
                new BlockDrift([0], "mark \"if\" at 27:13 to 27:37 says it reads \"if (line.Quantity > 10)\", and the code there reads \"if (line.Quantity >= 10)\""),
                new BlockDrift([0], "cyclomatic: base 1 + weights 7 = 8, and the source says 9"),
                new BlockDrift([1, 0, 0], "cyclomatic: base 1 + weights 4 = 5, and the source says 6"),
            ],
            off.Drift,
            (one, other) => one.At.SequenceEqual(other.At) && one.Says == other.Says);
        Assert.Equal([new BlockSum([0], 7, 8, false), new BlockSum([1, 0, 0], 4, 5, false)], off.Sums, SameSum);
    }

    private static bool SameAgreement(BlockAgreement one, BlockAgreement other) =>
        one.At.SequenceEqual(other.At) && one.Agrees == other.Agrees && one.Split.SequenceEqual(other.Split);

    private static QuestionBlock Facts(bool? agreement, params string[] values) => new()
    {
        Type = QuestionBlock.FactsType,
        Agreement = agreement,
        Facts = [.. values.Select((value, at) => new Fact($"f{at}", value))],
    };

    [Fact]
    public void FactsThatAskForAgreementAreComparedAsExactStrings()
    {
        var equal = Drift.Agreement([0], Facts(true, "keep", "keep", "keep"));
        var split = Drift.Agreement([1], Facts(true, "keep", "replace", "keep"));
        var cased = Drift.Agreement([2], Facts(true, "keep", "Keep"));

        Assert.True(equal!.Agrees);
        Assert.Equal([new ValueCount("keep", 3)], equal.Split);
        Assert.False(split!.Agrees);
        Assert.Equal([new ValueCount("keep", 2), new ValueCount("replace", 1)], split.Split);
        Assert.False(cased!.Agrees);
        Assert.Null(Drift.Agreement([3], Facts(true, "keep")));
        Assert.Null(Drift.Agreement([4], Facts(null, "keep", "keep")));
        Assert.Null(Drift.Agreement([5], Facts(false, "keep", "replace")));
    }

    private static bool SameSum(BlockSum one, BlockSum other) =>
        one.At.SequenceEqual(other.At) && one.Weights == other.Weights && one.Total == other.Total && one.AddsUp == other.AddsUp;

    /// <summary>brain checks where every mark sits, a nested one inside its parent, and finds drift inside a timeline's steps too.</summary>
    [Fact]
    public void AMarkThatDoesNotFitItsCodeSaysSo()
    {
        static CodeMark Mark(string label, int line, int from, int to, params CodeMark[] inner) =>
            new(new CodePosition(line, from), new CodePosition(line, to), label, 1, Marks: inner);
        var code = new QuestionBlock
        {
            Type = QuestionBlock.CodeType,
            StartLine = 10,
            Code = "if (a || b)\n{\n    x();\n}\n",
            Marks =
            [
                Mark("if", 10, 1, 12, Mark("||", 10, 7, 9), new CodeMark(new CodePosition(10, 5), new CodePosition(11, 2), "wide", 1)),
                Mark("x", 12, 5, 9),
                Mark("again", 12, 7, 8),
                Mark("past", 14, 1, 2),
                Mark("empty", 11, 2, 2),
            ],
        };
        var question = new Question("q", "/x", "t", DateTimeOffset.UnixEpoch, [new QuestionBlock
        {
            Type = QuestionBlock.TimelineType,
            Steps = [new TimelineStep("one", [new QuestionBlock { Type = QuestionBlock.TextType, Text = "t" }, code])],
        }], [new QuestionChoice("a", "A")]);

        var drift = Drift.Of(question.Blocks);

        Assert.All(drift, one => Assert.Equal([0, 0, 1], one.At));
        Assert.Equal(
            [
                "mark \"wide\" at 10:5 to 11:2 reaches outside the mark \"if\" it sits in",
                "mark \"||\" at 10:7 to 10:9 overlaps the mark \"wide\" before it",
                "mark \"empty\" at 11:2 to 11:2 is empty",
                "mark \"again\" at 12:7 to 12:8 overlaps the mark \"x\" before it",
                "mark \"past\" at 14:1 to 14:2 lies outside the code shown, lines 10 to 13",
            ],
            drift.Select(one => one.Says));
    }

    /// <summary>A field brain does not know rides along to the window on a mark, a tally, a fact and a step alike.</summary>
    [Fact]
    public void UnknownFieldsOfTheNewBlocksAreKept()
    {
        var reply = SourceReply.Parse("s", """
            {"source":"s","questions":[{"id":"q","repo":"/x","title":"t","created":"2026-11-01T08:00:00Z","choices":[{"id":"a","label":"A"}],"blocks":[
              {"type":"code","code":"x","marks":[{"start":{"line":1,"col":1},"end":{"line":1,"col":2},"label":"x","weight":1,"colour":"red"}],
               "tally":{"label":"l","base":0,"value":1,"unit":"paths"}},
              {"type":"facts","facts":[{"label":"l","value":"v","source":"ci"}]},
              {"type":"timeline","steps":[{"label":"l","when":null,"blocks":[],"commit":"abc"}]}]}]}
            """);

        var blocks = reply.Questions[0].Blocks;
        Assert.Equal("red", blocks[0].Marks![0].Rest!["colour"].GetString());
        Assert.Equal("paths", blocks[0].Tally!.Rest!["unit"].GetString());
        Assert.Equal("ci", blocks[1].Facts![0].Rest!["source"].GetString());
        Assert.Equal("abc", blocks[2].Steps![0].Rest!["commit"].GetString());
    }

    [Fact]
    public void AFailingSourceSaysWhyAndTheOthersStillShow()
    {
        WriteSources(
            $"{Source}\tbash '{Fixture}' '{repo}'",
            "# a comment line",
            "",
            "crashes\techo 'no state file' >&2; exit 3",
            "garbled\techo 'not json'",
            "impostor\tbash '" + Fixture + "' /x",
            """unfinished	echo '{"source":"unfinished","questions":[{"id":"q1","repo":"/x","title":"t","created":"2026-11-01T08:00:00Z","blocks":[]}]}'""",
            """codeless	echo '{"source":"codeless","questions":[{"id":"q1","repo":"/x","title":"t","created":"2026-11-01T08:00:00Z","blocks":[{"type":"code"}],"choices":[{"id":"a","label":"A"}]}]}'""",
            """escaping	echo '{"source":"escaping","questions":[{"id":"../x","repo":"/x","title":"t","created":"2026-11-01T08:00:00Z","blocks":[],"choices":[{"id":"a","label":"A"}]}]}'""",
            """nested	echo '{"source":"nested","questions":[{"id":"q1","repo":"/x","title":"t","created":"2026-11-01T08:00:00Z","choices":[{"id":"a","label":"A"}],"blocks":[{"type":"timeline","steps":[{"label":"s","blocks":[{"type":"timeline","steps":[]}]}]}]}]}'""",
            """backwards	echo '{"source":"backwards","questions":[{"id":"q1","repo":"/x","title":"t","created":"2026-11-01T08:00:00Z","choices":[{"id":"a","label":"A"}],"blocks":[{"type":"timeline","steps":[{"label":"a","when":"2026-10-02T00:00:00Z","blocks":[]},{"label":"u","when":null,"blocks":[]},{"label":"b","when":"2026-10-01T00:00:00Z","blocks":[]}]}]}]}'""",
            """toned	echo '{"source":"toned","questions":[{"id":"q1","repo":"/x","title":"t","created":"2026-11-01T08:00:00Z","choices":[{"id":"a","label":"A"}],"blocks":[{"type":"facts","facts":[{"label":"l","value":"v","tone":"great"}]}]}]}'""",
            """factless	echo '{"source":"factless","questions":[{"id":"q1","repo":"/x","title":"t","created":"2026-11-01T08:00:00Z","choices":[{"id":"a","label":"A"}],"blocks":[{"type":"facts"}]}]}'""",
            "../bad\techo",
            "silent");

        var sources = Inboxes().List().ToDictionary(source => source.Name);

        Assert.Null(sources[Source].Error);
        Assert.Equal(4, sources[Source].Questions.Count);
        Assert.Equal("it exited 3: no state file", sources["crashes"].Error);
        Assert.StartsWith("it printed invalid JSON", sources["garbled"].Error, StringComparison.Ordinal);
        Assert.Equal("it says it is \"disputes\", and sources.tsv names it impostor", sources["impostor"].Error);
        Assert.Contains("choices", sources["unfinished"].Error, StringComparison.Ordinal);
        Assert.Equal("question \"q1\": block 1, code: it has no code", sources["codeless"].Error);
        Assert.StartsWith("question \"../x\": an id is", sources["escaping"].Error, StringComparison.Ordinal);
        Assert.Equal("question \"q1\": block 1, timeline: step 1, \"s\", block 1, timeline: a timeline inside a timeline", sources["nested"].Error);
        Assert.Equal("question \"q1\": block 1, timeline: step 3, \"b\": steps run oldest first", sources["backwards"].Error);
        Assert.Equal("question \"q1\": block 1, facts: fact \"l\": a tone is good, bad or neutral, not \"great\"", sources["toned"].Error);
        Assert.Equal("question \"q1\": block 1, facts: it has no facts", sources["factless"].Error);
        Assert.StartsWith("sources.tsv names a source \"../bad\"", sources["../bad"].Error, StringComparison.Ordinal);
        Assert.Equal("sources.tsv gives silent no command after a tab", sources["silent"].Error);
        Assert.All(sources.Values.Where(source => source.Error is not null), source => Assert.Empty(source.Questions));
    }

    [Fact]
    public void WithoutSourcesTheWindowHasOnlyNodeReview() => Assert.Empty(Inboxes().List());

    [Fact]
    public void AnAnswerIsWrittenOnceAndItsQuestionStaysGone()
    {
        WriteSources($"{Source}\tbash '{Fixture}' '{repo}'");
        var inboxes = Inboxes();

        Assert.Throws<BrainException>(() => inboxes.Answer(Source, Split, "agent", null));
        inboxes.List();
        Assert.Throws<BrainException>(() => inboxes.Answer(Source, Split, "neither", null));
        Assert.Throws<BrainException>(() => inboxes.Answer(Source, "0000999", "agent", null));
        var answer = inboxes.Answer(Source, Split, "gemma4", "  The loop moved, the paths stayed.  ");
        var again = Assert.Throws<BrainException>(() => inboxes.Answer(Source, Split, "agent", null));
        var listed = Assert.Single(inboxes.List()).Questions;
        var fresh = Assert.Single(Inboxes().List()).Questions;

        Assert.Equal("The loop moved, the paths stayed.", answer.Words);
        using var file = JsonDocument.Parse(File.ReadAllText(Path.Combine(Answers, Split + ".json")));
        Assert.Equal(Split, file.RootElement.GetProperty("id").GetString());
        Assert.Equal("gemma4", file.RootElement.GetProperty("choice").GetString());
        Assert.Equal("The loop moved, the paths stayed.", file.RootElement.GetProperty("words").GetString());
        Assert.Equal(new DateTimeOffset(2026, 11, 1, 9, 0, 0, TimeSpan.Zero), file.RootElement.GetProperty("answeredAt").GetDateTimeOffset());
        Assert.Contains("has no open question", again.Message, StringComparison.Ordinal);
        Assert.Equal([Flat, Score, Off], listed.Select(view => view.Question.Id));
        Assert.Equal([Flat, Score, Off], fresh.Select(view => view.Question.Id));
        Assert.Equal([Split + ".json"], Directory.GetFiles(Answers).Select(Path.GetFileName));
    }

    [Fact]
    public void AnAnswerWithoutWordsHasNone()
    {
        WriteSources($"{Source}\tbash '{Fixture}' '{repo}'");
        var inboxes = Inboxes();
        inboxes.List();

        inboxes.Answer(Source, Flat, "agent", "   ");

        using var file = JsonDocument.Parse(File.ReadAllText(Path.Combine(Answers, Flat + ".json")));
        Assert.Equal(JsonValueKind.Null, file.RootElement.GetProperty("words").ValueKind);
    }

    private Inboxes Inboxes() =>
        new(brain.Env(TextReader.Null, TextWriter.Null, TextWriter.Null), new FixedTime(new DateTimeOffset(2026, 11, 1, 9, 0, 0, TimeSpan.Zero)));

    private void WriteSources(params string[] lines) =>
        File.WriteAllText(Path.Combine(brain.Home, Sources.FileName), string.Join('\n', lines) + "\n");

    private sealed class FixedTime(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
