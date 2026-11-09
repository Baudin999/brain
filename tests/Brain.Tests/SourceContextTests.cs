using Brain.Questions;
using Brain.Serving;

namespace Brain.Tests;

/// <summary>
/// A source's questions may come in groups, name the commit they are about, say that commit is older code and name the
/// query their context opens on. sources.tsv's third column names the context command, which brain runs with the
/// query's words as arguments of their own, never parsed by a shell, and which fails or hangs without taking the window
/// down.
/// </summary>
public sealed class SourceContextTests : IDisposable
{
    private const string Source = "navarone";
    private const string Query = "method DeterministicProblemClassifier.Classify class:short expand:1 [class:short]";

    private static readonly string Questions = Path.Combine(AppContext.BaseDirectory, "Fixtures", "grouped-source.sh");
    private static readonly string Disputes = Path.Combine(AppContext.BaseDirectory, "Fixtures", "dispute-source.sh");
    private static readonly string Context = Path.Combine(AppContext.BaseDirectory, "Fixtures", "context-source.sh");

    private readonly TempBrain brain = new();

    public void Dispose() => brain.Dispose();

    [Fact]
    public void AQuestionCarriesItsGroupCommitStalenessAndContext()
    {
        WriteSources($"{Source}\tbash '{Questions}' /repo\tbash '{Context}'", $"disputes\tbash '{Disputes}' /repo");

        var sources = Inboxes().List().ToDictionary(source => source.Name);

        var questions = sources[Source].Questions.Select(view => view.Question).ToList();
        Assert.Null(sources[Source].Error);
        Assert.Equal(["master", "master", "master", "self-audit", "self-audit"], questions.Select(q => q.Group));
        Assert.Equal("2c0e96878c4c1f0e9b7d3a5c8e2f4a6b1d3c5e7f", questions[0].Commit);
        Assert.Equal([false, true, false, false, false], questions.Select(q => q.Stale));
        Assert.Equal(Query, questions[0].Context);
        Assert.StartsWith("0000001 cyclomatic Navarone.Classifiers", questions[0].Title, StringComparison.Ordinal);
        Assert.Equal("The agent defends its fix. The judge rejected its reason. Who is right: is the fix good?", questions[0].Blocks[0].Text);
        Assert.EndsWith("Does the reason justify keeping it?", questions[1].Blocks[0].Text, StringComparison.Ordinal);
        Assert.Equal(["cyclomatic", "disputed"], questions[0].Tags);
        Assert.Equal(["what you judge", "positions", "agent: why the fix is good", "judge: why it isn't", "the judge's claims", "the code now", "the fix", "the score", "history"], questions[0].Blocks.Select(block => block.Caption));
        Assert.All(sources["disputes"].Questions.Select(view => view.Question), q =>
        {
            Assert.Null(q.Group);
            Assert.Null(q.Commit);
            Assert.False(q.Stale);
            Assert.Null(q.Context);
        });
    }

    [Fact]
    public void SourcesTsvNamesAContextCommandInItsThirdColumn()
    {
        WriteSources(
            "navarone\t'/home/alice/.local/bin/navarone' disputes --json\t'/home/alice/.local/bin/navarone' lens --json",
            "without\tlist --json",
            "blank\tlist --json\t  ",
            "spaced\tlist  --json \t lens  'a b' ");

        var sources = Sources.All(brain.Home).ToDictionary(source => source.Name);

        Assert.Equal(
            ("'/home/alice/.local/bin/navarone' disputes --json", "'/home/alice/.local/bin/navarone' lens --json"),
            (sources["navarone"].Command, sources["navarone"].Context));
        Assert.Null(sources["without"].Context);
        Assert.Null(sources["blank"].Context);
        Assert.Equal(("list  --json", "lens  'a b'"), (sources["spaced"].Command, sources["spaced"].Context));
        Assert.All(sources.Values, source => Assert.Null(source.Problem));
    }

    [Fact]
    public void TheContextIsReadInSectionsWithItsMarksLinks()
    {
        WriteSources($"{Source}\tbash '{Questions}' '{Repo}'\tbash '{Context}'");

        var view = Lens(Query);

        Assert.Equal("2c0e96878c4c1f0e9b7d3a5c8e2f4a6b1d3c5e7f", view.Commit);
        var all = view.Sections.SelectMany(Flat).ToList();
        Assert.Equal(["s1", "s2", "s5"], view.Sections.Select(one => one.Section.Id));
        Assert.Equal([.. Enumerable.Range(1, 13).Select(n => $"s{n}")], all.Select(one => one.Section.Id));
        Assert.All(all, one => Assert.Null(one.Section.Sections));
        Assert.Equal(["s1"], all.Where(one => one.Section.Open).Select(one => one.Section.Id));
        Assert.All(all, one => Assert.Empty(one.Drift));
        var marks = view.Sections[0].Section.Blocks[0].Marks!;
        Assert.Equal(
            ["method DeterministicProblemClassifier.TypeOf class:short", "method DeterministicProblemClassifier.IsValueClass class:short"],
            marks[1].Queries!.Select(query => query.Query));
        Assert.Equal("s3", all.Single(one => one.Section.Id == "s8").Section.Blocks[0].Marks![0].Section);
    }

