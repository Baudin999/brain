using System.Text.Json;
using Brain.Serving;

namespace Brain.Tests;

/// <summary>
/// The Navarone tab in a real headless Chromium: the navarone line of sources.tsv makes one tab, labelled Navarone,
/// which counts no escalation while none waits and says so. Its signals list each problem by its method; a row opens
/// the problem's story with its code; the lens opens over it by its key, a call mark makes that method the subject, u
/// steps back, a node page opened over the tab hides the lens until the tab's page is back, and Esc closes the lens onto
/// the story, then the story onto the signals.
/// </summary>
public sealed partial class ReviewServerTests
{
    private const string Before = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
    private const string After = "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb";

    [WindowFact]
    public async Task TheNavaroneTabShowsItsFlowsAProblemsStoryAndTheLens()
    {
        WriteNavarone();
        TempBrain.CommitAll(brain.Home);
        await InWindow(async window =>
        {
            time.Advance(WindowKeys.Arming);
            await window.Until("String(registered.filter((inbox) => inbox.label === 'Navarone').length)", "1").ConfigureAwait(false);
            Assert.Equal("0", await window.Eval("String(registered.filter((inbox) => inbox.name === 'navarone' && inbox.label !== 'Navarone').length)").ConfigureAwait(false));

            await window.Eval("showInbox('navarone')").ConfigureAwait(false);
            await window.Until("String(document.getElementById('main').innerText.includes('Nothing waits for you'))", "true").ConfigureAwait(false);
            Assert.Equal("0", await window.Eval("String(document.querySelectorAll('#strip .strip-count').length)").ConfigureAwait(false));

            await PressKeys(window, "2").ConfigureAwait(false);
            await window.Until("document.querySelector('.navarone-page .inbox-row .inbox-title')?.textContent ?? ''", "DeterministicProblemClassifier.Classify").ConfigureAwait(false);
            await PressKeys(window, "Enter").ConfigureAwait(false);
            await window.Until("document.querySelector('.inbox-group-name')?.textContent ?? ''", "DeterministicProblemClassifier.Classify").ConfigureAwait(false);
            await PressKeys(window, "Enter").ConfigureAwait(false);
            await window.Until("String(document.querySelectorAll('.navarone-step').length)", "2").ConfigureAwait(false);
            await window.Until($"String(document.querySelector('.navarone-story').innerText.includes('code of {After}'))", "true").ConfigureAwait(false);

            await window.Eval(Press("l", "KeyL", alt: true)).ConfigureAwait(false);
            await window.Until("document.querySelector('.navarone-trail code')?.textContent ?? ''", "DeterministicProblemClassifier.Classify").ConfigureAwait(false);
            await window.Until("String(document.querySelectorAll('.navarone-lens .mark-link').length > 0)", "true").ConfigureAwait(false);
            Assert.Contains("Navarone's lens open on DeterministicProblemClassifier.Classify", await window.Eval("shownInbox().context()").ConfigureAwait(false), StringComparison.Ordinal);
            await window.Eval("document.querySelector('.navarone-lens .mark-link').click()").ConfigureAwait(false);
            await window.Until("document.querySelector('.navarone-trail code')?.textContent ?? ''", "Methods.Of").ConfigureAwait(false);
            await window.Until("String(document.querySelector('.navarone-lens').innerText.includes('SelectMany'))", "true").ConfigureAwait(false);
            await PressKeys(window, "u").ConfigureAwait(false);
            await window.Until("document.querySelector('.navarone-trail code')?.textContent ?? ''", "DeterministicProblemClassifier.Classify").ConfigureAwait(false);

            await window.Eval("openMain('no-such-node')").ConfigureAwait(false);
            await window.Until("String(state.view === 'node' && document.querySelector('.navarone-lens') === null)", "true").ConfigureAwait(false);
            await window.Eval("showReview()").ConfigureAwait(false);
            await window.Until("document.querySelector('.navarone-trail code')?.textContent ?? ''", "DeterministicProblemClassifier.Classify").ConfigureAwait(false);
            await window.Eval("document.querySelector('.navarone-lens').focus()").ConfigureAwait(false);

            await PressKeys(window, "Escape").ConfigureAwait(false);
            await window.Until("String(document.querySelector('.navarone-lens') === null)", "true").ConfigureAwait(false);
            Assert.Equal("1", await window.Eval("String(document.querySelectorAll('.navarone-story').length)").ConfigureAwait(false));
            await PressKeys(window, "Escape").ConfigureAwait(false);
            await window.Until("document.querySelector('.inbox-group-name')?.textContent ?? ''", "DeterministicProblemClassifier.Classify").ConfigureAwait(false);

            Assert.True(window.Exceptions.Count == 0, string.Join('\n', window.Exceptions));
        }).ConfigureAwait(false);
    }

