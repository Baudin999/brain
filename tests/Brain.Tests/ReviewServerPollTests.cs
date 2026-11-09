namespace Brain.Tests;

/// <summary>
/// The window's polling, in a real headless Chromium on the page brain serve serves: a write asked after the window
/// polled shows in its queue within a few polls, and a read of the pending writes that fails is tried again on the
/// next poll instead of being lost, and a request that never answers ends instead of stopping the polling. They need a
/// Chromium-family browser and are skipped, which a run shows, without one.
/// </summary>
public sealed partial class ReviewServerTests
{
    /// <summary>Longer than the window's ten second limit on a request, and a poll after it.</summary>
    private static readonly TimeSpan HangWait = TimeSpan.FromSeconds(25);

    [WindowFact]
    public async Task AWriteAskedAfterTheWindowPolledShowsInItsQueue()
    {
        await InWindow(async window =>
        {
            var id = Stage();

            await window.Until("state.items.map((item) => item.id).join()", id).ConfigureAwait(false);
            await window.Eval("showInbox(NODE_REVIEW)").ConfigureAwait(false);
            var queue = await window.Eval("document.getElementById('queue').innerText").ConfigureAwait(false);
            Assert.Contains("tea", queue, StringComparison.Ordinal);
        }).ConfigureAwait(false);
    }

    [WindowFact]
    public async Task APendingReadThatFailsIsTriedAgainOnTheNextPoll()
    {
        await InWindow(async window =>
        {
            await window.Eval("""
                (() => {
                  const real = window.fetch;
                  window.pendingFailures = 0;
                  window.fetch = (url, options) => {
                    if (url === '/api/pending' && window.pendingFailures === 0) {
                      window.pendingFailures++;
                      return Promise.resolve(new Response(JSON.stringify({ error: 'refused for the test' }), { status: 500 }));
                    }
                    return real(url, options);
                  };
                })()
                """).ConfigureAwait(false);
            var id = Stage();

            await window.Until("state.items.map((item) => item.id).join()", id).ConfigureAwait(false);
            var failures = await window.Eval("String(window.pendingFailures)").ConfigureAwait(false);
            Assert.Equal("1", failures);
        }).ConfigureAwait(false);
    }

    [WindowFact]
    public async Task AStateReadThatNeverAnswersEndsAndTheNextPollShowsTheWrite()
    {
        await InWindow(async window =>
        {
            await window.Eval(HangFirst("/api/state")).ConfigureAwait(false);
            var id = Stage();

            await window.Until("state.items.map((item) => item.id).join()", id, HangWait).ConfigureAwait(false);
            var hung = await window.Eval("String(window.hung)").ConfigureAwait(false);
            Assert.Equal("1", hung);
        }).ConfigureAwait(false);
    }

    [WindowFact]
    public async Task APendingReadThatNeverAnswersEndsAndTheNextPollShowsTheWrite()
    {
        await InWindow(async window =>
        {
            await window.Eval(HangFirst("/api/pending")).ConfigureAwait(false);
            var id = Stage();

            await window.Until("state.items.map((item) => item.id).join()", id, HangWait).ConfigureAwait(false);
            var hung = await window.Eval("String(window.hung)").ConfigureAwait(false);
            Assert.Equal("1", hung);
        }).ConfigureAwait(false);
    }

    /// <summary>The first request for <paramref name="path"/> gets no answer and fails only when its signal aborts.</summary>
    private static string HangFirst(string path) => $$"""
        (() => {
          const real = window.fetch;
          window.hung = 0;
          window.fetch = (url, options) => {
            if (url === '{{path}}' && window.hung === 0) {
              window.hung++;
              return new Promise((resolve, reject) => options.signal.addEventListener('abort', () => reject(options.signal.reason)));
            }
            return real(url, options);
          };
        })()
        """;

    /// <summary>Opens the server's window in headless Chromium, waits until it polled once, runs <paramref name="test"/>, and closes the browser.</summary>
    private async Task InWindow(Func<Headless, Task> test)
    {
        server.OpenWindow();
        var window = await Headless.Open(windows.Single(), Path.Combine(brain.Root, "chromium")).ConfigureAwait(false);
        try
        {
            await window.Until("String(state.version !== null)", "true").ConfigureAwait(false);
            await test(window).ConfigureAwait(false);
        }
        finally
        {
            await window.DisposeAsync().ConfigureAwait(false);
        }
    }
}
