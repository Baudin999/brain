using System.Text.Json;
using System.Text.RegularExpressions;
using Brain.Conversations;

namespace Brain.Tests;

/// <summary>
/// <c>brain conversation</c>: a conversation lives in its scope's folder outside every graph, its state in its own
/// state.md, and conversations.json indexes every scope's from scratch. lookup and check never see one.
/// </summary>
public sealed partial class ConversationTests : IDisposable
{
    private readonly TempBrain brain = new();

    public void Dispose() => brain.Dispose();

    private string Index => Path.Combine(brain.Home, "cache", "conversations.json");

    [Fact]
    public void ParkWritesASessionsWordsAsTheEndedTrailOfANewConversationAndCommitsNothing()
    {
        TempBrain.CommitAll(brain.Home);
        var head = TempBrain.Git(brain.Home, "rev-parse", "HEAD");
        Transcript("abc12345-session",
            """{"type":"permission-mode","permissionMode":"auto"}""",
            """{"type":"user","timestamp":"2026-11-03T08:57:00.000Z","message":{"role":"user","content":"Let us talk about bed.<system-reminder>noise</system-reminder>"}}""",
            """{"type":"assistant","timestamp":"2026-11-03T08:57:05.000Z","message":{"content":[{"type":"thinking","thinking":"hm"},{"type":"text","text":"First part."},{"type":"tool_use","name":"Bash","input":{}}]}}""",
            """{"type":"user","timestamp":"2026-11-03T08:57:06.000Z","toolUseResult":{},"message":{"content":[{"type":"tool_result","content":"SECRET"}]}}""",
            """{"type":"assistant","timestamp":"2026-11-03T08:57:30.000Z","message":{"content":[{"type":"tool_use","name":"Bash","input":{}}]}}""",
            """{"type":"user","isMeta":true,"timestamp":"2026-11-03T08:57:07.000Z","message":{"content":"meta"}}""",
            """{"type":"user","timestamp":"2026-11-03T08:57:40.000Z","message":{"content":"<task-notification>\n<task-id>x</task-id>"}}""",
            """{"type":"user","isCompactSummary":true,"timestamp":"2026-11-03T08:57:41.000Z","message":{"content":"SUMMARY"}}""",
            """{"type":"assistant","timestamp":"2026-11-03T08:58:00.000Z","message":{"model":"claude-opus-4-8","content":[{"type":"text","text":"Sleep well."}]}}""",
            """{"type":"user","timestamp":"2026-11-03T08:59:00.000Z","message":{"content":"Good night."}}""");

        var reference = Ref(brain.Ok("conversation", "park", "abc12345-session", "personal", "--model", "opus", "--effort", "high"));

        var folder = Path.Combine(brain.Home, "conversations", "personal", reference["personal:".Length..]);
        Assert.Equal(
            "## phase 1 \u00b7 user \u00b7 2026-11-03T08:57\n\nLet us talk about bed.\n\n"
            + "## phase 2 \u00b7 agent \u00b7 2026-11-03T08:57 \u00b7 opus high\n\nFirst part.\n\nSleep well.\n\n"
            + "## phase 3 \u00b7 user \u00b7 2026-11-03T08:59\n\nGood night.\n\n",
            File.ReadAllText(Path.Combine(folder, "trails", "1.md")));
        var sitting = Read(reference).Current;
        Assert.NotNull(sitting.Ended);
        Assert.Equal(("opus", "high"), (sitting.Model, sitting.Effort));
        Assert.Equal(DateTimeOffset.Parse("2026-11-03T08:57:00Z", System.Globalization.CultureInfo.InvariantCulture), sitting.Started);
        Assert.Contains(reference, File.ReadAllText(Index), StringComparison.Ordinal);
        Assert.Equal(head, TempBrain.Git(brain.Home, "rev-parse", "HEAD"));
        Assert.Contains("?? conversations/", TempBrain.Git(brain.Home, "status", "--short"), StringComparison.Ordinal);
    }