    /// <summary>Sections nest without limit, and a section's children follow it.</summary>
    [Fact]
    public void SectionsNestWithoutLimitAndTheirChildrenFollowTheirParent()
    {
        WriteSources($"{Source}\tbash '{Questions}' '{Repo}'\tbash '{Context}'");

        var view = Lens(Query);

        var expand = view.Sections[2];
        var typeOf = expand.Children[0];
        var viaTypeOf = typeOf.Children[0];
        Assert.Equal(("s5", "s6", "s7"), (expand.Section.Id, typeOf.Section.Id, viaTypeOf.Section.Id));
        Assert.Equal(["s8", "s9"], viaTypeOf.Children.Select(one => one.Section.Id));
        Assert.Equal(["s6", "s10"], expand.Children.Select(one => one.Section.Id));
        Assert.Empty(viaTypeOf.Children[0].Children);
    }

    /// <summary>A question's focus lines highlight in its own code and in every query's code, and open the sections that hold them.</summary>
    [Fact]
    public void AQuestionsFocusLinesAreHighlightedInEveryQueryAndOpenTheirSections()
    {
        WriteSources($"{Source}\tbash '{Questions}' '{Repo}'\tbash '{Context}'");
        var inboxes = Listed();
        var question = inboxes.List().Single(one => one.Name == Source).Questions[0].Question;

        var first = Lens(Query).Sections.SelectMany(Flat).ToDictionary(one => one.Section.Id);
        var later = Lens("method DeterministicProblemClassifier.IsValueClass").Sections.SelectMany(Flat).ToDictionary(one => one.Section.Id);

        const string File = "src/Navarone.Classifiers/Deterministic/DeterministicProblemClassifier.cs";
        var asked = inboxes.List().Single(one => one.Name == Source).Questions;
        Assert.Equal([File], question.Focus!.Select(focus => focus.Path));
        Assert.Equal([[28, 33, 38]], question.Focus!.Select(focus => focus.Lines));
        Assert.Equal("src/Navarone.Cli/Commands.cs", asked[1].Question.Focus!.Single().Path);
        Assert.All(asked.Skip(2), view => Assert.Null(view.Question.Focus));
        Assert.Null(first["s9"].Section.Sections);
        Assert.Equal([28, 33, 38], first["s1"].Section.Blocks[0].Highlights);
        Assert.Null(first["s10"].Section.Blocks[1].Highlights);
        Assert.Null(first["s3"].Section.Blocks[0].Highlights);
        Assert.Equal(["s1"], first.Values.Where(one => one.Section.Open).Select(one => one.Section.Id));
        Assert.Null(later["s1"].Section.Blocks[0].Highlights);
        Assert.Contains(question.Blocks, block => block.Type == QuestionBlock.CodeType && block.Path == File && block.Highlights is not null && block.Highlights.Contains(33));
    }

    /// <summary>A section that holds a focus line opens with every ancestor, and nothing else opens.</summary>
    [Fact]
    public void ASectionThatHoldsAFocusLineOpensWithItsAncestors()
    {
        static QuestionBlock Code(string path, int start) => new() { Type = QuestionBlock.CodeType, Path = path, StartLine = start, Code = "a\nb\nc\n" };
        static ContextSection Section(string id, IReadOnlyList<QuestionBlock> blocks, params ContextSection[] children) => new(id, id, blocks, false, children.Length == 0 ? null : children);
        var tree = Section("a", [], Section("b", [], Section("c", [Code("x.cs", 10)])), Section("d", [Code("x.cs", 10)]), Section("e", [Code("y.cs", 10)]));

        var view = SectionView.Of(tree, [new FocusLines("x.cs", [11, 13, 40])]);

        var all = new[] { view }.SelectMany(Flat).ToDictionary(one => one.Section.Id);
        Assert.Equal(["a", "b", "c", "d"], all.Values.Where(one => one.Section.Open).Select(one => one.Section.Id));
        Assert.Equal([11], all["c"].Section.Blocks[0].Highlights);
        Assert.Null(all["e"].Section.Blocks[0].Highlights);
    }

