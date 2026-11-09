using System.Globalization;
using System.Net;
using System.Text.Json.Nodes;
using Brain.Serving;

namespace Brain.Tests;

/// <summary>
/// The window as modules and classic scripts together: brain serve serves every file of web/, its components/ and
/// pages/ included, and of shared/, under the page's policy, and in a real headless Chromium the window loads every
/// script with no uncaught error, Settings works by its keys, its model selector and its embedding model picker, and
/// Conversations, Home and the Stylebook render.
/// </summary>
public sealed partial class ReviewServerTests
{
    [Fact]
    public async Task EveryFileOfTheWindowIsServedUnderThePolicy()
    {
        var files = WindowResources.Of("web/", ".js").Concat(WindowResources.Of("web/", ".css")).ToList();

        foreach (var file in files)
        {
            var response = await Send(HttpMethod.Get, file[file.IndexOf('/', StringComparison.Ordinal)..]);

            Assert.True(response.StatusCode == HttpStatusCode.OK, $"{file} answered {response.StatusCode}");
            Assert.Contains("script-src 'self'", response.Headers.GetValues("Content-Security-Policy").Single(), StringComparison.Ordinal);
            var type = file.EndsWith(".css", StringComparison.Ordinal) ? "text/css" : "text/javascript";
            Assert.Equal(type, response.Content.Headers.ContentType?.MediaType);
        }

        Assert.Contains("web/pages/settings.js", files);
        Assert.Contains("shared/components/toggle.css", files);
        Assert.Contains("shared/components.css", files);
    }

