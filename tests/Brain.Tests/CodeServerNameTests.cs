using System.Net;
using System.Text.Json;

namespace Brain.Tests;

/// <summary>
/// A session's row is headed by its name, which <c>/rename</c> stores in the transcript, or else by the first words of
/// the user's first prompt; the user renames it with a name of letters and digits no other session carries.
/// </summary>
public sealed partial class CodeServerTests
{
    [Fact]
    public async Task ASessionIsHeadedByTheFirstWordsOfTheUsersPromptUntilTheUserNamesIt()
    {
        var token = await Trade(Code(windows[0]));
        var id = await StartSession();
        await Say(token, id, "hello");

        var unnamed = await ListedSession(token, id, (one) => one.TryGetProperty("title", out _));
        var renamed = await Send(HttpMethod.Post, "/api/rename", Json(new { session = id, name = "  Alpha1  " }), token: token, origin: server.Origin);
        var named = await ListedSession(token, id, (one) => one.TryGetProperty("name", out _));

        Assert.Equal("hello", unnamed.GetProperty("title").GetString());
        Assert.Equal("hello", unnamed.GetProperty("prompt").GetString());
        Assert.False(unnamed.TryGetProperty("name", out _));
        Assert.Equal(HttpStatusCode.NoContent, renamed.StatusCode);
        Assert.Contains("/rename Alpha1\"", await File.ReadAllTextAsync(Path.Combine(brain.Root, "claude.said")), StringComparison.Ordinal);
        Assert.Equal("Alpha1", named.GetProperty("name").GetString());
        Assert.Equal("Alpha1", named.GetProperty("title").GetString());
        Assert.Equal("hello", named.GetProperty("prompt").GetString());
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("two words")]
    [InlineData("dash-ed")]
    [InlineData("line\nbreak")]
    [InlineData("naïve")]
    [InlineData("0123456789012345678901234567890123456789012345678901234567890")]
    public async Task ANameThatBreaksTheRuleIsRefusedAndNothingIsSent(string name)
    {
        var token = await Trade(Code(windows[0]));
        var id = await StartSession();

        var refused = await Send(HttpMethod.Post, "/api/rename", Json(new { session = id, name }), token: token, origin: server.Origin);

        Assert.Equal(HttpStatusCode.Conflict, refused.StatusCode);
        Assert.False(File.Exists(Path.Combine(brain.Root, "claude.said")));
    }

    [Fact]
    public async Task ANameAnotherSessionCarriesIsRefusedWhateverTheCase()
    {
        var token = await Trade(Code(windows[0]));
        var first = await StartSession();
        var second = await StartSession();
        await Send(HttpMethod.Post, "/api/rename", Json(new { session = first, name = "Alpha" }), token: token, origin: server.Origin);
        await ListedSession(token, first, (one) => one.TryGetProperty("name", out _));

        var refused = await Send(HttpMethod.Post, "/api/rename", Json(new { session = second, name = "aLPHA" }), token: token, origin: server.Origin);
        var again = await Send(HttpMethod.Post, "/api/rename", Json(new { session = first, name = "alpha" }), token: token, origin: server.Origin);

        Assert.Equal(HttpStatusCode.Conflict, refused.StatusCode);
        Assert.Contains("another session is named", await refused.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        Assert.Equal(HttpStatusCode.NoContent, again.StatusCode);
    }

    /// <summary>The session <paramref name="id"/> as the window lists it, once <paramref name="until"/> holds of it; it fails after a wait.</summary>
    private async Task<JsonElement> ListedSession(string token, string id, Func<JsonElement, bool> until)
    {
        var deadline = DateTime.UtcNow + Wait;
        while (true)
        {
            var listed = await Json(await Send(HttpMethod.Get, "/api/sessions", token: token).ConfigureAwait(false)).ConfigureAwait(false);
            var one = listed.RootElement.GetProperty("sessions").EnumerateArray().Single((session) => session.GetProperty("id").GetString() == id).Clone();
            if (until(one) || DateTime.UtcNow > deadline)
            {
                Assert.True(until(one), "the session never listed as asked");
                return one;
            }

            await Task.Delay(50).ConfigureAwait(false);
        }
    }
}