    [Fact]
    public void ParkRefusesATranscriptWithAMalformedLineAndWritesNothing()
    {
        Transcript("abc12345-broken", """{"type":"user","timestamp":"2026-11-03T08:57:00.000Z","message":{"content":"hi"}}""", "{not json");

        var (code, _, errors) = brain.Run("conversation", "park", "abc12345-broken", "personal");

        Assert.NotEqual(0, code);
        Assert.Contains("cannot be read", errors, StringComparison.Ordinal);
        Assert.False(Directory.Exists(Path.Combine(brain.Home, "conversations", "personal")));
    }

    [Fact]
    public void ParkNamesTheModelTheTranscriptRecordsAndNoEffortAndReadsBack()
    {
        Transcript("abc12345-opus",
            """{"type":"user","timestamp":"2026-11-03T08:57:00.000Z","message":{"content":"hi"}}""",
            """{"type":"assistant","timestamp":"2026-11-03T08:57:05.000Z","message":{"model":"<synthetic>","content":[{"type":"text","text":"a"}]}}""",
            """{"type":"assistant","timestamp":"2026-11-03T08:57:06.000Z","message":{"model":"claude-opus-4-8[1m]","content":[{"type":"text","text":"b"}]}}""");
        Transcript("abc12345-odd",
            """{"type":"user","timestamp":"2026-11-03T08:57:00.000Z","message":{"content":"hi"}}""",
            """{"type":"assistant","timestamp":"2026-11-03T08:57:05.000Z","message":{"model":"mystery-9","content":[{"type":"text","text":"a"}]}}""");

        var opus = Ref(brain.Ok("conversation", "park", "abc12345-opus", "personal"));
        var picked = Ref(brain.Ok("conversation", "park", "abc12345-opus", "personal", "--model", "haiku"));
        var odd = Ref(brain.Ok("conversation", "park", "abc12345-odd", "personal"));

        Assert.Equal(("opus", null), (Read(opus).Current.Model, Read(opus).Current.Effort));
        Assert.Equal("haiku", Read(picked).Current.Model);
        Assert.Equal(("sonnet", null), (Read(odd).Current.Model, Read(odd).Current.Effort));
        var folder = Path.Combine(brain.Home, "conversations", "personal", opus["personal:".Length..]);
        Assert.Contains("\u00b7 agent \u00b7 2026-11-03T08:57 \u00b7 opus\n", File.ReadAllText(Path.Combine(folder, "trails", "1.md")), StringComparison.Ordinal);
        Assert.Equal("opus", Trail.Read(Path.Combine(folder, "trails", "1.md"))[1].Model);
        Assert.Null(Trail.Read(Path.Combine(folder, "trails", "1.md"))[1].Effort);
        Assert.Contains("| 1 | opus | - |", File.ReadAllText(Path.Combine(folder, "state.md")), StringComparison.Ordinal);
    }

    private void Transcript(string session, params string[] lines) =>
        File.WriteAllLines(Path.Combine(Directory.CreateDirectory(Path.Combine(brain.Transcripts, "-some-project")).FullName, session + ".jsonl"), lines);

    [Fact]
    public void ParkRefusesAMissingTranscriptAndWritesNothing()
    {
        var (code, _, errors) = brain.Run("conversation", "park", "abc12345-missing", "personal");

        Assert.NotEqual(0, code);
        Assert.Contains("no transcript of session abc12345-missing", errors, StringComparison.Ordinal);
        Assert.False(Directory.Exists(Path.Combine(brain.Home, "conversations", "personal")));
    }

