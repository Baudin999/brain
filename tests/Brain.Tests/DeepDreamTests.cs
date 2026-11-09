using System.Text.Json;
using System.Text.Json.Nodes;
using Brain.Children;
using Brain.Conversations;
using Brain.Dreaming;
using Brain.Serving;

namespace Brain.Tests;

/// <summary>
/// <c>brain dream --deep</c>: a dream settles what is unsettled first, then the open agent, on the deep dream setting,
/// embeds settled.md into the graph from the conversation's repo. It holds the conversation's lock while it runs, runs
/// for the dreams tab, never runs while a sitting is open, and its final words are logged and committed as a deep dream.
/// </summary>
public sealed partial class DreamTests
{
    private static readonly string Turn = Path.Combine(AppContext.BaseDirectory, "Fixtures", "claude-turn.jsonl");

    [Fact]
    public void ADeepDreamSettlesFirstThenTheOpenAgentDreamsIntoBrainAndItsWordsAreLogged()
    {
        var reference = Talked(sittings: 1);
        brain.Claude = StandIn();

        var output = brain.Ok("dream", reference, "--deep");

        var conversation = ConversationStore.Find(Env, reference);
        var settled = conversation.State.Sittings[0].Dream;
        Assert.NotNull(settled);
        Assert.Equal("# Generics\n\n- Erase generics: the runtime has no types.\n", File.ReadAllText(Path.Combine(conversation.Dir, "settled.md")));
        Assert.Equal(OpenAgent.Arguments("opus", "medium"), File.ReadAllLines(Path.Combine(brain.Root, "claude.args")));
        Assert.Equal(brain.Home, File.ReadAllText(Path.Combine(brain.Root, "claude.cwd")).Trim());
        var said = Assert.Single(File.ReadAllLines(Path.Combine(brain.Root, "claude.said")));
        Assert.Contains("for a deep dream of a conversation", said, StringComparison.Ordinal);
        Assert.Contains($"Conversation: {reference}, \\u0022Generics\\u0022", said, StringComparison.Ordinal);
        Assert.Contains($"Its folder: {conversation.Dir}", said, StringComparison.Ordinal);
        Assert.Contains("\\u003Csettled\\u003E\\n# Generics\\n\\n- Erase generics: the runtime has no types.\\n\\u003C/settled\\u003E", said, StringComparison.Ordinal);
        var deep = DreamLog.All(Env).First(record => record.Deep is not null);
        Assert.Equal((DreamRecord.Deeply, reference, (string?)null), (deep.Outcome, deep.Conversation, deep.Why));
        Assert.Equal(new DeepDream("opus", "medium", settled, "All done."), deep.Deep);
        Assert.Empty(deep.Sittings);
        Assert.Equal(deep.Before, deep.After);
        Assert.DoesNotContain(DreamLog.Since(Env, reference, DateTimeOffset.MinValue), record => record.Deep is not null);
        Assert.Equal($"Deep dream {deep.Id} on Generics\ndreams/{deep.Id}.json\n", TempBrain.Git(brain.Home, "log", "-1", "--format=%s", "--name-only").Replace("\n\n", "\n", StringComparison.Ordinal));
        Assert.Contains($"dream {settled} settled sittings 1", output, StringComparison.Ordinal);
        Assert.Contains($"deep dream {deep.Id} on {reference}, opus at medium", output, StringComparison.Ordinal);
        Assert.EndsWith("All done.\n", output, StringComparison.Ordinal);
        Assert.DoesNotContain(ClaudeRuns.Now(), run => run.Place?.Id == reference);
    }

    [Fact]
    public void WithNothingUnsettledTheDeepDreamRunsAloneOnTheModelOfItsSetting()
    {
        var reference = Talked(sittings: 1);
        brain.Ok("dream", reference);
        var asked = brain.Models.Calls.Count;
        BrainSettings.Change(brain.Home, "deep-dream", JsonNode.Parse("""{"model":"sonnet","effort":"high"}"""));
        brain.Claude = StandIn();

        brain.Ok("dream", reference, "--deep");

        Assert.Equal(asked, brain.Models.Calls.Count);
        Assert.Equal(["-p", "--agent", "brain", "--model", "sonnet", "--effort", "high"], File.ReadAllLines(Path.Combine(brain.Root, "claude.args"))[..7]);
        Assert.Null(DreamLog.All(Env).First(record => record.Deep is not null).Deep!.Settled);
    }

