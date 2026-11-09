using System.Net;
using System.Text.Json;
using Brain.Serving;

namespace Brain.Tests;

/// <summary>
/// The settings page's calls: it reads every setting, and each change it saves commits settings.json in the data repo.
/// A change needs a window's armed session, like an answer, and a refused one says why.
/// </summary>
public sealed partial class ReviewServerTests
{
    [Fact]
    public async Task TheSettingsPageReadsEverySettingAndSavesEachChange()
    {
        TempBrain.CommitAll(brain.Home);
        var token = await Trade(Launch());

        var stranger = await Send(HttpMethod.Get, "/api/settings");
        var read = await Send(HttpMethod.Get, "/api/settings", token: token);
        var early = await Send(HttpMethod.Post, "/api/settings", Json(new { key = "judge", value = new { model = "opus", effort = "high" } }), token: token, origin: server.Origin);
        time.Advance(WindowKeys.Arming);
        var foreign = await Send(HttpMethod.Post, "/api/settings", Json(new { key = "judge", value = new { model = "opus", effort = "high" } }), token: token, origin: "http://evil.example");
        var later = await Send(HttpMethod.Post, "/api/settings", Json(new { key = "judge", value = new { model = "gemma4", effort = "medium" } }), token: token, origin: server.Origin);
        var nameless = await Send(HttpMethod.Post, "/api/settings", Json(new { value = new { model = "opus", effort = "high" } }), token: token, origin: server.Origin);
        var saved = await Send(HttpMethod.Post, "/api/settings", Json(new { key = "judge", value = new { model = "opus", effort = "high" } }), token: token, origin: server.Origin);

        Assert.Equal(HttpStatusCode.Unauthorized, stranger.StatusCode);
        Assert.Equal("sonnet at medium", await Setting(read, "judge"));
        Assert.Equal((HttpStatusCode)425, early.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, foreign.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, later.StatusCode);
        Assert.Contains("gemma4\\u0022,\\u0022effort\\u0022:\\u0022medium\\u0022}, not a model of", await later.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        Assert.Equal(HttpStatusCode.BadRequest, nameless.StatusCode);
        Assert.Equal("opus at high", await Setting(saved, "judge"));
        Assert.Equal("Settings: judge is opus at high\n", TempBrain.Git(brain.Home, "log", "-1", "--format=%s"));
    }

    [Fact]
    public async Task ANewChatStartsOnTheChatSettingAndNotTheSittingOne()
    {
        await File.WriteAllTextAsync(Path.Combine(brain.Home, BrainSettings.FileName), """{"chat":{"model":"opus","effort":"xhigh"},"sitting":{"model":"haiku","effort":"low"}}""");
        var token = await Trade(Launch());
        time.Advance(WindowKeys.Arming);

        await StartChat(token, Json(new { tab = ":home", place = "Home" }));
        using var registry = await Chats(token);

        var chat = registry.RootElement.GetProperty("agents")[0];
        Assert.Equal(("opus", "xhigh"), (chat.GetProperty("model").GetString(), chat.GetProperty("effort").GetString()));
    }

    private static async Task<string?> Setting(HttpResponseMessage response, string key)
    {
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var settings = JsonDocument.Parse(await response.Content.ReadAsStringAsync().ConfigureAwait(false));
        var choice = settings.RootElement.GetProperty(key);
        return $"{choice.GetProperty("model").GetString()} at {choice.GetProperty("effort").GetString()}";
    }
}
