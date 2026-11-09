namespace Brain.Tests;

/// <summary>
/// The focus picker and Home's pending bar in a real headless Chromium. The window opens on Home when no tab is
/// remembered, and remembers the tab the user opened; a write arriving does not change the tab. The picker has a row for
/// all, personal and global, each with the path of its graph and what waits for it, and Home's bar lists only the
/// projects with something waiting. It needs a Chromium-family browser and is skipped, which a run shows, without one.
/// </summary>
public sealed partial class ReviewServerTests
{
    [WindowFact]
    public async Task TheWindowOpensOnHomeAndRemembersTheTabTheUserOpened()
    {
        await InWindow(async window =>
        {
            Assert.Equal(":home", await window.Eval("state.inbox").ConfigureAwait(false));

            var id = Stage();
            await window.Until("state.items.map((item) => item.id).join()", id).ConfigureAwait(false);
            Assert.Equal(":home", await window.Eval("state.inbox").ConfigureAwait(false));
            Assert.Equal("null", await window.Eval("String(localStorage.getItem('brain.tab'))").ConfigureAwait(false));

            await window.Eval("showInbox(NODE_REVIEW)").ConfigureAwait(false);
            Assert.Equal("node review", await window.Eval("localStorage.getItem('brain.tab')").ConfigureAwait(false));
            Assert.Equal("node review", await window.Eval("storedTab()").ConfigureAwait(false));
        }).ConfigureAwait(false);
    }

    [WindowFact]
    public async Task ThePickerGivesEachGraphItsPathAndWhatWaitsForIt()
    {
        await InWindow(async window =>
        {
            var id = Stage();
            await window.Until("state.items.map((item) => item.id).join()", id).ConfigureAwait(false);
            await window.Until("String(state.projects.length)", "2").ConfigureAwait(false);

            var rows = await window.Eval("focusChoices().map((choice) => `${choice.name} ${(choice.path ?? 'none').split('/').slice(-2).join('/')} ${choice.reviews} ${choice.disputes}`).join('|')").ConfigureAwait(false);

            Assert.Equal("all none 1 0|personal graph/personal 0 0|global graph/global 1 0", rows);
        }).ConfigureAwait(false);
    }

    [WindowFact]
    public async Task HomesPendingBarListsOnlyTheGraphsWithSomethingWaiting()
    {
        await InWindow(async window =>
        {
            Assert.Equal("0", await window.Eval("String(document.querySelectorAll('.pending-bar-item').length)").ConfigureAwait(false));

            var id = Stage();
            await window.Until("state.items.map((item) => item.id).join()", id).ConfigureAwait(false);
            await window.Until("String(document.querySelectorAll('.pending-bar-item').length)", "1").ConfigureAwait(false);

            Assert.Contains("global", await window.Eval("document.querySelector('.pending-bar-item').innerText").ConfigureAwait(false), StringComparison.Ordinal);
            await window.Eval("document.querySelector('.pending-bar-item').click()").ConfigureAwait(false);
            Assert.Equal("global", await window.Eval("focus()").ConfigureAwait(false));
        }).ConfigureAwait(false);
    }

    [WindowFact]
    public async Task ThePickerScrollsToTheCursorAndARedrawLeavesItWhereItIs()
    {
        var rows = string.Join('\n', Enumerable.Range(0, 30).Select(at => $"project{at:00}\t~/project{at:00}\t\t2026-10-03"));
        await File.WriteAllTextAsync(Path.Combine(brain.Home, ProjectRegistry.FileName), $"{ProjectRegistry.Header}\n{rows}\n").ConfigureAwait(false);

        await InWindow(async window =>
        {
            await window.Until("String(state.projects.length)", "32").ConfigureAwait(false);
            await window.Eval("openPalette('projects')").ConfigureAwait(false);

            await window.Eval("onPaletteKey({ key: 'ArrowUp', preventDefault() {} })").ConfigureAwait(false);

            var list = "$('palette-list')";
            Assert.Equal("true", await window.Eval($"String({list}.scrollHeight > {list}.clientHeight)").ConfigureAwait(false));
            var inside = $"(() => {{ const row = {list}.querySelector('[aria-selected=\"true\"]').getBoundingClientRect(); const box = {list}.getBoundingClientRect(); return String(row.top >= box.top && row.bottom <= box.bottom); }})()";
            Assert.Equal("true", await window.Eval(inside).ConfigureAwait(false));
            var before = await window.Eval($"String({list}.scrollTop)").ConfigureAwait(false);
            Assert.NotEqual("0", before);

            await window.Eval("renderPalette()").ConfigureAwait(false);

            Assert.Equal(before, await window.Eval($"String({list}.scrollTop)").ConfigureAwait(false));
        }).ConfigureAwait(false);
    }
}