    [Fact]
    public void ADeepDreamRefusesWhileASittingIsOpenAndStartsNoAgent()
    {
        var reference = Talked(sittings: 1);
        var conversation = ConversationStore.Find(Env, reference);
        Trail.Append(Trail.PathOf(conversation.Dir, 2), new Phase(1, Trail.User, DateTimeOffset.UnixEpoch, null, null, "still talking"));
        brain.Claude = StandIn();

        var (code, _, errors) = brain.Run("dream", reference, "--deep");

        Assert.NotEqual(0, code);
        Assert.Contains($"sitting 2 of {reference} is open, and a dream never runs while a sitting is", errors, StringComparison.Ordinal);
        Assert.Empty(brain.Models.Calls);
        Assert.False(File.Exists(Path.Combine(brain.Root, "claude.args")));
        Assert.Empty(DreamLog.All(Env));
    }

    /// <summary>
    /// The brief tells the agent to commit each write that landed, in the repo of its scope and never with the
    /// conversation's folder. A stand-in cannot land a write, since only the window answers one, so it commits a node as
    /// the agent would once the user said yes: the dream's log then commits alone on top, and the conversation's folder
    /// stays uncommitted.
    /// </summary>
    [Fact]
    public void TheAgentCommitsEachLandedWriteAndTheLogCommitsAloneOnTop()
    {
        var reference = Talked(sittings: 1);
        brain.Claude = StandIn();
        File.WriteAllText(Path.Combine(brain.Root, "claude.lands"), string.Empty);

        brain.Ok("dream", reference, "--deep");

        using var message = JsonDocument.Parse(File.ReadAllText(Path.Combine(brain.Root, "claude.said")));
        var said = message.RootElement.GetProperty("message").GetProperty("content").GetString();
        Assert.Contains($"`git -C {brain.Home} add graph`, then `git -C {brain.Home} commit", said, StringComparison.Ordinal);
        Assert.Contains("`git -C <repo> add docs/brain/graph`", said, StringComparison.Ordinal);
        Assert.Contains("Never stage or commit the conversation's folder.", said, StringComparison.Ordinal);
        var deep = DreamLog.All(Env).First(record => record.Deep is not null);
        Assert.Equal(
            $"Deep dream {deep.Id} on Generics\ndreams/{deep.Id}.json\nLanded personal:landed\ngraph/personal/landed.md\n",
            TempBrain.Git(brain.Home, "log", "-2", "--format=%s", "--name-only").Replace("\n\n", "\n", StringComparison.Ordinal));
        Assert.Empty(TempBrain.Git(brain.Home, "diff", "--cached", "--name-only"));
        Assert.Contains("settled.md", TempBrain.Git(brain.Home, "status", "--short", "--untracked-files=all", "--", "conversations"), StringComparison.Ordinal);
    }

    /// <summary>
    /// An agent whose turn fails mid-way fails the deep dream, after it is logged and committed alone with why and what
    /// the agent said; the lock goes and nothing of the agent is left running.
    /// </summary>
    [Fact]
    public void AFailedAgentIsLoggedWithWhyAndTheDeepDreamExitsOne()
    {
        var reference = Talked(sittings: 1);
        brain.Ok("dream", reference);
        brain.Claude = StandIn();
        File.WriteAllText(Path.Combine(brain.Root, "claude.fails"), string.Empty);

        var (code, _, errors) = brain.Run("dream", reference, "--deep");

        Assert.Equal(1, code);
        Assert.Contains("the deep dream failed: the agent failed: overloaded", errors, StringComparison.Ordinal);
        var deep = DreamLog.All(Env).Single(record => record.Deep is not null);
        Assert.Equal(("the agent failed: overloaded", "Half done."), (deep.Why, deep.Deep!.Words));
        Assert.Equal($"Deep dream {deep.Id} on Generics\ndreams/{deep.Id}.json\n", TempBrain.Git(brain.Home, "log", "-1", "--format=%s", "--name-only").Replace("\n\n", "\n", StringComparison.Ordinal));
        Assert.DoesNotContain(ClaudeRuns.Now(), run => run.Place?.Id == reference);
        brain.Ok("conversation", "sitting", reference);
    }

