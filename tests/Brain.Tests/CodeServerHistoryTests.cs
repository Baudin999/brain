using System.Net;

namespace Brain.Tests;

/// <summary>
/// The history of brain code: the open agent's past sessions in one folder, read from the transcripts Claude Code keeps.
/// Another agent's session, a subagent's transcript and another folder's session are left out, and a past session
/// reads as the transcript does.
/// </summary>
public sealed partial class CodeServerTests
{
    private void PastSession(string project, string id, string agent, string words, DateTime written)
    {
        var folder = Path.Combine(config, "projects", project);
        Directory.CreateDirectory(folder);
        var path = Path.Combine(folder, id + ".jsonl");
        File.WriteAllText(path, $$$"""
            {"type":"agent-setting","agentSetting":"{{{agent}}}","sessionId":"{{{id}}}"}
            {"type":"user","message":{"role":"user","content":"{{{words}}}\nsecond line"}}
            {"type":"assistant","message":{"id":"m1","content":[{"type":"text","text":"answer to {{{words}}}"},{"type":"tool_use","id":"t1","name":"Bash","input":{"command":"ls"}}]}}
            """ + "\n");
        File.SetLastWriteTimeUtc(path, written);
    }

    [Fact]
    public async Task TheHistoryListsTheOpenAgentsPastSessionsOfTheFolderAndReadsOne()
    {
        var token = await Trade(Code(windows[0]));
        PastSession("-work-demo-app", "11111111-1111-4111-8111-111111111111", "brain", "newer words", new DateTime(2026, 11, 2, 0, 0, 0, DateTimeKind.Utc));
        PastSession("-work-demo-app", "22222222-2222-4222-8222-222222222222", "brain", "older words", new DateTime(2026, 11, 1, 0, 0, 0, DateTimeKind.Utc));
        PastSession("-work-demo-app", "33333333-3333-4333-8333-333333333333", "builder", "builder words", new DateTime(2026, 11, 3, 0, 0, 0, DateTimeKind.Utc));
        PastSession("-work-other", "44444444-4444-4444-8444-444444444444", "brain", "other folder", new DateTime(2026, 11, 3, 0, 0, 0, DateTimeKind.Utc));
        var subagents = Path.Combine(config, "projects", "-work-demo-app", "11111111-1111-4111-8111-111111111111", "subagents");
        Directory.CreateDirectory(subagents);
        await File.WriteAllTextAsync(Path.Combine(subagents, "agent-a1.jsonl"), """{"type":"user","message":{"role":"user","content":"sub"}}""");

        var listed = await Json(await Send(HttpMethod.Get, "/api/history?folder=" + Uri.EscapeDataString("/work/demo.app"), token: token));
        var read = await Json(await Send(HttpMethod.Get, "/api/history-session?id=11111111-1111-4111-8111-111111111111&folder=" + Uri.EscapeDataString("/work/demo.app"), token: token));
        var refused = await Send(HttpMethod.Get, "/api/history-session?id=33333333-3333-4333-8333-333333333333&folder=" + Uri.EscapeDataString("/work/demo.app"), token: token);

        Assert.Equal(["newer words", "older words"], listed.RootElement.GetProperty("sessions").EnumerateArray().Select((one) => one.GetProperty("title").GetString()!).ToList());
        var events = read.RootElement.GetProperty("events").EnumerateArray().ToList();
        Assert.Equal(["user", "text", "tool"], events.Select((one) => one.GetProperty("kind").GetString()!).ToList());
        Assert.Equal("answer to newer words", events[1].GetProperty("text").GetString());
        Assert.Equal(HttpStatusCode.Conflict, refused.StatusCode);
    }
}