    [WindowFact]
    public async Task TheWindowLoadsEveryScriptAndSettingsWorksByItsKeys()
    {
        TempBrain.CommitAll(brain.Home);
        await InWindow(async window =>
        {
            time.Advance(WindowKeys.Arming);
            await window.Until("String(typeof setting === 'function' && setting('judge') !== undefined)", "true").ConfigureAwait(false);

            await window.Eval(Press("`", "Backquote", alt: true)).ConfigureAwait(false);
            await window.Until("state.inbox", ":settings").ConfigureAwait(false);
            await window.Until(FocusedRow, "builder-easy").ConfigureAwait(false);
            await PressKeys(window, "j").ConfigureAwait(false);
            await window.Until(FocusedRow, "builder-difficult").ConfigureAwait(false);
            await PressKeys(window, "k").ConfigureAwait(false);
            await window.Until(FocusedRow, "builder-easy").ConfigureAwait(false);
            await PressKeys(window, "G").ConfigureAwait(false);
            await window.Until(FocusedRow, "atlas").ConfigureAwait(false);
            await PressKeys(window, "g", "g").ConfigureAwait(false);
            await window.Until(FocusedRow, "builder-easy").ConfigureAwait(false);

            await PressKeys(window, "Enter").ConfigureAwait(false);
            await window.Until("String(document.getElementById('selector').hidden)", "false").ConfigureAwait(false);
            await window.Until("document.activeElement.id", "selector-box").ConfigureAwait(false);
            await window.Until(Cursor("Model"), "Sonnet").ConfigureAwait(false);
            await window.Until(Cursor("Effort"), "medium").ConfigureAwait(false);
            await window.Until(CurrentRow, "Sonnet").ConfigureAwait(false);
            await PressKeys(window, "k", "k", "k", "k").ConfigureAwait(false);
            await window.Until(Cursor("Model"), "Haiku").ConfigureAwait(false);
            await PressKeys(window, "l").ConfigureAwait(false);
            await window.Until("document.querySelector('#selector-box .model-selector-column[data-at] .model-selector-title').textContent", "Effort").ConfigureAwait(false);
            await window.Until(CurrentRow, "medium").ConfigureAwait(false);
            await PressKeys(window, "j").ConfigureAwait(false);
            await window.Until(Cursor("Effort"), "high").ConfigureAwait(false);
            await PressKeys(window, "Enter").ConfigureAwait(false);
            await window.Until("String(document.getElementById('selector').hidden)", "true").ConfigureAwait(false);
            await window.Until(RowText("builder-easy"), "Haiku high").ConfigureAwait(false);
            await window.Until(FocusedRow, "builder-easy").ConfigureAwait(false);
            var saved = JsonNode.Parse(await File.ReadAllTextAsync(Path.Combine(brain.Home, BrainSettings.FileName)).ConfigureAwait(false))!["builder-easy"]!;
            Assert.Equal(("haiku", "high"), (saved["model"]!.GetValue<string>(), saved["effort"]!.GetValue<string>()));

            await PressKeys(window, "G").ConfigureAwait(false);
            var was = await window.Eval(Toggle("atlas")).ConfigureAwait(false);
            await window.Eval(LeaveViewportElsewhere).ConfigureAwait(false);
            await PressKeys(window, "Enter").ConfigureAwait(false);
            await window.Until(Toggle("atlas"), was == "true" ? "false" : "true").ConfigureAwait(false);
            await window.Until(ViewportKept, "main:0").ConfigureAwait(false);
            await window.Eval("registered.find((inbox) => inbox.name === ':settings').redraw()").ConfigureAwait(false);
            await window.Until(ViewportKept, "main:0").ConfigureAwait(false);

            await PressKeys(window, "g", "g", "j", "j", "j", "j", "j", "j", "j", "j", "j").ConfigureAwait(false);
            await window.Until(FocusedRow, "embedder").ConfigureAwait(false);
            await window.Until("String(document.querySelector('[data-field=\"embedder\"] .select').textContent.trim().length > 0)", "true").ConfigureAwait(false);
            await PressKeys(window, "Enter").ConfigureAwait(false);
            await window.Until("String(document.querySelectorAll('#selector-box [aria-label=\"Embedding models\"] [role=\"option\"]').length > 0)", "true").ConfigureAwait(false);
            var count = int.Parse(await window.Eval($"String({EmbedderOptions}.length)").ConfigureAwait(false), CultureInfo.InvariantCulture);
            var cursor = int.Parse(await window.Eval(EmbedderCursor).ConfigureAwait(false), CultureInfo.InvariantCulture);
            await PressKeys(window, "j").ConfigureAwait(false);
            await window.Until(EmbedderCursor, Math.Min(cursor + 1, count - 1).ToString(CultureInfo.InvariantCulture)).ConfigureAwait(false);
            await PressKeys(window, "Escape").ConfigureAwait(false);
            await window.Until("String(document.getElementById('selector').hidden)", "true").ConfigureAwait(false);
            await PressKeys(window, "Escape").ConfigureAwait(false);
            await window.Until("String(state.inbox !== ':settings')", "true").ConfigureAwait(false);

            await window.Eval("showInbox(':conversations')").ConfigureAwait(false);
            await window.Until("String(document.getElementById('main').innerText.trim().length > 0)", "true").ConfigureAwait(false);
            await window.Eval("showInbox(':home')").ConfigureAwait(false);
            await window.Until("String(document.querySelector('#main canvas') !== null)", "true").ConfigureAwait(false);
            await window.Eval("showInbox(':stylebook')").ConfigureAwait(false);
            await window.Until("String(document.querySelectorAll('#main .model-selector .option-list, #main .toggle').length >= 6 && document.querySelectorAll('#main .embedder-picker').length === 2 && document.querySelectorAll('#main .embedder-picker-step progress').length === 2 && document.querySelector('#main .embedder-picker-failed') !== null)", "true").ConfigureAwait(false);
            await window.Until("[...['.agent-row', '.agent-message', '.trail-step', '.navarone-signal', '.verdict'].map((one) => document.querySelectorAll('#main ' + one).length)].join()", "8,2,5,8,2").ConfigureAwait(false);
            await window.Until("[...['.agent-flow', '.agent-flow-work', '.agent-flow-step', '.agent-flow-check'].map((one) => document.querySelectorAll('#main ' + one).length)].join()", "2,4,10,3").ConfigureAwait(false);

            Assert.True(window.Exceptions.Count == 0, string.Join('\n', window.Exceptions));
        }).ConfigureAwait(false);
    }