    [WindowFact]
    public async Task TheSignalsFoldByMethodAndTheSearchNarrowsThemByName()
    {
        WriteNavarone();
        TempBrain.CommitAll(brain.Home);
        await InWindow(async window =>
        {
            time.Advance(WindowKeys.Arming);
            await window.Until("String(registered.filter((inbox) => inbox.label === 'Navarone').length)", "1").ConfigureAwait(false);
            await window.Eval("showInbox('navarone')").ConfigureAwait(false);
            await PressKeys(window, "2").ConfigureAwait(false);

            await window.Until("String(document.querySelectorAll('.navarone-page .inbox-row').length)", "2").ConfigureAwait(false);
            Assert.Equal("DeterministicProblemClassifier.Classify|1 problem;Methods.Of|2 problems", await window.Eval(MethodRows).ConfigureAwait(false));

            await Search(window, "methods").ConfigureAwait(false);
            await window.Until("String(document.querySelectorAll('.navarone-page .inbox-row').length)", "1").ConfigureAwait(false);
            Assert.Equal("Methods.Of|2 problems", await window.Eval(MethodRows).ConfigureAwait(false));

            await Search(window, "nothing like it").ConfigureAwait(false);
            await window.Until("String(document.querySelector('.navarone-none')?.textContent ?? '')", "No method matches the search.").ConfigureAwait(false);
            Assert.Equal("1", await window.Eval("String(document.querySelectorAll('.navarone-page input.search').length)").ConfigureAwait(false));

            Assert.True(window.Exceptions.Count == 0, string.Join('\n', window.Exceptions));
        }).ConfigureAwait(false);
    }

    [WindowFact]
    public async Task AChatSummonedOnAnOpenMethodGetsItsNameAndProblemsAndNoneWithoutOne()
    {
        WriteNavarone();
        TempBrain.CommitAll(brain.Home);
        await InWindow(async window =>
        {
            time.Advance(WindowKeys.Arming);
            await window.Until("String(registered.filter((inbox) => inbox.label === 'Navarone').length)", "1").ConfigureAwait(false);
            await window.Eval("showInbox('navarone')").ConfigureAwait(false);
            await PressKeys(window, "2").ConfigureAwait(false);
            await window.Until("String(document.querySelectorAll('.navarone-page .inbox-row').length)", "2").ConfigureAwait(false);
            Assert.Equal("null", await window.Eval("String(shownInbox().context())").ConfigureAwait(false));

            await Search(window, "methods").ConfigureAwait(false);
            await PressKeys(window, "Enter").ConfigureAwait(false);
            await window.Until("document.querySelector('.inbox-group-name')?.textContent ?? ''", "Methods.Of").ConfigureAwait(false);
            var context = await window.Eval("shownInbox().context()").ConfigureAwait(false);
            Assert.Contains("the method Methods.Of open in Navarone's signals, with its 2 problems", context, StringComparison.Ordinal);
            Assert.Equal(2, context.Split('\n').Count(line => line.StartsWith("- ", StringComparison.Ordinal)));

            await PressKeys(window, "1").ConfigureAwait(false);
            await window.Until("String(shownInbox().context())", "null").ConfigureAwait(false);
            await PressKeys(window, "2").ConfigureAwait(false);
            await window.Until("String(shownInbox().context()).includes('the method Methods.Of open')", "true").ConfigureAwait(false);

            await PressKeys(window, "Escape").ConfigureAwait(false);
            await window.Until("String(document.querySelector('.inbox-group-name') === null)", "true").ConfigureAwait(false);
            Assert.Equal("null", await window.Eval("String(shownInbox().context())").ConfigureAwait(false));

            Assert.True(window.Exceptions.Count == 0, string.Join('\n', window.Exceptions));
        }).ConfigureAwait(false);
    }

