using System.Collections.Concurrent;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Brain.Conversations;
using Brain.Dreaming;

namespace Brain.Tests;

/// <summary>
/// <c>brain dream</c>: two cheap dreamers distill a conversation's unsettled sittings into settled.md, and an agreement
/// lands while a divergence or a doubt goes to Opus. state.md marks what a dream settled, the next seed leaves it out,
/// the log in the data repo keeps every dream and is committed, and a dream never runs while a sitting is open.
/// </summary>
[Collection(ReviewServerTests.StartsClaude)]
public sealed partial class DreamTests : IDisposable
{
    private const string Haiku = "SURE\n# Generics\n\n- Erase them, since the runtime has no types.\n";
    private const string Sonnet = "SURE\n# Generics\n\n- Erase generics: the runtime has no types.\n";
    private const string Opus = "SURE\n# Generics\n\n- Monomorphise at the call site.\n";

    private readonly TempBrain brain = new();

    public DreamTests()
    {
        TempBrain.CommitAll(brain.Home);
        brain.Models.Drafts["haiku"] = Haiku;
        brain.Models.Drafts["sonnet"] = Sonnet;
        brain.Models.Comparison = "AGREE";
        brain.Models.Escalation = Opus;
    }

    public void Dispose() => brain.Dispose();

    private BrainEnv Env => brain.Env(TextReader.Null, TextWriter.Null, TextWriter.Null);

    [Fact]
    public void AgreeingDraftsLandInSettledAndTheEndedSittingsAreSettled()
    {
        var reference = Talked(sittings: 2);

        var output = brain.Ok("dream", reference);

        var conversation = ConversationStore.Find(Env, reference);
        var id = DreamId().Match(output).Groups[1].Value;
        Assert.Equal("# Generics\n\n- Erase generics: the runtime has no types.\n", File.ReadAllText(Path.Combine(conversation.Dir, "settled.md")));
        Assert.Equal(new string?[] { id, id, null }, conversation.State.Sittings.Select(sitting => sitting.Dream));
        Assert.Contains("| 1 | sonnet | low | ", File.ReadAllText(Path.Combine(conversation.Dir, "state.md")), StringComparison.Ordinal);
        Assert.Contains($" | {id} |\n", File.ReadAllText(Path.Combine(conversation.Dir, "state.md")), StringComparison.Ordinal);
        Assert.Equal(["haiku", "sonnet", "sonnet"], brain.Models.Calls.Select(call => call.Model).Order(StringComparer.Ordinal));
        Assert.DoesNotContain(brain.Models.Calls, call => call.Model == "opus");
        var record = DreamLog.Find(Env, id);
        Assert.Equal((DreamRecord.Agreed, (string?)null, (DreamDraft?)null), (record.Outcome, record.Why, record.Escalation));
        Assert.Equal([1, 2], record.Sittings);
        Assert.Equal(new DreamShape(Sittings: 2, Phases: 4, Characters: record.Shape.Characters, SettledCharacters: 0), record.Shape);
        Assert.True(record.Shape.Characters > 0);
        Assert.Equal((string.Empty, "# Generics\n\n- Erase generics: the runtime has no types."), (record.Before, record.After));
        Assert.Contains("sonnet's draft is settled.md now", output, StringComparison.Ordinal);
    }

    [Fact]
    public void TheDreamerAsksTheTwoModelsOfItsSettingEachAtItsEffort()
    {
        var reference = Talked(sittings: 2);
        BrainSettings.Change(brain.Home, "dreamer", JsonNode.Parse("""[{"model":"opus","effort":"high"},{"model":"haiku","effort":"low"}]"""));
        brain.Models.Drafts["opus"] = Sonnet;

        var output = brain.Ok("dream", reference);

        Assert.Equal(
            [("haiku", "low"), ("opus", "high")],
            brain.Models.Calls.Where(call => call.System == DreamPrompt.Dreamer).Select(call => (call.Model, call.Effort)).Order());
        Assert.Equal(("sonnet", "medium"), brain.Models.Calls.Where(call => call.System == DreamPrompt.Comparison).Select(call => (call.Model, call.Effort)).Single());
        Assert.Contains("haiku's draft is settled.md now", output, StringComparison.Ordinal);
    }