    /// <summary>
    /// A node's markdown draws its mermaid diagram wherever the node is read, and a click on the diagram opens the image
    /// viewer: scroll zooms it, Alt+R resets it and Esc closes it, none of them reaching a handler of the page, which hears
    /// both keys again once the viewer is closed.
    /// </summary>
    [WindowFact]
    public async Task ADiagramInANodeOpensInTheImageViewerThatTakesItsKeys()
    {
        TempBrain.CommitAll(brain.Home);
        await InWindow(async window =>
        {
            time.Advance(WindowKeys.Arming);
            await window.Until("String(typeof markdownView === 'function')", "true").ConfigureAwait(false);
            await window.Eval("fill($('main'), doc({ ref: 'x', slug: 'x', title: 'X', subjects: [], edges: [], body: '```mermaid\\nflowchart LR\\n  a --> b\\n```' }))").ConfigureAwait(false);
            await window.Until("String(document.querySelectorAll('#main figure.diagram svg').length)", "1").ConfigureAwait(false);
            await window.Eval("window.heard = 0; document.addEventListener('keydown', () => { window.heard++; })").ConfigureAwait(false);

            await window.Eval("document.querySelector('#main figure.diagram svg').dispatchEvent(new MouseEvent('click', { bubbles: true }))").ConfigureAwait(false);
            await window.Until("String(document.querySelectorAll('.image-viewer svg').length)", "1").ConfigureAwait(false);
            await window.Eval("document.querySelector('.image-viewer').dispatchEvent(new WheelEvent('wheel', { deltaY: -400, clientX: 10, clientY: 10, bubbles: true, cancelable: true }))").ConfigureAwait(false);
            Assert.DoesNotContain("scale(1)", await window.Eval(Zoom).ConfigureAwait(false), StringComparison.Ordinal);

            var before = await window.Eval("state.imageViewer.x + ' ' + state.imageViewer.y").ConfigureAwait(false);
            await window.Eval(DragViewer).ConfigureAwait(false);
            var after = await window.Eval("state.imageViewer.x + ' ' + state.imageViewer.y").ConfigureAwait(false);
            var moved = (Offset(before), Offset(after));
            Assert.Equal((moved.Item1.X + 40, moved.Item1.Y + 20), (moved.Item2.X, moved.Item2.Y));
            Assert.Contains($"translate({Math.Round(moved.Item2.X)}", await window.Eval(Zoom).ConfigureAwait(false), StringComparison.Ordinal);

            await window.Eval(Press("r", "KeyR", alt: true)).ConfigureAwait(false);
            Assert.EndsWith("scale(1)", await window.Eval(Zoom).ConfigureAwait(false), StringComparison.Ordinal);
            await window.Eval(Press("Escape", "Escape")).ConfigureAwait(false);
            await window.Until("String(document.querySelectorAll('.image-viewer').length)", "0").ConfigureAwait(false);
            Assert.Equal("0", await window.Eval("String(window.heard)").ConfigureAwait(false));

            await window.Eval(Press("Escape", "Escape")).ConfigureAwait(false);
            Assert.Equal("1", await window.Eval("String(window.heard)").ConfigureAwait(false));
            Assert.True(window.Exceptions.Count == 0, string.Join('\n', window.Exceptions));
        }).ConfigureAwait(false);
    }

    /// <summary>A drag on the viewer: the pointer goes down, moves 40 across and 20 down, and goes up.</summary>
    private const string DragViewer = """
        (() => {
          const frame = document.querySelector('.image-viewer');
          const at = (type, x, y) => frame.dispatchEvent(new PointerEvent(type, { button: 0, pointerId: 7, clientX: x, clientY: y, bubbles: true }));
          at('pointerdown', 100, 100);
          at('pointermove', 140, 120);
          at('pointerup', 140, 120);
        })()
        """;

    private static (double X, double Y) Offset(string pair)
    {
        var parts = pair.Split(' ');
        return (double.Parse(parts[0], CultureInfo.InvariantCulture), double.Parse(parts[1], CultureInfo.InvariantCulture));
    }