    /// <summary>While the agent works, the dream holds the conversation's lock, so no sitting starts, and its run is the dreams tab's.</summary>
    [Fact]
    public async Task WhileADeepDreamRunsItHoldsTheLockAndRunsForTheDreamsTab()
    {
        var reference = Talked(sittings: 1);
        brain.Ok("dream", reference);
        brain.Claude = StandIn();
        await File.WriteAllTextAsync(Path.Combine(brain.Root, "claude.wait"), string.Empty);

        var dreaming = Task.Run(() => DeepDreamer.Dream(Env, reference, TimeProvider.System));
        Assert.True(SpinWait.SpinUntil(() => dreaming.IsCompleted || File.Exists(Path.Combine(brain.Root, "claude.said")), TimeSpan.FromSeconds(10)));
        Assert.False(dreaming.IsFaulted, dreaming.Exception?.ToString());
        var (code, _, errors) = brain.Run("conversation", "sitting", reference);
        var runs = ClaudeRuns.Now().Where(run => run.Place?.Id == reference).Select(run => (run.Place!.Tab, run.Place.Kind, run.Place.Name, run.Model)).ToList();
        File.Delete(Path.Combine(brain.Root, "claude.wait"));
        var dream = await dreaming.WaitAsync(TimeSpan.FromSeconds(20));

        Assert.NotEqual(0, code);
        Assert.Contains($"a dream runs on {reference}", errors, StringComparison.Ordinal);
        Assert.Equal([(WindowTabs.Dreams, AgentEntry.Dream, "deep dream of " + reference, "opus")], runs);
        Assert.Equal("All done.", dream.Deep!.Words);
        brain.Ok("conversation", "sitting", reference);
    }

    /// <summary>
    /// A stand-in for claude, so the gate never calls the model: it keeps its arguments, its directory and each line it
    /// is sent, waits while <c>claude.wait</c> is there, commits a node in the data repo as an agent commits a write that
    /// landed while <c>claude.lands</c> is there, and answers each line with the turn claude 2.1.316 streamed, whose answer
    /// is "All done.". While <c>claude.fails</c> is there, it answers with "Half done." and a turn that failed instead.
    /// </summary>
    private string StandIn()
    {
        var path = Path.Combine(brain.Root, "claude");
        File.WriteAllText(path, $$$$"""
            #!/bin/sh
            here={{{{brain.Root}}}}
            printf '%s\n' "$@" > "$here/claude.args"
            pwd > "$here/claude.cwd"
            while IFS= read -r line; do
              printf '%s\n' "$line" >> "$here/claude.said"
              while [ -e "$here/claude.wait" ]; do sleep 0.05; done
              if [ -e "$here/claude.lands" ]; then
                printf 'a landed node\n' > "{{{{brain.Home}}}}/graph/personal/landed.md"
                git -C "{{{{brain.Home}}}}" add graph
                git -C "{{{{brain.Home}}}}" commit -q -m "Landed personal:landed"
              fi
              if [ -e "$here/claude.fails" ]; then
                echo '{"type":"assistant","parent_tool_use_id":null,"message":{"content":[{"type":"text","text":"Half done."}]}}'
                echo '{"type":"result","subtype":"error_during_execution","is_error":true,"result":"overloaded"}'
              else
                cat "{{{{Turn}}}}"
              fi
            done
            """);
        OmarchyThemeTests.Chmod(path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        return path;
    }
}