    [Fact]
    public void EachDreamerReadsSettledAndTheTrailOfEveryEndedSitting()
    {
        var reference = Talked(sittings: 2);
        var dir = ConversationStore.Find(Env, reference).Dir;
        File.WriteAllText(Path.Combine(dir, "settled.md"), "Keep the old rule.\n");

        brain.Ok("dream", reference);

        var dreamer = brain.Models.Calls.First(call => call.System == DreamPrompt.Dreamer);
        Assert.StartsWith("<settled>\nKeep the old rule.\n</settled>\n", dreamer.Prompt, StringComparison.Ordinal);
        Assert.Contains("<trail sitting=\"1\">\n## phase 1 · user", dreamer.Prompt, StringComparison.Ordinal);
        Assert.Contains("<trail sitting=\"2\">", dreamer.Prompt, StringComparison.Ordinal);
        Assert.Contains("said in sitting 2", dreamer.Prompt, StringComparison.Ordinal);
        Assert.DoesNotContain("<trail sitting=\"3\">", dreamer.Prompt, StringComparison.Ordinal);
    }

    [Fact]
    public void AnUnsureDreamerEscalatesToOpusWithBothDraftsAndNoComparison()
    {
        var reference = Talked(sittings: 1);
        brain.Models.Drafts["haiku"] = "UNSURE: the user never said yes\n# Generics\n";

        brain.Ok("dream", reference);

        var record = DreamLog.All(Env).Single();
        var escalation = brain.Models.Calls.Single(call => call.Model == "opus");
        Assert.Equal("medium", escalation.Effort);
        Assert.Equal(DreamRecord.Escalated, record.Outcome);
        Assert.Equal("haiku was unsure: the user never said yes", record.Why);
        Assert.Null(record.Comparison);
        Assert.DoesNotContain(brain.Models.Calls, call => call.System == DreamPrompt.Comparison);
        Assert.Equal(DreamPrompt.Escalation, escalation.System);
        Assert.Contains("<draft>\n# Generics\n</draft>", escalation.Prompt, StringComparison.Ordinal);
        Assert.Contains("Erase generics: the runtime has no types.", escalation.Prompt, StringComparison.Ordinal);
        Assert.Contains("<trail sitting=\"1\">", escalation.Prompt, StringComparison.Ordinal);
        Assert.Equal("# Generics\n\n- Monomorphise at the call site.\n", File.ReadAllText(Path.Combine(ConversationStore.Find(Env, reference).Dir, "settled.md")));
    }

    [Fact]
    public void DivergingDraftsEscalateWithWhatDiffers()
    {
        var reference = Talked(sittings: 1);
        brain.Models.Comparison = "DIVERGE: only one keeps the reason";

        var output = brain.Ok("dream", reference);

        var record = DreamLog.All(Env).Single();
        Assert.Equal(DreamRecord.Escalated, record.Outcome);
        Assert.Equal("the drafts diverged: only one keeps the reason", record.Why);
        Assert.False(record.Comparison!.Agreed);
        Assert.Equal("opus", record.Escalation!.Model);
        Assert.Contains("escalated to opus", output, StringComparison.Ordinal);
    }