    private const string Zoom = "document.querySelector('.image-viewer-stage').style.transform";

    private const string EmbedderOptions = "[...document.querySelectorAll('#selector-box [aria-label=\"Embedding models\"] [role=\"option\"]')]";

    /// <summary>The index of the embedding model picker's cursor.</summary>
    private const string EmbedderCursor = "String(" + EmbedderOptions + ".findIndex((option) => option.getAttribute('aria-selected') === 'true'))";

    /// <summary>
    /// The window launches only once every script ran. With chat.js held back long after the launch call could answer,
    /// the window has no session yet, and once chat.js loads it launches, polls and reports no error. Launched while a
    /// later script still loaded, it drew the strip with a function that script had not defined yet, and never polled.
    /// </summary>
    [WindowFact]
    public async Task TheWindowLaunchesOnlyOnceEveryScriptRan()
    {
        server.OpenWindow();
        var window = await Headless.Open(windows.Single(), Path.Combine(brain.Root, "chromium"), hold: "*/chat.js").ConfigureAwait(false);
        try
        {
            await window.UntilHeld().ConfigureAwait(false);
            await Task.Delay(LaunchAnswers).ConfigureAwait(false);
            var token = await window.Eval("String(state.token)").ConfigureAwait(false);
            await window.Release().ConfigureAwait(false);

            Assert.Equal("null", token);
            await window.Until("String(state.version !== null)", "true").ConfigureAwait(false);
            Assert.True(window.Exceptions.Count == 0, string.Join('\n', window.Exceptions));
        }
        finally
        {
            await window.DisposeAsync().ConfigureAwait(false);
        }
    }

    /// <summary>Far longer than brain serve takes to answer the launch call on this machine.</summary>
    private static readonly TimeSpan LaunchAnswers = TimeSpan.FromSeconds(2);

    /// <summary>
    /// Makes the page taller than the window, puts the focus on main instead of the current row and the scroll at the top,
    /// far from the current row at the bottom of the page.
    /// </summary>
    private const string LeaveViewportElsewhere =
        "document.querySelector('.settings').style.paddingBottom = '3000px'; const main = document.getElementById('main'); main.scrollTop = 0; main.focus()";

    /// <summary>Where the focus and the scroll are: a redraw that no key asked for leaves them as they were.</summary>
    private const string ViewportKept = "document.activeElement.id + ':' + document.getElementById('main').scrollTop";

    /// <summary>The setting the browser's focus is on, by its field row.</summary>
    private const string FocusedRow = "document.activeElement.dataset.field";

    /// <summary>The text of the model selector's current row: the cursor of the list the keys are in.</summary>
    private const string CurrentRow = "document.querySelector('#selector-box [aria-current=\"true\"] .option-list-text').textContent";

    /// <summary>The text under the cursor of the model selector's list with this title.</summary>
    private static string Cursor(string title) =>
        $"[...document.querySelectorAll('#selector-box .model-selector-column')].find((column) => column.querySelector('.model-selector-title').textContent === '{title}').querySelector('[aria-selected=\"true\"] .option-list-text').textContent";

    private static string RowText(string field) =>
        $"document.querySelector('[data-field=\"{field}\"] .select').textContent.replace(/\\s+/g, ' ').trim()";

    private static string Toggle(string field) => $"document.querySelector('[data-field=\"{field}\"] .toggle').getAttribute('aria-checked')";

    /// <summary>A key pressed where the browser's focus is, as the window's key handlers hear it.</summary>
    private static string Press(string key, string code, bool alt = false) =>
        $"document.activeElement.dispatchEvent(new KeyboardEvent('keydown', {{ key: '{key}', code: '{code}', altKey: {(alt ? "true" : "false")}, bubbles: true, cancelable: true }}))";

    private static async Task PressKeys(Headless window, params string[] keys)
    {
        foreach (var key in keys)
        {
            await window.Eval(Press(key, string.Empty)).ConfigureAwait(false);
        }
    }
}