    [WindowFact]
    public async Task InsideTheSearchBoxTheKeysAreTextAndTheArrowsAndEnterStillWork()
    {
        WriteNavarone();
        TempBrain.CommitAll(brain.Home);
        await InWindow(async window =>
        {
            time.Advance(WindowKeys.Arming);
            await window.Until("String(registered.filter((inbox) => inbox.label === 'Navarone').length)", "1").ConfigureAwait(false);
            await window.Eval("showInbox('navarone')").ConfigureAwait(false);
            await PressKeys(window, "2").ConfigureAwait(false);
            await window.Until("String(document.querySelectorAll('.navarone-page .inbox-row').length)", "2").ConfigureAwait(false);

            await window.Eval("document.querySelector('.navarone-page input.search').focus()").ConfigureAwait(false);
            await PressKeys(window, "j", "k", "1", "2").ConfigureAwait(false);
            Assert.Equal("true", await window.Eval("String(document.querySelector('.navarone-flows [aria-pressed=\"true\"]').textContent.includes('Signals'))").ConfigureAwait(false));
            Assert.Equal("1", await window.Eval("String(document.querySelectorAll('.navarone-page input.search').length)").ConfigureAwait(false));
            Assert.Equal("true", await window.Eval("String(document.activeElement.classList.contains('search'))").ConfigureAwait(false));

            await Search(window, "e").ConfigureAwait(false);
            await PressKeys(window, "ArrowDown").ConfigureAwait(false);
            await window.Until("document.querySelector('.navarone-page .inbox-row[aria-current=\"true\"] .inbox-title')?.textContent ?? ''", "Methods.Of").ConfigureAwait(false);
            await PressKeys(window, "ArrowUp").ConfigureAwait(false);
            await window.Until("document.querySelector('.navarone-page .inbox-row[aria-current=\"true\"] .inbox-title')?.textContent ?? ''", "DeterministicProblemClassifier.Classify").ConfigureAwait(false);
            await PressKeys(window, "ArrowDown").ConfigureAwait(false);

            await PressKeys(window, "Escape").ConfigureAwait(false);
            Assert.Equal("false", await window.Eval("String(document.activeElement.classList.contains('search'))").ConfigureAwait(false));
            Assert.Equal("e", await window.Eval("document.querySelector('.navarone-page input.search').value").ConfigureAwait(false));

            await window.Eval("document.querySelector('.navarone-page input.search').focus()").ConfigureAwait(false);
            await PressKeys(window, "Enter").ConfigureAwait(false);
            await window.Until("document.querySelector('.inbox-group-name')?.textContent ?? ''", "Methods.Of").ConfigureAwait(false);
            Assert.Equal("2", await window.Eval("String(document.querySelectorAll('.navarone-page .inbox-row').length)").ConfigureAwait(false));

            await PressKeys(window, "Escape").ConfigureAwait(false);
            await window.Until("String(document.querySelector('.inbox-group-name') === null)", "true").ConfigureAwait(false);
            Assert.Equal("e", await window.Eval("document.querySelector('.navarone-page input.search').value").ConfigureAwait(false));

            Assert.True(window.Exceptions.Count == 0, string.Join('\n', window.Exceptions));
        }).ConfigureAwait(false);
    }

    /// <summary>Each method row as its name and problem count, joined.</summary>
    private const string MethodRows = "[...document.querySelectorAll('.navarone-page .inbox-row')].map((row) => row.querySelector('.inbox-title').textContent + '|' + row.querySelector('.inbox-id').textContent).join(';')";

    /// <summary>Types into the search box as the browser does: sets its value and fires input.</summary>
    private static Task<string> Search(Headless window, string text) =>
        window.Eval($"(() => {{ const box = document.querySelector('.navarone-page input.search'); box.value = '{text}'; box.dispatchEvent(new Event('input', {{ bubbles: true }})); return box.value; }})()");

    /// <summary>A navarone line of sources.tsv with no escalation, one problem fixed in a worktree that exists, its code and a lens.</summary>
    private void WriteNavarone()
    {
        var fixtures = Path.Combine(AppContext.BaseDirectory, "Fixtures");
        var worktree = Directory.CreateDirectory(Path.Combine(brain.Root, "studio", "slice")).FullName;
        var problem = new Dictionary<string, object> { ["at"] = "2026-11-05T10:00:00Z", ["repo"] = worktree, ["worktree"] = worktree, ["branch"] = "slice", ["id"] = "0000007", ["check"] = "cyclomatic", ["symbol"] = "p::N.DeterministicProblemClassifier.Classify(ProblemCase)" };
        var raised = new Dictionary<string, object>(problem) { ["type"] = "raised" };
        var fixedOne = new Dictionary<string, object>(problem) { ["type"] = "fixed", ["before"] = Before, ["after"] = After };
        var events = Path.Combine(brain.Root, "events.json");
        var otherOne = new Dictionary<string, object>(raised) { ["id"] = "0000008", ["symbol"] = "p::N.Methods.Of(Block)" };
        var otherTwo = new Dictionary<string, object>(otherOne) { ["id"] = "0000009" };
        File.WriteAllText(events, JsonSerializer.Serialize(new { source = "navarone", events = new[] { otherOne, otherTwo, raised, fixedOne } }));
        var code = Path.Combine(brain.Root, "code.sh");
        File.WriteAllText(code, "#!/bin/sh\nprintf 'code of %s' \"$1\"\n");
        File.WriteAllText(
            Path.Combine(brain.Home, "sources.tsv"),
            $"navarone\techo '{{\"source\":\"navarone\",\"questions\":[]}}'\tbash '{fixtures}/context-source.sh'\tcat '{events}'\tsh '{code}'\n");
    }
}
