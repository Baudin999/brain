using System.Net;
using System.Text.Json;

namespace Brain.Tests;

public sealed partial class CodeServerTests
{
    /// <summary>
    /// The second flow in a real browser: calls in a row fold into one line that names what they did, a click on a call
    /// opens it in the information panel, a brain query lists its nodes there, and a kind turned off in the settings is
    /// no longer drawn.
    /// </summary>
    [WindowFact]
    public async Task TheWindowFoldsToolCallsOpensThemInThePanelAndHidesAKindTheSettingsTurnOff()
    {
        var id = Guid.NewGuid().ToString("D");
        WriteTranscript(
            "q",
            id,
            $$$"""{"type":"user","cwd":"{{{project}}}","message":{"role":"user","content":"look around"}}""",
            Use("r1", "Read", """{"file_path":"/x/a.cs"}"""),
            Result("r1", "     1\\tclass A {}"),
            Use("r2", "Read", """{"file_path":"/x/b.cs"}"""),
            Result("r2", "     1\\tclass B {}"),
            Use("c1", "Bash", """{"command":"ls /x","description":"List x"}"""),
            Result("c1", "a.cs\\nb.cs"),
            Said(1, "I looked."),
            Use("b1", "Bash", """{"command":"brain lookup stylebook"}"""),
            Result("b1", "project:stylebook  sure  The look lives in one stylebook\\nproject:other  likely  Another node"));
        var window = await Headless.Open(windows[0], Path.Combine(brain.Root, "chromium")).ConfigureAwait(false);
        try
        {
            await window.Until("document.querySelector('.code-new') !== null", "true").ConfigureAwait(false);
            await Send(HttpMethod.Post, "/api/key/start", Json(new { agent = "brain", cwd = project, resume = id }), key: Key()).ConfigureAwait(false);
            await window.Until("document.querySelector('.tool-run-words')?.textContent", "Read 2 files, ran 1 command").ConfigureAwait(false);
            await window.Until("document.querySelector('.tool-call[data-kind=\"brain\"] .tool-call-kind')?.textContent", "Brain query").ConfigureAwait(false);
            await window.Until("document.querySelector('.info-panel') === null", "true").ConfigureAwait(false);

            await window.Eval("document.querySelector('.tool-run-summary').click()").ConfigureAwait(false);
            await window.Eval("document.querySelectorAll('.tool-run .tool-call')[2].click()").ConfigureAwait(false);
            await window.Until("document.querySelector('.info-panel-title')?.textContent", "List x").ConfigureAwait(false);
            await window.Until("document.querySelector('.tool-detail[data-kind=\"command\"] .tool-detail-code')?.textContent", "$ ls /x").ConfigureAwait(false);

            await window.Eval("document.querySelector('.tool-call[data-kind=\"brain\"]').click()").ConfigureAwait(false);
            await window.Until("document.querySelectorAll('.tool-detail-item').length", "2").ConfigureAwait(false);
            await window.Until("document.querySelector('.tool-detail-ref')?.textContent", "project:stylebook").ConfigureAwait(false);

            await window.Eval("document.querySelector('.info-panel .icon').click()").ConfigureAwait(false);
            await window.Until("document.querySelector('.info-panel') === null", "true").ConfigureAwait(false);

            await window.Eval("document.querySelector('.icon[aria-label=\"Settings\"]').click()").ConfigureAwait(false);
            await window.Until("document.querySelectorAll('.toggle[aria-checked=\"true\"]').length", "2").ConfigureAwait(false);
            await window.Eval("document.querySelector('.toggle[aria-label=\"Tool calls\"]').click()").ConfigureAwait(false);
            await window.Until("document.querySelectorAll('.toggle[aria-checked=\"true\"]').length", "1").ConfigureAwait(false);
            await window.Eval("document.querySelector('.code-session').click()").ConfigureAwait(false);
            await window.Until("document.querySelectorAll('.tool-call').length", "1").ConfigureAwait(false);

            var saved = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(brain.Home, "cache", "code", "settings.json")).ConfigureAwait(false));
            Assert.False(saved.RootElement.GetProperty("tools").GetBoolean());
            Assert.True(saved.RootElement.GetProperty("brain").GetBoolean());
            Assert.True(window.Exceptions.Count == 0, string.Join('\n', window.Exceptions));
        }
        finally
        {
            await window.DisposeAsync().ConfigureAwait(false);
        }
    }

    [Fact]
    public async Task ASettingsBodyThatIsNoChoiceIsRefusedAndSavesNothing()
    {
        var token = await Trade(Code(windows[0]));
        var saved = await Send(HttpMethod.Post, "/api/settings", Json(new { tools = false, brain = true }), token: token, origin: server.Origin);

        var refused = await Send(HttpMethod.Post, "/api/settings", "not json", token: token, origin: server.Origin);
        var read = await Json(await Send(HttpMethod.Get, "/api/settings", token: token));

        Assert.Equal(HttpStatusCode.OK, saved.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, refused.StatusCode);
        Assert.False(read.RootElement.GetProperty("tools").GetBoolean());
    }

    private static string Use(string tool, string name, string input) =>
        $$$"""{"type":"assistant","message":{"content":[{"type":"tool_use","id":"{{{tool}}}","name":"{{{name}}}","input":{{{input}}}}]}}""";

    private static string Result(string tool, string text) =>
        $$$"""{"type":"user","message":{"role":"user","content":[{"type":"tool_result","tool_use_id":"{{{tool}}}","content":"{{{text}}}"}]}}""";
}
