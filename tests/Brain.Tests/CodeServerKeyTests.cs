namespace Brain.Tests;

public sealed partial class CodeServerTests
{
    private const string PressCtrlC = "document.dispatchEvent(new KeyboardEvent('keydown', { key: 'c', ctrlKey: true, bubbles: true, cancelable: true }))";
    private const string PressEsc = "document.dispatchEvent(new KeyboardEvent('keydown', { key: 'Escape', bubbles: true, cancelable: true }))";

    /// <summary>
    /// The keys in a real browser, while a turn works: Esc closes the information panel and never opens the stop
    /// confirm or interrupts; with nothing open it does nothing at all.
    /// </summary>
    [WindowFact]
    public async Task WhileATurnWorksEscClosesTheInformationPanelAndNeverInterrupts()
    {
        var id = Guid.NewGuid().ToString("D");
        WriteTranscript(
            "q",
            id,
            $$$"""{"type":"user","cwd":"{{{project}}}","message":{"role":"user","content":"look around"}}""",
            Use("c1", "Bash", """{"command":"ls /x","description":"List x"}"""),
            Result("c1", "a.cs\\nb.cs"));
        var window = await Headless.Open(windows[0], Path.Combine(brain.Root, "chromium")).ConfigureAwait(false);
        try
        {
            await window.Until("document.querySelector('.code-new') !== null", "true").ConfigureAwait(false);
            await Send(HttpMethod.Post, "/api/key/start", Json(new { agent = "brain", cwd = project, resume = id }), key: Key()).ConfigureAwait(false);
            await window.Until("document.querySelector('.tool-call') !== null", "true").ConfigureAwait(false);
            await window.Eval("document.querySelector('.tool-call').click()").ConfigureAwait(false);
            await window.Until("document.querySelector('.info-panel-title')?.textContent", "List x").ConfigureAwait(false);
            await window.Eval("const box = document.querySelector('.code-words'); box.value = 'stream it'; box.dispatchEvent(new Event('input')); box.dispatchEvent(new KeyboardEvent('keydown', { key: 'Enter', bubbles: true }))").ConfigureAwait(false);
            await window.Until("document.querySelector('.code-composer .button.no') !== null", "true").ConfigureAwait(false);

            await window.Eval(PressEsc).ConfigureAwait(false);
            await window.Until("document.querySelector('.info-panel') === null", "true").ConfigureAwait(false);
            await window.Eval(PressEsc).ConfigureAwait(false);

            var confirm = await window.Eval("String(document.querySelector('.stop-confirm') !== null)").ConfigureAwait(false);
            var working = await window.Eval("String(document.querySelector('.code-composer .button.no') !== null)").ConfigureAwait(false);
            var said = await File.ReadAllTextAsync(Path.Combine(brain.Root, "claude.said")).ConfigureAwait(false);
            Assert.Equal("false", confirm);
            Assert.Equal("true", working);
            Assert.DoesNotContain("control_request", said, StringComparison.Ordinal);
            Assert.True(window.Exceptions.Count == 0, string.Join('\n', window.Exceptions));
        }
        finally
        {
            await window.DisposeAsync().ConfigureAwait(false);
        }
    }

    /// <summary>
    /// The keys in a real browser: Ctrl+C with text selected copies and does not interrupt, Ctrl+C with none opens the
    /// stop confirm while an agent the session sent runs, and Esc then keeps it running.
    /// </summary>
    [WindowFact]
    public async Task CtrlCInterruptsTheWorkingTurnUnlessTextIsSelected()
    {
        var window = await Headless.Open(windows[0], Path.Combine(brain.Root, "chromium")).ConfigureAwait(false);
        try
        {
            await window.Until("document.querySelector('.code-new') !== null", "true").ConfigureAwait(false);
            await StartSession().ConfigureAwait(false);
            await window.Until("document.querySelector('.code-session')?.getAttribute('aria-current')", "true").ConfigureAwait(false);
            await window.Eval("const box = document.querySelector('.code-words'); box.value = 'stream with an agent'; box.dispatchEvent(new Event('input')); box.dispatchEvent(new KeyboardEvent('keydown', { key: 'Enter', bubbles: true }))").ConfigureAwait(false);
            await window.Until("document.querySelector('.code-said .prose')?.textContent.trim()", "I hear you").ConfigureAwait(false);
            await window.Until("document.querySelector('.code-events .agent-row-state')?.textContent", "running").ConfigureAwait(false);

            await window.Eval("getSelection().selectAllChildren(document.querySelector('.code-said .prose'))").ConfigureAwait(false);
            await window.Eval(PressCtrlC).ConfigureAwait(false);
            var selected = await window.Eval("String(document.querySelector('.stop-confirm') !== null)").ConfigureAwait(false);

            await window.Eval("getSelection().removeAllRanges()").ConfigureAwait(false);
            await window.Eval(PressCtrlC).ConfigureAwait(false);
            await window.Until("document.querySelector('.stop-confirm') !== null", "true").ConfigureAwait(false);
            await window.Eval(PressEsc).ConfigureAwait(false);
            await window.Until("document.querySelector('.stop-confirm') === null", "true").ConfigureAwait(false);

            var said = await File.ReadAllTextAsync(Path.Combine(brain.Root, "claude.said")).ConfigureAwait(false);
            Assert.Equal("false", selected);
            Assert.DoesNotContain("control_request", said, StringComparison.Ordinal);
            Assert.True(window.Exceptions.Count == 0, string.Join('\n', window.Exceptions));
        }
        finally
        {
            await window.DisposeAsync().ConfigureAwait(false);
        }
    }