    [Fact]
    public void StartMakesAFolderOutsideTheGraphWithItsStateAndItsFirstSitting()
    {
        var output = brain.Ok("conversation", "start", "personal", "--model", "opus", "--effort", "high");

        var reference = Ref(output);
        var dir = Path.Combine(brain.Home, "conversations", "personal", reference["personal:".Length..]);
        var state = File.ReadAllText(Path.Combine(dir, "state.md"));
        Assert.Matches("^personal:2026-10-03-[0-9a-f]{6}$", reference);
        Assert.StartsWith("---\ntitle: New conversation\nstatus: active\nborn: 2026-10-03\n---\n\n## Sittings\n\n| sitting | model | effort | started | ended | dream |\n|---|---|---|---|---|---|\n| 1 | opus | high | ", state, StringComparison.Ordinal);
        Assert.EndsWith(" | - | - |\n", state, StringComparison.Ordinal);
        Assert.Equal(string.Empty, File.ReadAllText(Path.Combine(dir, "settled.md")));
        Assert.Contains(reference, File.ReadAllText(Index), StringComparison.Ordinal);
    }

    [Fact]
    public void ASittingStartsOnTheSettingsModelAndEffortUnlessPicked()
    {
        var first = Ref(brain.Ok("conversation", "start", "global"));
        File.WriteAllText(Path.Combine(brain.Home, BrainSettings.FileName), """{"chat":{"model":"haiku","effort":"max"},"sitting":{"model":"opus","effort":"high"}}""");
        var set = Ref(brain.Ok("conversation", "start", "global"));
        var picked = Ref(brain.Ok("conversation", "start", "global", "--effort", "low"));

        Assert.Equal(("sonnet", "low"), (Read(first).Current.Model, Read(first).Current.Effort));
        Assert.Equal(("opus", "high"), (Read(set).Current.Model, Read(set).Current.Effort));
        Assert.Equal(("opus", "low"), (Read(picked).Current.Model, Read(picked).Current.Effort));
    }

    [Fact]
    public void AProjectsConversationsLiveInItsDocsBrainAndItsNameStartsTheRef()
    {
        var repo = brain.MakeRepo("cklang");
        brain.Ok("init");
        brain.Cwd = brain.Root;

        var named = Ref(brain.Ok("conversation", "start", "cklang"));
        brain.Cwd = repo;
        var inEffect = Ref(brain.Ok("conversation", "start", "project"));

        Assert.StartsWith("cklang:", named, StringComparison.Ordinal);
        Assert.StartsWith("cklang:", inEffect, StringComparison.Ordinal);
        Assert.True(File.Exists(Path.Combine(repo, "docs", "brain", "conversations", named["cklang:".Length..], "state.md")));
        Assert.True(Directory.Exists(Path.Combine(repo, "docs", "brain", "graph")));
    }

