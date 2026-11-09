using System.Net;
using System.Text.Json;
using Brain.Children;
using Brain.Code;

namespace Brain.Tests;

/// <summary>
/// Images in brain code: the user's words carry the images he attached to claude as image content blocks, and a
/// transcript's images come back as data URLs on the user's words and on a tool's result. An image of another type,
/// not in base64 or too large is refused before claude hears of it. The file explorer reads the disk through the server,
/// which lists a folder's folders and images only, reads an image file and nothing else, and remembers the folders
/// images were attached from, the newest first, in the data repo's cache; it opens in the last of them, or in
/// ~/Pictures the first time.
/// </summary>
public sealed partial class CodeServerTests
{
    /// <summary>A PNG of one pixel, in base64.</summary>
    private const string Pixel = "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNk+M9QDwADhgGAWjR9awAAAABJRU5ErkJggg==";

    [Fact]
    public void TheUsersWordsCarryHisImagesAsImageBlocksAheadOfTheText()
    {
        using var message = JsonDocument.Parse(ClaudeStream.UserMessage("look", [new ImageBlock("image/png", Pixel)]));
        using var words = JsonDocument.Parse(ClaudeStream.UserMessage("look"));

        var content = message.RootElement.GetProperty("message").GetProperty("content");
        Assert.Equal(["image", "text"], content.EnumerateArray().Select((block) => block.GetProperty("type").GetString()));
        var source = content[0].GetProperty("source");
        Assert.Equal(("base64", "image/png", Pixel), (source.GetProperty("type").GetString(), source.GetProperty("media_type").GetString(), source.GetProperty("data").GetString()));
        Assert.Equal("look", content[1].GetProperty("text").GetString());
        Assert.Equal("look", words.RootElement.GetProperty("message").GetProperty("content").GetString());
    }

    [Fact]
    public void ATranscriptsImagesComeWithTheUsersWordsAndWithAResult()
    {
        var image = $$$"""{"type":"image","source":{"type":"base64","media_type":"image/png","data":"{{{Pixel}}}"}}""";
        var said = $$$"""{"type":"user","message":{"role":"user","content":[{{{image}}},{"type":"text","text":"look"}]}}""";
        var alone = $$$"""{"type":"user","message":{"role":"user","content":[{{{image}}}]}}""";
        var result = $$$"""{"type":"user","message":{"role":"user","content":[{"type":"tool_result","tool_use_id":"t1","content":[{"type":"text","text":"read"},{{{image}}}]}]}}""";

        var words = Transcript.Events(said).Single();
        var only = Transcript.Events(alone).Single();
        var gave = Transcript.Events(result).Single();

        var url = "data:image/png;base64," + Pixel;
        Assert.Equal((CodeEvent.User, "look", url), (words.Kind, words.Text, words.Images!.Single()));
        Assert.Equal((CodeEvent.User, string.Empty, url), (only.Kind, only.Text, only.Images!.Single()));
        Assert.Equal((CodeEvent.Result, "read", url), (gave.Kind, gave.Text, gave.Images!.Single()));
        Assert.Null(Transcript.Events("""{"type":"user","message":{"role":"user","content":"plain"}}""").Single().Images);
    }

    /// <summary>The images the user attached reach claude with his words, and his conversation shows them, from the transcript.</summary>
    [Fact]
    public async Task ImagesSaidReachClaudeAndComeBackInTheConversation()
    {
        var token = await Trade(Code(windows[0]));
        var id = await StartSession();

        var said = await Send(HttpMethod.Post, "/api/say", Json(new { session = id, words = string.Empty, images = new[] { new { name = "a.png", mediaType = "image/png", data = Pixel } } }), token: token, origin: server.Origin);
        using var conversation = await ConversationWhen(token, id, (read) => read.GetProperty("events").GetArrayLength() == 2);

        Assert.Equal(HttpStatusCode.NoContent, said.StatusCode);
        using var sent = JsonDocument.Parse((await File.ReadAllLinesAsync(Path.Combine(brain.Root, "claude.said")))[^1]);
        var block = sent.RootElement.GetProperty("message").GetProperty("content").EnumerateArray().Single();
        Assert.Equal(("image", Pixel), (block.GetProperty("type").GetString(), block.GetProperty("source").GetProperty("data").GetString()));
        var user = conversation.RootElement.GetProperty("events")[0];
        Assert.Equal("user", user.GetProperty("kind").GetString());
        Assert.Equal("data:image/png;base64," + Pixel, user.GetProperty("images")[0].GetString());
    }