    [Fact]
    public void FocusLinesAreCheckedWhenAQuestionIsRead()
    {
        static string Reply(string focus) => $$"""
            {"source":"navarone","questions":[{"id":"q1","repo":"/r","title":"T","created":"2026-11-02T09:00:00+01:00","blocks":[],"choices":[{"id":"a","label":"A"}],"focus":{{focus}}}]}
            """;

        var fine = SourceReply.Parse("navarone", Reply("""[{"path":"a.cs","lines":[28,33,38]},{"path":"b.cs","lines":[7]},{"path":"c.cs","lines":[]}]"""));
        var zero = Assert.Throws<BrainException>(() => SourceReply.Parse("navarone", Reply("""[{"path":"a.cs","lines":[28,0]}]""")));
        var pathless = Assert.Throws<BrainException>(() => SourceReply.Parse("navarone", Reply("""[{"path":" ","lines":[1,2]}]""")));

        Assert.Equal([[28, 33, 38], [7], []], fine.Questions[0].Focus!.Select(focus => focus.Lines));
        Assert.Equal("question \"q1\": focus \"a.cs\" has a line below 1", zero.Message);
        Assert.Equal("question \"q1\": a focus has no path", pathless.Message);
    }

    /// <summary>The context command finds its repo from where it runs: the repo it is given, for every query, never the data repo.</summary>
    [Fact]
    public void TheContextCommandRunsInTheRepoItIsGiven()
    {
        WriteSources($"{Source}\tbash '{Questions}' '{Repo}'\tCONTEXT_PROBE=1 bash '{Context}'");

        var first = Lens(Query);
        var later = Lens("method DeterministicProblemClassifier.IsValueClass");

        Assert.All([first, later], view => Assert.Equal(Repo, view.Sections.Single(one => one.Section.Id == "where").Section.Blocks[0].Text));
    }

    /// <summary>Every word reaches the command as it was typed: no word splits, globs, expands or runs anything.</summary>
    [Fact]
    public void TheContextCommandGetsTheQueryWordsAsArgumentsOfTheirOwn()
    {
        WriteSources($"{Source}\tbash '{Questions}' '{Repo}'\tCONTEXT_PROBE=1 bash '{Context}'");
        string[] words = ["method", "DeterministicProblemClassifier.Classify", "a;b", "$(touch${IFS}pwned)", "`touch${IFS}pwned`", ";touch${IFS}pwned", "'q'", "\"d\"", "*", "x|y", "&&", ">pwned", "$HOME", "\\n", "--json"];

        var view = Lens("  " + string.Join(" \t ", words) + "\n");

        var facts = view.Sections.Single(one => one.Section.Id == "words").Section.Blocks[0].Facts!;
        Assert.Equal(words, facts.Select(fact => fact.Value));
        Assert.Empty(Directory.EnumerateFileSystemEntries(Repo));
        Assert.False(File.Exists(Path.Combine(brain.Home, "pwned")));
    }

