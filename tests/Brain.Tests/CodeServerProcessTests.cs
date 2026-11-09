using System.Diagnostics;
using System.Globalization;
using System.Net;
using Brain.Children;

namespace Brain.Tests;

/// <summary>
/// The processes a session's agent started, in brain code's server and window: the session lists each job that still
/// runs by its pid with its whole command, and a stop ends that job and everything below it while the session goes
/// on; a pid that is not one of the session's jobs is refused and never signalled, and a job already gone answers as
/// gone. The window counts them in a chip in the session's head, folds them out over the conversation, and stops one,
/// and the head keeps its height as they come and go.
/// </summary>
public sealed partial class CodeServerTests
{
    [Fact]
    public async Task AStopEndsOneOfTheSessionsJobsWithWhatIsBelowItAndNoOtherProcess()
    {
        var token = await Trade(Code(windows[0]));
        var id = await StartSession();
        await Say(token, id, "start a background job");
        var job = await StartedJob();
        var listed = await SessionWhen(token, id, (session) => session.GetProperty("running").GetArrayLength() == 1);
        Assert.True(SpinWait.SpinUntil(() => ProcessTree.Read().Children(job).Count == 1, Wait));
        var below = ProcessTree.Read().Children(job)[0];
        using var foreign = Process.Start("sleep", "30");

        var refused = await EndProcess(token, id, foreign.Id);
        var unnamed = await Send(HttpMethod.Post, "/api/end-process", Json(new { session = id }), token: token, origin: server.Origin);
        var zero = await EndProcess(token, id, 0);
        var negative = await EndProcess(token, id, -1);
        var ended = await EndProcess(token, id, job);
        var after = await SessionWhen(token, id, (session) => session.GetProperty("running").GetArrayLength() == 0);
        var again = await EndProcess(token, id, job);

        var running = listed.GetProperty("running")[0];
        Assert.Equal(job, running.GetProperty("pid").GetInt32());
        Assert.Equal("sh -c sleep 300 & wait", running.GetProperty("command").GetString());
        Assert.False(running.GetProperty("slot").GetBoolean());
        Assert.False(running.TryGetProperty("idle", out _));
        Assert.Equal(HttpStatusCode.Conflict, refused.StatusCode);
        Assert.False(foreign.HasExited);
        Assert.Equal(HttpStatusCode.Conflict, unnamed.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, zero.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, negative.StatusCode);
        Assert.Equal(HttpStatusCode.OK, ended.StatusCode);
        Assert.Contains("\"gone\":false", await ended.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        Assert.True(ReviewServerTests.Gone(job));
        Assert.True(ReviewServerTests.Gone(below));
        Assert.True(after.GetProperty("alive").GetBoolean());
        Assert.Contains("\"gone\":true", await again.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        foreign.Kill();
    }

    /// <summary>
    /// The window in a real browser: a job the session's agent started shows as a chip in the session's head, which
    /// folds out the job over the conversation; Esc folds it away, and the job's stop ends it, and the chip goes. The
    /// head is as high with the chip as without it.
    /// </summary>
    [WindowFact]
    public async Task TheWindowFoldsOutTheSessionsJobsAndStopsOneWhileTheHeadKeepsItsHeight()
    {
        const string Head = "String(Math.round(document.querySelector('.code-head').getBoundingClientRect().height))";
        var window = await Headless.Open(windows[0], Path.Combine(brain.Root, "chromium")).ConfigureAwait(false);
        try
        {
            await window.Until("document.querySelector('.code-new') !== null", "true").ConfigureAwait(false);
            await StartSession().ConfigureAwait(false);
            await window.Until("document.querySelector('.code-session')?.getAttribute('aria-current')", "true").ConfigureAwait(false);
            var before = await window.Eval(Head).ConfigureAwait(false);
            await window.Eval("const box = document.querySelector('.code-words'); box.value = 'start a background job'; box.dispatchEvent(new Event('input')); box.dispatchEvent(new KeyboardEvent('keydown', { key: 'Enter', bubbles: true }))").ConfigureAwait(false);
            await StartedJob().ConfigureAwait(false);

            await window.Until("document.querySelector('.process-fold .chip')?.textContent", "1 running").ConfigureAwait(false);
            var with = await window.Eval(Head).ConfigureAwait(false);
            await window.Eval("document.querySelector('.process-fold .chip').click()").ConfigureAwait(false);
            await window.Until("document.querySelector('.process-fold-command')?.title", "sh -c sleep 300 & wait").ConfigureAwait(false);
            var open = await window.Eval(Head).ConfigureAwait(false);
            await window.Eval("document.dispatchEvent(new KeyboardEvent('keydown', { key: 'Escape', bubbles: true }))").ConfigureAwait(false);
            await window.Until("document.querySelector('.process-fold-list') === null", "true").ConfigureAwait(false);
            await window.Eval("document.querySelector('.process-fold .chip').click()").ConfigureAwait(false);
            await window.Eval("document.querySelector('.process-fold-job .button.no').click()").ConfigureAwait(false);
            await window.Until("document.querySelector('.process-fold') === null", "true").ConfigureAwait(false);
            var after = await window.Eval(Head).ConfigureAwait(false);

            Assert.Equal(before, with);
            Assert.Equal(before, open);
            Assert.Equal(before, after);
            Assert.DoesNotContain("control_request", await File.ReadAllTextAsync(Path.Combine(brain.Root, "claude.said")).ConfigureAwait(false), StringComparison.Ordinal);
            Assert.True(window.Exceptions.Count == 0, string.Join('\n', window.Exceptions));
        }
        finally
        {
            await window.DisposeAsync().ConfigureAwait(false);
        }
    }

    /// <summary>The pid of the job the stand-in started for words with "background" in them, once it wrote it.</summary>
    private async Task<int> StartedJob()
    {
        var path = Path.Combine(brain.Root, "claude.job");
        Assert.True(SpinWait.SpinUntil(() => File.Exists(path) && new FileInfo(path).Length > 0, Wait));
        var said = await File.ReadAllTextAsync(path).ConfigureAwait(false);
        return int.Parse(said.Trim(), CultureInfo.InvariantCulture);
    }

    private Task<HttpResponseMessage> EndProcess(string token, string id, int pid) =>
        Send(HttpMethod.Post, "/api/end-process", Json(new { session = id, pid }), token: token, origin: server.Origin);
}