    [Fact]
    public void TheDreamIsLoggedInTheDataRepoAndCommittedAlone()
    {
        var reference = Talked(sittings: 1);
        var before = TempBrain.Git(brain.Home, "rev-list", "--count", "HEAD").Trim();

        brain.Ok("dream", reference);

        var id = DreamLog.All(Env).Single().Id;
        var files = TempBrain.Git(brain.Home, "show", "--name-only", "--format=%s", "HEAD").Split('\n', StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(int.Parse(before, System.Globalization.CultureInfo.InvariantCulture) + 1, int.Parse(TempBrain.Git(brain.Home, "rev-list", "--count", "HEAD").Trim(), System.Globalization.CultureInfo.InvariantCulture));
        Assert.Equal([$"Dream {id} on Generics: agreed", $"dreams/{id}.json"], files);
        Assert.Contains("?? conversations/", TempBrain.Git(brain.Home, "status", "--short"), StringComparison.Ordinal);
    }

    [Fact]
    public void TheNextSeedCarriesOnlyTheUnsettledSittings()
    {
        var reference = Talked(sittings: 1);
        brain.Ok("dream", reference);
        var conversation = ConversationStore.Find(Env, reference);
        Trail.Append(Trail.PathOf(conversation.Dir, 2), new Phase(1, Trail.User, DateTimeOffset.UnixEpoch, null, null, "said in sitting 2"));
        conversation = ConversationStore.Sit(Env, reference, "sonnet", "low", DateTimeOffset.UnixEpoch);

        var seed = Seed.For(conversation, "Next.");

        Assert.Contains("<settled>\n# Generics\n\n- Erase generics: the runtime has no types.\n</settled>", seed, StringComparison.Ordinal);
        Assert.DoesNotContain("<trail sitting=\"1\">", seed, StringComparison.Ordinal);
        Assert.Contains("<trail sitting=\"2\">", seed, StringComparison.Ordinal);
    }

    [Fact]
    public void ADreamRefusesWhileASittingIsOpenAndAsksNoModel()
    {
        var reference = Talked(sittings: 1);
        var conversation = ConversationStore.Find(Env, reference);
        Trail.Append(Trail.PathOf(conversation.Dir, 2), new Phase(1, Trail.User, DateTimeOffset.UnixEpoch, null, null, "still talking"));
        var state = File.ReadAllText(Path.Combine(conversation.Dir, "state.md"));

        var (code, _, errors) = brain.Run("dream", reference);

        Assert.NotEqual(0, code);
        Assert.Contains($"sitting 2 of {reference} is open, and a dream never runs while a sitting is", errors, StringComparison.Ordinal);
        Assert.Empty(brain.Models.Calls);
        Assert.Equal(state, File.ReadAllText(Path.Combine(conversation.Dir, "state.md")));
        Assert.Empty(DreamLog.All(Env));
    }

    [Fact]
    public void NothingUnsettledIsNothingToDream()
    {
        var fresh = Ref(brain.Ok("conversation", "start", "personal"));
        var reference = Talked(sittings: 1);
        brain.Ok("dream", reference);

        var (freshCode, _, freshErrors) = brain.Run("dream", fresh);
        var (againCode, _, againErrors) = brain.Run("dream", reference);

        Assert.NotEqual(0, freshCode);
        Assert.Contains($"nothing to dream in {fresh}: no sitting is unsettled", freshErrors, StringComparison.Ordinal);
        Assert.NotEqual(0, againCode);
        Assert.Contains("no sitting is unsettled", againErrors, StringComparison.Ordinal);
    }

    [Fact]
    public void WhileADreamRunsNoSittingStartsOnTheConversation()
    {
        var reference = Talked(sittings: 1);
        var during = new ConcurrentBag<(int Code, string Out, string Err)>();
        brain.Models.During = () => during.Add(brain.Run("conversation", "sitting", reference));

        brain.Ok("dream", reference);

        Assert.NotEmpty(during);
        Assert.All(during, refused => Assert.NotEqual(0, refused.Code));
        Assert.All(during, refused => Assert.Contains($"a dream runs on {reference}", refused.Err, StringComparison.Ordinal));
        Assert.Equal(2, ConversationStore.Find(Env, reference).State.Sittings.Count);
        brain.Ok("conversation", "sitting", reference);
    }

    [Fact]
    public void AModelWithNoAnswerFailsTheDreamAndChangesNothing()
    {
        var reference = Talked(sittings: 1);
        brain.Models.Drafts.Remove("haiku");
        var dir = ConversationStore.Find(Env, reference).Dir;
        var state = File.ReadAllText(Path.Combine(dir, "state.md"));

        var (code, _, errors) = brain.Run("dream", reference);

        Assert.NotEqual(0, code);
        Assert.Contains("the dream failed, and nothing changed: haiku gave no answer", errors, StringComparison.Ordinal);
        Assert.Equal(state, File.ReadAllText(Path.Combine(dir, "state.md")));
        Assert.Equal(string.Empty, File.ReadAllText(Path.Combine(dir, "settled.md")));
        Assert.Empty(DreamLog.All(Env));
    }

    [Fact]
    public void SomethingStagedWhileTheModelsThinkRefusesTheDreamAndChangesNothing()
    {
        var reference = Talked(sittings: 1);
        var dir = ConversationStore.Find(Env, reference).Dir;
        var state = File.ReadAllText(Path.Combine(dir, "state.md"));
        var staging = 0;
        brain.Models.During = () =>
        {
            // The dreamers ask at once; the first of them stages, as the user would once.
            if (Interlocked.Exchange(ref staging, 1) == 0)
            {
                File.WriteAllText(Path.Combine(brain.Home, "staged.txt"), "x");
                TempBrain.Git(brain.Home, "add", "staged.txt");
            }
        };

        var (code, _, errors) = brain.Run("dream", reference);

        Assert.NotEqual(0, code);
        Assert.Contains("the dream could not be committed, and nothing changed:", errors, StringComparison.Ordinal);
        Assert.Contains("has other changes staged", errors, StringComparison.Ordinal);
        Assert.Equal(state, File.ReadAllText(Path.Combine(dir, "state.md")));
        Assert.Equal(string.Empty, File.ReadAllText(Path.Combine(dir, "settled.md")));
        Assert.Empty(DreamLog.All(Env));
        Assert.Equal("A  staged.txt", TempBrain.Git(brain.Home, "status", "--short", "--", "staged.txt").Trim());
    }

    [Fact]
    public void AnEmptyEscalationFailsTheDreamAndChangesNothing()
    {
        var reference = Talked(sittings: 1);
        brain.Models.Comparison = "DIVERGE: they differ";
        brain.Models.Escalation = "SURE\n";
        var dir = ConversationStore.Find(Env, reference).Dir;
        var state = File.ReadAllText(Path.Combine(dir, "state.md"));

        var (code, _, errors) = brain.Run("dream", reference);

        Assert.NotEqual(0, code);
        Assert.Contains("the dream failed, and nothing changed: opus answered no draft", errors, StringComparison.Ordinal);
        Assert.Equal(state, File.ReadAllText(Path.Combine(dir, "state.md")));
        Assert.Equal(string.Empty, File.ReadAllText(Path.Combine(dir, "settled.md")));
        Assert.Empty(DreamLog.All(Env));
    }

    [Fact]
    public void ADreamRefusesBeforeAskingWhenTheDataRepoHasChangesStaged()
    {
        var reference = Talked(sittings: 1);
        File.WriteAllText(Path.Combine(brain.Home, "staged.txt"), "x");
        TempBrain.Git(brain.Home, "add", "staged.txt");

        var (code, _, errors) = brain.Run("dream", reference);

        Assert.NotEqual(0, code);
        Assert.Contains("has other changes staged", errors, StringComparison.Ordinal);
        Assert.Empty(brain.Models.Calls);
    }

    [Theory]
    [InlineData("SURE\nThe draft.", true, null, "The draft.")]
    [InlineData("UNSURE: no yes\nThe draft.", false, "no yes", "The draft.")]
    [InlineData("SURE\n```markdown\nThe draft.\n```", true, null, "The draft.")]
    [InlineData("The draft.", false, "its answer opened with neither SURE nor UNSURE", "The draft.")]
    [InlineData("SURE", false, "its answer holds no draft", "")]
    [InlineData("SURE\n\n", false, "its answer holds no draft", "")]
    public void ADraftSaysWhetherItIsSureAndThenTheDraft(string answer, bool sure, string? doubt, string text) =>
        Assert.Equal(new DreamDraft("haiku", sure, doubt, text), DreamDraft.Parse("haiku", answer));

    [Theory]
    [InlineData("AGREE", true, null)]
    [InlineData("DIVERGE: one lacks the reason\nmore", false, "one lacks the reason")]
    [InlineData("maybe", false, "its answer was neither AGREE nor DIVERGE: maybe")]
    public void TheComparisonAgreesOrSaysWhatDiffers(string answer, bool agreed, string? why) =>
        Assert.Equal(new DreamComparison("sonnet", agreed, why), DreamComparison.Parse("sonnet", answer));

    [Fact]
    public void AStateFromBeforeDreamingReadsWithEverySittingUnsettled()
    {
        const string Old = "---\ntitle: Old\nstatus: active\nborn: 2026-10-01\n---\n\n## Sittings\n\n| sitting | model | effort | started | ended |\n|---|---|---|---|---|\n| 1 | sonnet | low | 2026-10-01T10:00:00Z | - |\n";

        var state = ConversationState.Parse(Old, "state.md");

        Assert.Null(state.Current.Dream);
        Assert.EndsWith("| sitting | model | effort | started | ended | dream |\n|---|---|---|---|---|---|\n| 1 | sonnet | low | 2026-10-01T10:00:00Z | - | - |\n", state.Render(), StringComparison.Ordinal);
    }

    /// <summary>
    /// A personal conversation titled Generics whose first <paramref name="sittings"/> sittings each hold a user's and an
    /// agent's phase, and a fresh sitting after them that nothing was said in yet.
    /// </summary>
    private string Talked(int sittings)
    {
        var reference = Ref(brain.Ok("conversation", "start", "personal"));
        brain.Ok("conversation", "rename", reference, "Generics");
        for (var number = 1; number <= sittings; number++)
        {
            var dir = ConversationStore.Find(Env, reference).Dir;
            var trail = Trail.PathOf(dir, number);
            Trail.Append(trail, new Phase(1, Trail.User, DateTimeOffset.UnixEpoch, null, null, $"said in sitting {number}"));
            Trail.Append(trail, new Phase(2, Trail.Agent, DateTimeOffset.UnixEpoch, "sonnet", "low", "Erase generics."));
            brain.Ok("conversation", "sitting", reference);
        }

        return reference;
    }

    private static string Ref(string output) => Started().Match(output).Groups[1].Value;

    [GeneratedRegex(@"^\w+ ([a-z0-9-]+:[0-9]{4}-[0-9]{2}-[0-9]{2}-[0-9a-f]{6}):")]
    private static partial Regex Started();

    [GeneratedRegex("dream ([0-9]{4}-[0-9]{2}-[0-9]{2}-[0-9a-f]{6}) settled")]
    private static partial Regex DreamId();
}