    [Fact]
    public void AConversationIsNeverANodeSoCheckPassesAndLookupNeverFindsIt()
    {
        var repo = brain.MakeRepo("cklang");
        brain.Ok("init");
        var reference = Ref(brain.Ok("conversation", "start", "project"));
        brain.Ok("conversation", "rename", reference, "Generics monomorphised");
        Ref(brain.Ok("conversation", "start", "personal"));
        File.WriteAllText(Path.Combine(repo, "docs", "brain", "conversations", reference["cklang:".Length..], "settled.md"), "# Generics monomorphised\n");

        var check = brain.Ok("check");
        var lookup = brain.Ok("lookup", "generics", "monomorphised");

        Assert.StartsWith("check: 0 nodes, 0 edges, 0 errors", check, StringComparison.Ordinal);
        Assert.DoesNotContain("monomorphised", lookup, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ANewSittingEndsTheCurrentOneAndKeepsTheFolder()
    {
        var reference = Ref(brain.Ok("conversation", "start", "personal"));

        brain.Ok("conversation", "sitting", reference, "--model", "fable", "--effort", "max");
        var state = Read(reference);

        Assert.Equal([1, 2], state.Sittings.Select(sitting => sitting.Number));
        Assert.Equal(state.Sittings[1].Started, state.Sittings[0].Ended);
        Assert.Null(state.Current.Ended);
        Assert.Equal(("fable", "max"), (state.Current.Model, state.Current.Effort));
    }

    [Fact]
    public void ARenameChangesTheTitleAndNeverTheSlug()
    {
        var reference = Ref(brain.Ok("conversation", "start", "personal"));

        var output = brain.Ok("conversation", "rename", reference, "How I want to be told I am wrong");

        Assert.StartsWith($"renamed {reference}: How I want to be told I am wrong", output, StringComparison.Ordinal);
        Assert.Equal("How I want to be told I am wrong", Read(reference).Title);
        Assert.Contains("How I want to be told I am wrong", File.ReadAllText(Index), StringComparison.Ordinal);
    }

    [Fact]
    public void CommitMakesOneCommitOfTheFolderWithoutTempInTheDataRepoWithTheFixedMessage()
    {
        TempBrain.CommitAll(brain.Home);
        var reference = Ref(brain.Ok("conversation", "start", "personal"));
        brain.Ok("conversation", "rename", reference, "Generics");
        var dir = Path.Combine(brain.Home, "conversations", "personal", reference["personal:".Length..]);
        Directory.CreateDirectory(Path.Combine(dir, "TEMP"));
        File.Delete(Path.Combine(dir, ".gitignore"));
        File.WriteAllText(Path.Combine(dir, "TEMP", "scratch.txt"), "x");
        File.WriteAllText(Path.Combine(brain.Home, "stray.txt"), "x");
        var before = TempBrain.Git(brain.Home, "rev-list", "--count", "HEAD").Trim();

        brain.Ok("conversation", "commit", reference);

        var day = Read(reference).Current.Started.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
        var files = TempBrain.Git(brain.Home, "show", "--name-only", "--format=%s", "HEAD").Split('\n', StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(int.Parse(before, System.Globalization.CultureInfo.InvariantCulture) + 1, int.Parse(TempBrain.Git(brain.Home, "rev-list", "--count", "HEAD").Trim(), System.Globalization.CultureInfo.InvariantCulture));
        Assert.Equal($"Generics, {day}", files[0]);
        Assert.All(files.Skip(1), file => Assert.StartsWith($"conversations/personal/{reference["personal:".Length..]}/", file, StringComparison.Ordinal));
        Assert.DoesNotContain(files, file => file.Contains("TEMP", StringComparison.Ordinal));
        Assert.Contains("?? stray.txt", TempBrain.Git(brain.Home, "status", "--short"), StringComparison.Ordinal);
    }

    [Fact]
    public void CommitLeavesATrackedTempFileAloneAndNeverDeletesIt()
    {
        var reference = Ref(brain.Ok("conversation", "start", "personal"));
        var dir = Path.Combine(brain.Home, "conversations", "personal", reference["personal:".Length..]);
        Directory.CreateDirectory(Path.Combine(dir, "TEMP"));
        File.WriteAllText(Path.Combine(dir, "TEMP", "kept.txt"), "x");
        TempBrain.CommitAll(brain.Home);
        File.Delete(Path.Combine(dir, "TEMP", "kept.txt"));
        brain.Ok("conversation", "rename", reference, "Changed");

        brain.Ok("conversation", "commit", reference);

        var files = TempBrain.Git(brain.Home, "show", "--name-status", "--format=", "HEAD");
        Assert.DoesNotContain("TEMP", files, StringComparison.Ordinal);
        Assert.Contains("TEMP/kept.txt", TempBrain.Git(brain.Home, "status", "--short"), StringComparison.Ordinal);
    }

    [Fact]
    public void CommitRecordsADeletedFileOfTheFolder()
    {
        var reference = Ref(brain.Ok("conversation", "start", "personal"));
        var dir = Path.Combine(brain.Home, "conversations", "personal", reference["personal:".Length..]);
        TempBrain.CommitAll(brain.Home);
        File.Delete(Path.Combine(dir, "settled.md"));

        brain.Ok("conversation", "commit", reference);

        Assert.Equal($"D\tconversations/personal/{reference["personal:".Length..]}/settled.md", TempBrain.Git(brain.Home, "show", "--name-status", "--format=", "HEAD").Trim());
    }

    [Fact]
    public void CommitInAProjectLandsInTheProjectsRepo()
    {
        var repo = brain.MakeRepo("cklang");
        TempBrain.CommitAll(repo);
        brain.Ok("init");
        brain.Cwd = brain.Root;
        var reference = Ref(brain.Ok("conversation", "start", "cklang"));

        brain.Ok("conversation", "commit", reference);

        Assert.Contains($"docs/brain/conversations/{reference["cklang:".Length..]}/state.md", TempBrain.Git(repo, "show", "--name-only", "--format=%s", "HEAD"), StringComparison.Ordinal);
    }

    [Fact]
    public void CommitRefusesWhenAnythingElseIsStagedAndWhenThereIsNothingNew()
    {
        TempBrain.CommitAll(brain.Home);
        var reference = Ref(brain.Ok("conversation", "start", "personal"));
        File.WriteAllText(Path.Combine(brain.Home, "other.txt"), "x");
        TempBrain.Git(brain.Home, "add", "other.txt");

        var staged = brain.Run("conversation", "commit", reference);
        TempBrain.Git(brain.Home, "reset", "-q");
        brain.Ok("conversation", "commit", reference);
        var again = brain.Run("conversation", "commit", reference);

        Assert.NotEqual(0, staged.Code);
        Assert.Contains("staged", staged.Err, StringComparison.Ordinal);
        Assert.NotEqual(0, again.Code);
        Assert.Contains("nothing new", again.Err, StringComparison.Ordinal);
    }

    [Fact]
    public void RemoveDeletesTheWholeFolderRebuildsTheIndexAndCommitsNothingUntilCommitIsAsked()
    {
        var reference = Ref(brain.Ok("conversation", "start", "personal"));
        var other = Ref(brain.Ok("conversation", "start", "personal"));
        var dir = Path.Combine(brain.Home, "conversations", "personal", reference["personal:".Length..]);
        Directory.CreateDirectory(Path.Combine(dir, "research"));
        File.WriteAllText(Path.Combine(dir, "research", "note.md"), "x");
        TempBrain.CommitAll(brain.Home);
        Directory.CreateDirectory(Path.Combine(dir, "TEMP"));
        File.WriteAllText(Path.Combine(dir, "TEMP", "scratch.txt"), "x");
        var before = TempBrain.Git(brain.Home, "rev-parse", "HEAD");

        var output = brain.Ok("conversation", "remove", reference);

        Assert.StartsWith($"removed {reference}", output, StringComparison.Ordinal);
        Assert.False(Directory.Exists(dir));
        Assert.DoesNotContain(reference, File.ReadAllText(Index), StringComparison.Ordinal);
        Assert.Contains(other, File.ReadAllText(Index), StringComparison.Ordinal);
        Assert.Equal(before, TempBrain.Git(brain.Home, "rev-parse", "HEAD"));
        Assert.Contains(" D conversations/personal/", TempBrain.Git(brain.Home, "status", "--short"), StringComparison.Ordinal);

        brain.Ok("conversation", "commit", reference);

        Assert.Equal($"removed {reference}", TempBrain.Git(brain.Home, "show", "--no-patch", "--format=%s", "HEAD").Trim());
        Assert.DoesNotContain(reference["personal:".Length..], TempBrain.Git(brain.Home, "ls-files"), StringComparison.Ordinal);
    }

    [Fact]
    public void RemoveRefusesAndDeletesNothingWhileADreamOrASittingAgentHoldsTheConversation()
    {
        var reference = Ref(brain.Ok("conversation", "start", "personal"));
        var slug = reference["personal:".Length..];
        var dir = Path.Combine(brain.Home, "conversations", "personal", slug);
        var locks = Path.Combine(brain.Home, "cache", "locks");
        Directory.CreateDirectory(locks);

        using (new FileStream(Path.Combine(locks, $"personal.{slug}.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None))
        {
            var dream = brain.Run("conversation", "remove", reference);
            Assert.NotEqual(0, dream.Code);
            Assert.Contains("a dream runs", dream.Err, StringComparison.Ordinal);
        }

        using (new FileStream(Path.Combine(locks, $"personal.{slug}.sitting.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None))
        {
            var sitting = brain.Run("conversation", "remove", reference);
            Assert.NotEqual(0, sitting.Code);
            Assert.Contains("live sitting agent", sitting.Err, StringComparison.Ordinal);
        }

        Assert.True(File.Exists(Path.Combine(dir, "state.md")));
    }

    [Theory]
    [InlineData("start", "nowhere", "nowhere is not personal, global, project or a project in projects.tsv")]
    [InlineData("start", "project", "no project scope is in effect here")]
    [InlineData("sitting", "personal:2026-10-03-abcdef", "no conversation personal:2026-10-03-abcdef")]
    [InlineData("sitting", "personal:my-talk", "personal:my-talk is not a conversation's ref")]
    [InlineData("commit", "personal:2026-10-03-abcdef", "no conversation personal:2026-10-03-abcdef")]
    [InlineData("remove", "personal:2026-10-03-abcdef", "no conversation personal:2026-10-03-abcdef")]
    public void AnUnknownPlaceOrConversationIsRefused(string verb, string target, string refusal)
    {
        var (code, _, errors) = brain.Run("conversation", verb, target);

        Assert.Equal(1, code);
        Assert.Contains(refusal, errors, StringComparison.Ordinal);
        Assert.False(Directory.Exists(Path.Combine(brain.Home, "conversations")));
    }

    [Fact]
    public void AModelOrEffortOutsideThePickIsRefused()
    {
        var model = brain.Run("conversation", "start", "personal", "--model", "gpt");
        var effort = brain.Run("conversation", "start", "personal", "--effort", "huge");

        Assert.Contains("model gpt is not one of haiku, sonnet, opus, fable", model.Err, StringComparison.Ordinal);
        Assert.Contains("effort huge is not one of low, medium, high, xhigh, max", effort.Err, StringComparison.Ordinal);
        Assert.False(Directory.Exists(Path.Combine(brain.Home, "conversations")));
    }

    [Fact]
    public void AProjectNotClonedHereHoldsNoConversation()
    {
        File.WriteAllText(Path.Combine(brain.Home, "projects.tsv"), "name\tpath\tremote\tdate\nafterlife\t~/afterlife\t-\t2026-10-01\n");

        var (code, _, errors) = brain.Run("conversation", "start", "afterlife");

        Assert.Equal(1, code);
        Assert.Contains("project afterlife is not cloned here", errors, StringComparison.Ordinal);
    }

    [Fact]
    public void AnEmptyOrMultiLineTitleIsRefused()
    {
        var reference = Ref(brain.Ok("conversation", "start", "personal"));

        var empty = brain.Run("conversation", "rename", reference, " ");
        var lines = brain.Run("conversation", "rename", reference, "one\ntwo");

        Assert.Contains("a conversation's title is empty", empty.Err, StringComparison.Ordinal);
        Assert.Contains("a conversation's title is one line", lines.Err, StringComparison.Ordinal);
        Assert.Equal(ConversationState.FirstTitle, Read(reference).Title);
    }

    [Fact]
    public void ListRebuildsTheIndexFromEveryStateAndNamesWhatItCannotRead()
    {
        var first = Ref(brain.Ok("conversation", "start", "personal"));
        var second = Ref(brain.Ok("conversation", "start", "global"));
        File.Delete(Index);
        var broken = Path.Combine(brain.Home, "conversations", "global", "2026-10-03-000000");
        Directory.CreateDirectory(broken);
        File.WriteAllText(Path.Combine(broken, "state.md"), "no frontmatter\n");

        var (code, output, errors) = brain.Run("conversation", "list");

        Assert.Equal(0, code);
        Assert.Contains(first + "  New conversation  (active, 1 sittings)", output, StringComparison.Ordinal);
        Assert.Contains(second, output, StringComparison.Ordinal);
        Assert.EndsWith("conversations: 2, 1 unreadable\n", output, StringComparison.Ordinal);
        Assert.Contains("is not a conversation's state: it does not start with a --- frontmatter", errors, StringComparison.Ordinal);
        using var index = JsonDocument.Parse(File.ReadAllText(Index));
        Assert.Equal(2, index.RootElement.GetProperty("conversations").GetArrayLength());
        Assert.Equal(1, index.RootElement.GetProperty("problems").GetArrayLength());
    }

    [Fact]
    public void AStateReadsBackAsItWasWritten()
    {
        var state = new ConversationState(
            "Generics: monomorphise or erase",
            ConversationState.Active,
            new DateOnly(2026, 11, 1),
            [
                new Sitting(1, "opus", "high", new DateTimeOffset(2026, 11, 1, 14, 2, 11, TimeSpan.Zero), new DateTimeOffset(2026, 11, 1, 15, 0, 0, TimeSpan.Zero), Dream: "2026-11-01-0a1b2c"),
                new Sitting(2, "sonnet", "low", new DateTimeOffset(2026, 11, 1, 15, 0, 0, TimeSpan.Zero), Ended: null),
            ]);

        var read = ConversationState.Parse(state.Render(), "state.md");

        Assert.Equal(state.Title, read.Title);
        Assert.Equal(state.Born, read.Born);
        Assert.Equal(state.Sittings, read.Sittings);
    }

    /// <summary>A trail is the doc's markdown, one header a phase, and reads back phase by phase as it was written.</summary>
    [Fact]
    public void ATrailIsMarkdownPhaseByPhaseAndReadsBackAsItWasWritten()
    {
        var path = Trail.PathOf(Path.Combine(brain.Home, "conversations", "personal", "2026-10-03-abcdef"), 3);
        var at = new DateTimeOffset(2026, 11, 1, 14, 2, 41, TimeSpan.Zero);
        Phase[] phases =
        [
            new(1, Trail.User, at, Model: null, Effort: null, "Monomorphise at the call site?"),
            new(2, Trail.Agent, at.AddMinutes(1), "sonnet", "low", "No.\n\n## phase 2 · user · 2026-11-01T14:03\n\nquoted, not a phase."),
            new(3, Trail.User, at.AddMinutes(2), Model: null, Effort: null, "Why not?"),
        ];

        foreach (var phase in phases)
        {
            Trail.Append(path, phase);
        }

        var text = File.ReadAllText(path);
        Assert.EndsWith(Path.Combine("2026-10-03-abcdef", "trails", "3.md"), path, StringComparison.Ordinal);
        Assert.StartsWith("## phase 1 · user · 2026-11-01T14:02\n\nMonomorphise at the call site?\n\n## phase 2 · agent · 2026-11-01T14:03 · sonnet low\n\nNo.\n", text, StringComparison.Ordinal);
        Assert.Equal(phases.Select(phase => phase with { At = new DateTimeOffset(phase.At.Year, phase.At.Month, phase.At.Day, phase.At.Hour, phase.At.Minute, 0, TimeSpan.Zero) }), Trail.Read(path));
        Assert.Empty(Trail.Read(Path.Combine(brain.Home, "no-trail.md")));
    }

    /// <summary>A user's comments open its phase in the doc's format, in the order added, and read back with their anchors.</summary>
    [Fact]
    public void ACommentIsAQuoteLineAndItsTextBeforeTheTextTypedAtTheBottom()
    {
        var path = Trail.PathOf(Path.Combine(brain.Home, "conversations", "personal", "2026-10-03-abcdef"), 1);
        var at = new DateTimeOffset(2026, 11, 1, 14, 2, 0, TimeSpan.Zero);
        var phase = new Phase(7, Trail.User, at, null, null, "And the text typed at the bottom.", [new(1, 6, "monomorphise at the call site", "why not at link time?"), new(2, Trail.Settled, "traits are \"structural\"", "this contradicts #4\n\n> and a second paragraph")]);
        Trail.Append(path, new Phase(1, Trail.User, at, null, null, "> a plain quote\nstays text."));
        phase = phase with { Number = 2 };
        Trail.Append(path, phase);
        Trail.Append(path, new Phase(3, Trail.User, at, null, null, string.Empty, [new(1, 1, "q", "only a comment")]));
        const string Looks = "> 1 on phase 9: \"x\"\nlooks like a comment\n\\> and a backslash";
        Trail.Append(path, new Phase(4, Trail.User, at, null, null, Looks));
        Trail.Append(path, new Phase(5, Trail.Agent, at, "sonnet", "low", "> a blockquote"));

        Assert.Contains("## phase 2 · user · 2026-11-01T14:02\n\n> 1 on phase 6: \"monomorphise at the call site\"\nwhy not at link time?\n\n> 2 on settled: \"traits are \"structural\"\"\nthis contradicts #4\n>\n\\> and a second paragraph\n\nAnd the text typed at the bottom.\n", File.ReadAllText(path), StringComparison.Ordinal);
        var read = Trail.Read(path);
        Assert.Equal(phase.Text, read[1].Text);
        Assert.Equal(phase.Comments, read[1].Comments);
        Assert.Null(read[0].Comments);
        Assert.Equal("> a plain quote\nstays text.", read[0].Text);
        Assert.Equal("", read[2].Text);
        Assert.Equal("only a comment", Assert.Single(read[2].Comments!).Text);
        Assert.Null(read[3].Comments);
        Assert.Equal(Looks, read[3].Text);
        Assert.Equal("> a blockquote", read[4].Text);
        File.AppendAllText(path, "## phase 6 · user · 2026-11-01T14:02\n\n> plain quote\nwritten by hand\n\n");
        Assert.Equal("> plain quote\nwritten by hand", Trail.Read(path)[5].Text);
    }

    /// <summary>A sitting's agent starts from where it sits, settled.md and every trail so far, then the user's words.</summary>
    [Fact]
    public void TheSeedNamesTheConversationAndCarriesSettledAndEveryTrail()
    {
        var env = brain.Env(TextReader.Null, TextWriter.Null, TextWriter.Null);
        var reference = Ref(brain.Ok("conversation", "start", "personal"));
        var conversation = ConversationStore.Find(env, reference);
        File.WriteAllText(Path.Combine(conversation.Dir, "settled.md"), "Erase generics.\n");
        Trail.Append(Trail.PathOf(conversation.Dir, 1), new Phase(1, Trail.User, DateTimeOffset.UnixEpoch, null, null, "first words"));
        conversation = ConversationStore.Sit(env, reference, "opus", "high", DateTimeOffset.UnixEpoch);

        Seed.Prepare(conversation);
        var seed = Seed.For(conversation, "And now?");

        Assert.Contains($"Conversation: {reference}, \"New conversation\", sitting 2 on opus at high effort.", seed, StringComparison.Ordinal);
        Assert.Contains(Path.Combine(conversation.Dir, "research") + "/", seed, StringComparison.Ordinal);
        Assert.Contains(Path.Combine(conversation.Dir, "TEMP") + "/", seed, StringComparison.Ordinal);
        Assert.Contains("<settled>\nErase generics.\n</settled>", seed, StringComparison.Ordinal);
        Assert.Contains("<trail sitting=\"1\">\n## phase 1 · user · 1970-01-01T00:00\n\nfirst words\n</trail>", seed, StringComparison.Ordinal);
        Assert.EndsWith("The user:\n\nAnd now?", seed, StringComparison.Ordinal);
        Assert.Equal("TEMP/\n", File.ReadAllText(Path.Combine(conversation.Dir, ".gitignore")));
        Assert.True(Directory.Exists(Path.Combine(conversation.Dir, "research")));
    }

    private ConversationState Read(string reference) =>
        ConversationStore.Find(brain.Env(TextReader.Null, TextWriter.Null, TextWriter.Null), reference).State;

    private static string Ref(string output) => Started().Match(output).Groups[1].Value;

    [GeneratedRegex(@"^\w+ ([a-z0-9-]+:[0-9]{4}-[0-9]{2}-[0-9]{2}-[0-9a-f]{6}):")]
    private static partial Regex Started();
}