    /// <summary>
    /// The close confirm in a real browser: Close asks first, and Esc, a click outside the box and Keep leave the
    /// session running. The modal takes focus on Keep and holds the keys, and a second click at the header's Close
    /// lands on the shade; only the confirm closes the session.
    /// </summary>
    [WindowFact]
    public async Task CloseAsksInAModalAndOnlyTheConfirmClosesTheSession()
    {
        var window = await Headless.Open(windows[0], Path.Combine(brain.Root, "chromium")).ConfigureAwait(false);
        try
        {
            await window.Until("document.querySelector('.code-new') !== null", "true").ConfigureAwait(false);
            await StartSession().ConfigureAwait(false);
            await window.Until("document.querySelector('.code-session')?.getAttribute('aria-current')", "true").ConfigureAwait(false);
            await window.Eval("const box = document.querySelector('.code-words'); box.value = 'stream with an agent'; box.dispatchEvent(new Event('input')); box.dispatchEvent(new KeyboardEvent('keydown', { key: 'Enter', bubbles: true }))").ConfigureAwait(false);
            await window.Until("document.querySelector('.code-events .agent-row-state')?.textContent", "running").ConfigureAwait(false);
            const string Ask = "document.querySelector('.code-head-actions .button.no').click()";
            const string Shown = "document.querySelector('.palette .stop-confirm') !== null";
            const string Gone = "document.querySelector('.palette') === null";

            await window.Eval(Ask).ConfigureAwait(false);
            await window.Until(Shown, "true").ConfigureAwait(false);
            await window.Eval(PressEsc).ConfigureAwait(false);
            await window.Until(Gone, "true").ConfigureAwait(false);

            await window.Eval(Ask).ConfigureAwait(false);
            await window.Until(Shown, "true").ConfigureAwait(false);
            await window.Eval("document.querySelector('.palette').click()").ConfigureAwait(false);
            await window.Until(Gone, "true").ConfigureAwait(false);

            await window.Eval(Ask).ConfigureAwait(false);
            await window.Until(Shown, "true").ConfigureAwait(false);
            await window.Eval("document.querySelector('.palette .button:not(.no)').click()").ConfigureAwait(false);
            await window.Until(Gone, "true").ConfigureAwait(false);

            // A second click at the place of the header's Close lands on the shade, and keeps the session.
            await window.Eval(Ask).ConfigureAwait(false);
            await window.Until(Shown, "true").ConfigureAwait(false);
            await window.Eval("const r = document.querySelector('.code-head-actions .button.no').getBoundingClientRect(); document.elementFromPoint(r.x + r.width / 2, r.y + r.height / 2).click()").ConfigureAwait(false);
            await window.Until(Gone, "true").ConfigureAwait(false);
            var early = await window.Eval("String(document.querySelector('.code-session') !== null)").ConfigureAwait(false);

            await window.Eval(Ask).ConfigureAwait(false);
            await window.Until(Shown, "true").ConfigureAwait(false);
            var focused = await window.Eval("String(document.activeElement?.closest('.palette') !== null && !document.activeElement.classList.contains('no'))").ConfigureAwait(false);
            await window.Eval(PressCtrlC).ConfigureAwait(false);
            var under = await window.Eval("String(document.querySelector('.stop-confirm:not(.palette .stop-confirm)') !== null)").ConfigureAwait(false);
            await window.Eval("document.querySelector('.palette .button.no').click()").ConfigureAwait(false);
            await window.Until("document.querySelector('.code-session') === null", "true").ConfigureAwait(false);

            Assert.Equal("true", early);
            Assert.Equal("true", focused);
            Assert.Equal("false", under);
            Assert.True(window.Exceptions.Count == 0, string.Join('\n', window.Exceptions));
        }
        finally
        {
            await window.DisposeAsync().ConfigureAwait(false);
        }
    }
}