    [Theory]
    [InlineData("image/tiff", Pixel, "is no png, jpeg, gif or webp image")]
    [InlineData("image/png", "not base64!", "came without its bytes in base64")]
    [InlineData("image/png", "", "is empty")]
    [InlineData("image/png", "dGV4dCwgbm8gaW1hZ2U=", "is no image/png image")]
    [InlineData("image/webp", "UklGRgAAAABBVklG", "is no image/webp image")]
    public async Task AnImageClaudeWouldNotTakeIsRefusedBeforeItIsSent(string type, string data, string why)
    {
        var token = await Trade(Code(windows[0]));
        var id = await StartSession();

        var said = await Send(HttpMethod.Post, "/api/say", Json(new { session = id, words = "look", images = new[] { new { name = "a.png", mediaType = type, data } } }), token: token, origin: server.Origin);

        Assert.Equal(HttpStatusCode.Conflict, said.StatusCode);
        Assert.Contains("a.png " + why, await said.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        Assert.False(File.Exists(Path.Combine(brain.Root, "claude.said")));
    }

    [Fact]
    public async Task AnImageLargerThanClaudeTakesIsRefused()
    {
        var token = await Trade(Code(windows[0]));
        var id = await StartSession();
        var large = Convert.ToBase64String(new byte[AttachedImages.Largest + 1]);

        var said = await Send(HttpMethod.Post, "/api/say", Json(new { session = id, words = "look", images = new[] { new { name = "big.png", mediaType = "image/png", data = large } } }), token: token, origin: server.Origin);

        Assert.Equal(HttpStatusCode.Conflict, said.StatusCode);
        Assert.Contains("big.png is 5.0 MB, and an image goes up to 5 MB", await said.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }

    /// <summary>A folder lists its folders and images, sorted, and nothing else: no other file and nothing a dot hides.</summary>
    [Fact]
    public async Task TheExplorerListsAFoldersFoldersAndImagesOnly()
    {
        var token = await Trade(Code(windows[0]));
        var folder = Pictures();
        Directory.CreateDirectory(Path.Combine(folder, "shots"));
        Directory.CreateDirectory(Path.Combine(folder, ".hidden"));
        await File.WriteAllTextAsync(Path.Combine(folder, "notes.txt"), "no image");
        await File.WriteAllTextAsync(Path.Combine(folder, ".dot.png"), "hidden");
        await File.WriteAllBytesAsync(Path.Combine(folder, "b.JPG"), [1]);
        await File.WriteAllBytesAsync(Path.Combine(folder, "a.png"), Convert.FromBase64String(Pixel));

        using var listed = await Json(await Send(HttpMethod.Get, "/api/folder?path=" + Uri.EscapeDataString(folder), token: token));
        var root = listed.RootElement;

        Assert.Equal(folder, root.GetProperty("path").GetString());
        Assert.Equal(brain.Root, root.GetProperty("parent").GetString());
        Assert.Equal(["shots"], root.GetProperty("folders").EnumerateArray().Select((one) => one.GetString()));
        Assert.Equal(["a.png", "b.JPG"], root.GetProperty("images").EnumerateArray().Select((one) => one.GetString()));
    }

    /// <summary>An image file is read in base64 with its media type; any other file, a relative path and a missing folder are refused.</summary>
    [Fact]
    public async Task TheExplorerReadsAnImageFileAndNothingElse()
    {
        var token = await Trade(Code(windows[0]));
        var folder = Pictures();
        await File.WriteAllBytesAsync(Path.Combine(folder, "a.png"), Convert.FromBase64String(Pixel));
        await File.WriteAllTextAsync(Path.Combine(folder, "secret.txt"), "the secret words");

        using var image = await Json(await Send(HttpMethod.Get, "/api/image?path=" + Uri.EscapeDataString(Path.Combine(folder, "a.png")), token: token));
        var text = await Send(HttpMethod.Get, "/api/image?path=" + Uri.EscapeDataString(Path.Combine(folder, "secret.txt")), token: token);
        var relative = await Send(HttpMethod.Get, "/api/folder?path=Pictures", token: token);
        var missing = await Send(HttpMethod.Get, "/api/folder?path=" + Uri.EscapeDataString(Path.Combine(folder, "none")), token: token);

        Assert.Equal(("a.png", "image/png", Pixel), (image.RootElement.GetProperty("name").GetString(), image.RootElement.GetProperty("mediaType").GetString(), image.RootElement.GetProperty("data").GetString()));
        Assert.Equal(HttpStatusCode.Conflict, text.StatusCode);
        Assert.DoesNotContain("secret words", await text.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        Assert.Equal(HttpStatusCode.Conflict, relative.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, missing.StatusCode);
    }

    /// <summary>
    /// The explorer reads nothing that is no image, whatever its name says: a text file named as a PNG, a link named as a
    /// PNG to a text file, a FIFO, which would block the read, and a file past the most it reads are refused, and none
    /// of their bytes go out. A link to an image reads as the image.
    /// </summary>
    [Fact]
    public async Task TheExplorerRefusesWhatOnlyItsNameCallsAnImage()
    {
        var token = await Trade(Code(windows[0]));
        var folder = Pictures();
        var secret = Path.Combine(brain.Root, "id_ed25519");
        await File.WriteAllTextAsync(secret, "the secret words");
        await File.WriteAllBytesAsync(Path.Combine(folder, "a.png"), Convert.FromBase64String(Pixel));
        await File.WriteAllTextAsync(Path.Combine(folder, "renamed.png"), "the secret words");
        File.CreateSymbolicLink(Path.Combine(folder, "key.png"), secret);
        File.CreateSymbolicLink(Path.Combine(folder, "linked.png"), Path.Combine(folder, "a.png"));
        using (var large = File.Create(Path.Combine(folder, "large.png")))
        {
            large.SetLength(ImageFolders.Largest + 1L);
        }

        using var fifo = System.Diagnostics.Process.Start("mkfifo", Path.Combine(folder, "pipe.png"));
        await fifo.WaitForExitAsync();

        using var linked = await Json(await Send(HttpMethod.Get, "/api/image?path=" + Uri.EscapeDataString(Path.Combine(folder, "linked.png")), token: token));
        Assert.Equal(Pixel, linked.RootElement.GetProperty("data").GetString());
        foreach (var name in new[] { "renamed.png", "key.png", "pipe.png", "large.png" })
        {
            var refused = await Send(HttpMethod.Get, "/api/image?path=" + Uri.EscapeDataString(Path.Combine(folder, name)), token: token);
            var said = await refused.Content.ReadAsStringAsync();
            Assert.Equal(HttpStatusCode.Conflict, refused.StatusCode);
            Assert.DoesNotContain("secret words", said, StringComparison.Ordinal);
            Assert.DoesNotContain("dGhlIHNlY3JldCB3b3Jkcw", said, StringComparison.Ordinal);
        }

        var past = await Send(HttpMethod.Get, "/api/image?path=" + Uri.EscapeDataString(Path.Combine(folder, "large.png")), token: token);
        Assert.Contains("larger than the 32 MB the explorer reads", await past.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }

    /// <summary>
    /// The explorer opens in ~/Pictures the first time; once images were attached from a folder it opens there, and the
    /// folders attached from are offered the newest first, the last five, kept in the data repo's cache.
    /// </summary>
    [Fact]
    public async Task TheExplorerOpensInTheFolderLastAttachedFrom()
    {
        var token = await Trade(Code(windows[0]));
        var pictures = Pictures();
        var folders = Enumerable.Range(1, ImageFolders.Remembered + 1).Select((at) => Directory.CreateDirectory(Path.Combine(pictures, "f" + at)).FullName).ToList();

        using var first = await Json(await Send(HttpMethod.Get, "/api/places", token: token));
        foreach (var folder in folders)
        {
            var attached = await Send(HttpMethod.Post, "/api/attached-from", Json(new { folder }), token: token, origin: server.Origin);
            Assert.Equal(HttpStatusCode.NoContent, attached.StatusCode);
        }

        var again = await Send(HttpMethod.Post, "/api/attached-from", Json(new { folder = folders[3] }), token: token, origin: server.Origin);
        var gone = await Send(HttpMethod.Post, "/api/attached-from", Json(new { folder = Path.Combine(pictures, "none") }), token: token, origin: server.Origin);
        using var places = await Json(await Send(HttpMethod.Get, "/api/places", token: token));

        Assert.Equal(pictures, first.RootElement.GetProperty("start").GetString());
        Assert.Equal(pictures, first.RootElement.GetProperty("pictures").GetString());
        Assert.Empty(first.RootElement.GetProperty("recent").EnumerateArray());
        Assert.Equal(HttpStatusCode.NoContent, again.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, gone.StatusCode);
        Assert.Equal(folders[3], places.RootElement.GetProperty("start").GetString());
        Assert.Equal([folders[3], folders[5], folders[4], folders[2], folders[1]], places.RootElement.GetProperty("recent").EnumerateArray().Select((one) => one.GetString()));
        Assert.Contains(folders[3], await File.ReadAllTextAsync(Path.Combine(brain.Home, "cache", CodeServer.Folder, "attached-from")), StringComparison.Ordinal);
    }

    /// <summary>
    /// The window in a real browser: an image pasted and one dropped wait in the composer as thumbnails, one is removed;
    /// Alt+P opens the explorer in ~/Pictures, Space marks an image and Enter attaches it; the words go with the two
    /// images left, which the conversation then shows, and a click on one opens it in the image viewer.
    /// </summary>
    [WindowFact]
    public async Task TheWindowAttachesImagesThreeWaysAndShowsThemInTheConversation()
    {
        await File.WriteAllBytesAsync(Path.Combine(Pictures(), "shot.png"), Convert.FromBase64String(Pixel)).ConfigureAwait(false);
        var window = await Headless.Open(windows[0], Path.Combine(brain.Root, "chromium")).ConfigureAwait(false);
        try
        {
            await window.Until("document.querySelector('.code-new') !== null", "true").ConfigureAwait(false);
            await StartSession().ConfigureAwait(false);
            await window.Until("document.querySelector('.code-session')?.getAttribute('aria-current')", "true").ConfigureAwait(false);

            const string file = "(name) => { const bytes = Uint8Array.from(atob('" + Pixel + "'), (c) => c.charCodeAt(0)); const files = new DataTransfer(); files.items.add(new File([bytes], name, { type: 'image/png' })); return files; }";
            await window.Eval($"document.querySelector('.code-words').dispatchEvent(new ClipboardEvent('paste', {{ clipboardData: ({file})(''), bubbles: true, cancelable: true }}))").ConfigureAwait(false);
            await window.Eval($"document.querySelector('.code-scroll').dispatchEvent(new DragEvent('drop', {{ dataTransfer: ({file})('dropped.png'), bubbles: true, cancelable: true }}))").ConfigureAwait(false);
            await window.Until("String(document.querySelectorAll('.code-attached .image-thumb').length)", "2").ConfigureAwait(false);
            await window.Eval("document.querySelector('.code-attached .image-thumb-remove[aria-label=\"Remove dropped.png\"]').click()").ConfigureAwait(false);
            await window.Until("String(document.querySelectorAll('.code-attached .image-thumb').length)", "1").ConfigureAwait(false);

            await window.Eval("document.dispatchEvent(new KeyboardEvent('keydown', { key: 'π', code: 'KeyP', altKey: true, bubbles: true }))").ConfigureAwait(false);
            await window.Until("document.querySelector('.file-explorer-path')?.textContent", Pictures()).ConfigureAwait(false);
            await window.Until("document.querySelector('.file-explorer-tile[data-kind=\"image\"] .file-explorer-image') !== null", "true").ConfigureAwait(false);
            await window.Press(" ", "Space", " ").ConfigureAwait(false);
            await window.Until("document.querySelector('.file-explorer-tile[data-kind=\"image\"]').getAttribute('aria-pressed')", "true").ConfigureAwait(false);
            await window.Press("Enter", "Enter").ConfigureAwait(false);
            await window.Until("document.querySelector('.file-explorer') === null", "true").ConfigureAwait(false);
            await window.Until("String(document.querySelectorAll('.code-attached .image-thumb').length)", "2").ConfigureAwait(false);
            Assert.Equal(string.Empty, await window.Eval("document.querySelector('.code-words').value").ConfigureAwait(false));

            await window.Eval("const box = document.querySelector('.code-words'); box.value = 'look'; box.dispatchEvent(new Event('input')); box.dispatchEvent(new KeyboardEvent('keydown', { key: 'Enter', bubbles: true }))").ConfigureAwait(false);
            await window.Until("String(document.querySelectorAll('.code-user .image-thumb').length)", "2").ConfigureAwait(false);
            await window.Until("String(document.querySelectorAll('.code-attached .image-thumb').length)", "0").ConfigureAwait(false);
            await window.Eval("document.querySelector('.code-user .image-thumb-open').click()").ConfigureAwait(false);
            await window.Until("String(document.querySelectorAll('.image-viewer img').length)", "1").ConfigureAwait(false);

            Assert.Contains(Pictures(), await File.ReadAllTextAsync(Path.Combine(brain.Home, "cache", CodeServer.Folder, "attached-from")).ConfigureAwait(false), StringComparison.Ordinal);
            Assert.True(window.Exceptions.Count == 0, string.Join('\n', window.Exceptions));
        }
        finally
        {
            await window.DisposeAsync().ConfigureAwait(false);
        }
    }

    /// <summary>
    /// The window in a real browser: an image a tool's result carries, as a Read of an image file gives back, is no part
    /// of the conversation's flow; a click on the call shows it as a thumbnail in the information panel, and a click on
    /// that opens it in the image viewer.
    /// </summary>
    [WindowFact]
    public async Task TheWindowShowsAResultsImageInTheInformationPanel()
    {
        var id = Guid.NewGuid().ToString("D");
        var image = $$$"""{"type":"image","source":{"type":"base64","media_type":"image/png","data":"{{{Pixel}}}"}}""";
        WriteTranscript(
            "q",
            id,
            $$$"""{"type":"user","cwd":"{{{project}}}","message":{"role":"user","content":"look at it"}}""",
            Use("r1", "Read", """{"file_path":"/x/shot.png"}"""),
            $$$"""{"type":"user","message":{"role":"user","content":[{"type":"tool_result","tool_use_id":"r1","content":[{{{image}}}]}]}}""");
        var window = await Headless.Open(windows[0], Path.Combine(brain.Root, "chromium")).ConfigureAwait(false);
        try
        {
            await window.Until("document.querySelector('.code-new') !== null", "true").ConfigureAwait(false);
            await Send(HttpMethod.Post, "/api/key/start", Json(new { agent = "brain", cwd = project, resume = id }), key: Key()).ConfigureAwait(false);
            await window.Until("document.querySelector('.tool-call') !== null", "true").ConfigureAwait(false);
            Assert.Equal("0", await window.Eval("String(document.querySelectorAll('.code-events .image-thumb').length)").ConfigureAwait(false));

            await window.Eval("document.querySelector('.tool-call').click()").ConfigureAwait(false);
            await window.Until("String(document.querySelectorAll('.info-panel .tool-detail-images .image-thumb').length)", "1").ConfigureAwait(false);
            Assert.Equal("0", await window.Eval("String(document.querySelectorAll('.info-panel .tool-detail-waiting').length)").ConfigureAwait(false));
            await window.Eval("document.querySelector('.info-panel .image-thumb-open').click()").ConfigureAwait(false);
            await window.Until("document.querySelector('.image-viewer img')?.getAttribute('src')", "data:image/png;base64," + Pixel).ConfigureAwait(false);

            Assert.True(window.Exceptions.Count == 0, string.Join('\n', window.Exceptions));
        }
        finally
        {
            await window.DisposeAsync().ConfigureAwait(false);
        }
    }

    /// <summary>The ~/Pictures of the user's home the server was given, made.</summary>
    private string Pictures() => Directory.CreateDirectory(Path.Combine(brain.Root, "Pictures")).FullName;
}
