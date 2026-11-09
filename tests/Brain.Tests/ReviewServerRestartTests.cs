using System.Net;
using Brain.Serving;

namespace Brain.Tests;

/// <summary>
/// A restart of brain serve with its window open, in a real headless Chromium: the window that was open moves onto the
/// successor in place, trades the code handed to it, and goes on there, with no second window and nothing closed.
/// </summary>
public sealed partial class ReviewServerTests
{
    [WindowFact]
    public async Task ARestartMovesTheOpenWindowOntoTheSuccessorInPlace()
    {
        StartWithOmarchy();
        await InWindow(async window =>
        {
            // The window listens once the theme its stream says on opening is swapped in.
            await window.Until("String(document.querySelector('link[data-theme][href*=\"at=\"]') !== null)", "true").ConfigureAwait(false);
            await window.Eval("String(window.before = true)").ConfigureAwait(false);

            var restart = await Send(HttpMethod.Post, "/api/restart", key: Key).ConfigureAwait(false);
            await serving.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
            var successor = new ReviewServer(brain.Env(TextReader.Null, TextWriter.Null, TextWriter.Null), port, Open, time, new OmarchyTheme(brain.Root, colorCommand: null), Successor, models, GpuWatch.None(time));
            using var stopping = new CancellationTokenSource();
            var served = successor.Serve(window: true, stopping.Token);
            await window.Until("String(window.before === undefined && state.version !== null)", "true").ConfigureAwait(false);
            var closed = await window.Eval("String(state.closed)").ConfigureAwait(false);

            Assert.Equal(HttpStatusCode.Accepted, restart.StatusCode);
            Assert.Single(windows);
            Assert.Equal("null", closed);
            Assert.True(window.Exceptions.Count == 0, string.Join('\n', window.Exceptions));
            await stopping.CancelAsync().ConfigureAwait(false);
            await served.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
        }).ConfigureAwait(false);
    }
}