    [Fact]
    public void AFailingContextCommandSaysWhatItSaidOnStderr()
    {
        WriteSources(
            $"{Source}\tbash '{Questions}' '{Repo}'\tbash '{Context}'",
            $"silent\tbash '{Questions}' '{Repo}' silent",
            $"garbled\tbash '{Questions}' '{Repo}' garbled\techo 'not json'");

        var failed = Assert.Throws<BrainException>(() => Lens("fail now"));
        var none = Assert.Throws<BrainException>(() => Lens(Query, "silent"));
        var garbled = Assert.Throws<BrainException>(() => Lens(Query, "garbled"));

        Assert.Equal("it exited 2: lens: no symbol answers the query\nlens: try method <Type>.<Method>", failed.Message);
        Assert.Equal("sources.tsv gives silent no context command", none.Message);
        Assert.StartsWith("it printed invalid JSON", garbled.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AHangingContextCommandIsStoppedAtItsTimeout()
    {
        var source = new Source(Source, "true", null, $"bash '{Context}'");
        var clock = System.Diagnostics.Stopwatch.StartNew();

        var late = Assert.Throws<BrainException>(() => SourceContext.Run(source, "hang", brain.Home, TimeSpan.FromSeconds(1)));

        Assert.Equal("it did not finish within 1 seconds", late.Message);
        Assert.True(clock.Elapsed < TimeSpan.FromSeconds(10), $"it took {clock.Elapsed}");
    }

    [Fact]
    public void AContextThatDoesNotHoldTogetherIsRefused()
    {
        static string Context(string sections) => $$"""{"commit":"c","sections":[{{sections}}]}""";
        const string Mark = """{"start":{"line":1,"col":1},"end":{"line":1,"col":2},"label":"x","weight":0""";

        var twice = Assert.Throws<BrainException>(() => SourceContext.Parse(Context("""{"id":"a","title":"A","blocks":[]},{"id":"a","title":"B","blocks":[]}""")));
        var nowhere = Assert.Throws<BrainException>(() => SourceContext.Parse(Context($$"""{"id":"a","title":"A","blocks":[{"type":"code","code":"x","marks":[{{Mark}},"section":"b"}]}]}""")));
        var empty = Assert.Throws<BrainException>(() => SourceContext.Parse(Context($$"""{"id":"a","title":"A","blocks":[{"type":"code","code":"x","marks":[{{Mark}},"queries":[{"label":"l","query":" "}]}]}]}""")));
        var codeless = Assert.Throws<BrainException>(() => SourceContext.Parse(Context("""{"id":"a","title":"A","blocks":[{"type":"code"}]}""")));
        var fine = SourceContext.Parse(Context($$"""{"id":"a","title":"A","blocks":[{"type":"code","code":"x","marks":[{{Mark}},"section":"a"}]}]}"""));

        Assert.Equal("it lists section \"a\" twice", twice.Message);
        Assert.Equal("section \"a\": mark \"x\" jumps to section \"b\", which the context does not hold", nowhere.Message);
        Assert.Equal("section \"a\": mark \"x\" offers an empty query", empty.Message);
        Assert.Equal("section \"a\": block 1, code: it has no code", codeless.Message);
        Assert.False(fine.Sections[0].Open);
    }

    [Fact]
    public void NestedSectionsAreCheckedAcrossTheWholeTree()
    {
        static string Context(string sections) => $$"""{"commit":"c","sections":[{{sections}}]}""";
        const string Mark = """{"start":{"line":1,"col":1},"end":{"line":1,"col":2},"label":"x","weight":0""";

        var twice = Assert.Throws<BrainException>(() => SourceContext.Parse(Context("""{"id":"a","title":"A","blocks":[],"sections":[{"id":"b","title":"B","blocks":[],"sections":[{"id":"a","title":"C","blocks":[]}]}]}""")));
        var cousins = Assert.Throws<BrainException>(() => SourceContext.Parse(Context("""{"id":"a","title":"A","blocks":[],"sections":[{"id":"c","title":"C","blocks":[]}]},{"id":"b","title":"B","blocks":[],"sections":[{"id":"c","title":"C","blocks":[]}]}""")));
        var deep = Assert.Throws<BrainException>(() => SourceContext.Parse(Context("""{"id":"a","title":"A","blocks":[],"sections":[{"id":"b","title":"B","blocks":[],"sections":[{"id":"c","title":"C","blocks":[{"type":"code"}]}]}]}""")));
        var nowhere = Assert.Throws<BrainException>(() => SourceContext.Parse(Context($$"""{"id":"a","title":"A","blocks":[{"type":"code","code":"x","marks":[{{Mark}},"section":"z"}]}],"sections":[{"id":"b","title":"B","blocks":[]}]}""")));
        var fine = SourceContext.Parse(Context($$"""{"id":"a","title":"A","blocks":[{"type":"code","code":"x","marks":[{{Mark}},"section":"d"}]}],"sections":[{"id":"b","title":"B","blocks":[],"sections":[{"id":"d","title":"D","blocks":[]}]}]}"""));

        Assert.Equal("it lists section \"a\" twice", twice.Message);
        Assert.Equal("it lists section \"c\" twice", cousins.Message);
        Assert.Equal("section \"c\": block 1, code: it has no code", deep.Message);
        Assert.Equal("section \"a\": mark \"x\" jumps to section \"z\", which the context does not hold", nowhere.Message);
        Assert.Equal(["a", "b", "d"], fine.Sections.SelectMany(section => section.Tree()).Select(section => section.Id));
    }

    private static IEnumerable<SectionView> Flat(SectionView view) => view.Children.SelectMany(Flat).Prepend(view);

    private string Repo => Directory.CreateDirectory(Path.Combine(brain.Root, "navarone")).FullName;

    private Inboxes Inboxes() => new(brain.Env(TextReader.Null, TextWriter.Null, TextWriter.Null), TimeProvider.System);

    /// <summary>Inboxes that listed every source once, as the window does before it shows a question.</summary>
    private Inboxes Listed()
    {
        var inboxes = Inboxes();
        Assert.All(inboxes.List().Where(source => source.Name != "garbled"), source => Assert.Null(source.Error));
        return inboxes;
    }

    /// <summary>What the lens of a source gives for a query in the repo, with the source's first question's focus lines lit.</summary>
    private ContextView Lens(string query, string name = Source)
    {
        var source = Sources.All(brain.Home).Single(one => one.Name == name);
        var questions = Inboxes().List().Single(one => one.Name == name).Questions;
        IReadOnlyList<FocusLines> focus = questions.Count == 0 ? [] : questions[0].Question.Focus ?? [];
        return ContextView.Of(SourceContext.Run(source, query, Repo, SourceCommand.Timeout), focus);
    }

    private void WriteSources(params string[] lines) =>
        File.WriteAllText(Path.Combine(brain.Home, Sources.FileName), string.Join('\n', lines) + "\n");
}
