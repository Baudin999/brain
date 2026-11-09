using System.Net;
using System.Text.Json;
using Brain.Search;
using Brain.Serving;

namespace Brain.Tests;

/// <summary>
/// The switch of embedding models from the settings page: it says what it costs, downloads the model, embeds every node
/// again beside the vectors search reads, and only then names the new model in settings.json, moves search onto it and
/// drops the old model's vectors. A failed download or a model that does not answer changes nothing.
/// </summary>
public sealed partial class ReviewServerTests
{
    private const string EmbedderCall = "/api/embedder";

    /// <summary>The embedders the switch loaded, in order.</summary>
    private readonly List<FakeEmbedder> loaded = [];

    /// <summary>When set, every embedder the switch loads answers null.</summary>
    private bool down;

    private string ModelRoot => Path.Combine(brain.Root, "model");

    private string MirrorRoot => Path.Combine(brain.Root, "mirror");

    private string Mirror => new Uri(MirrorRoot).AbsoluteUri;

    private static EmbedderProfile Small => FakeProfile.Shipped[0];

    private static EmbedderProfile Large => FakeProfile.Shipped[1];

    [Fact]
    public async Task ASwitchDownloadsEmbedsEveryNodeAndOnlyThenMovesSearchAndBackAgain()
    {
        var token = await SearchingOnSmall();
        FakeProfile.Mirror(MirrorRoot, Large);

        var cost = await Parsed(await Send(HttpMethod.Get, $"{EmbedderCall}/cost?model={Large.Name}", token: token));
        var early = await Send(HttpMethod.Post, EmbedderCall, Json(new { model = Large.Name }), token: token, origin: server.Origin);
        time.Advance(WindowKeys.Arming);
        var started = await Parsed(await Send(HttpMethod.Post, EmbedderCall, Json(new { model = Large.Name }), token: token, origin: server.Origin));
        await models.Job.ConfigureAwait(true);
        var after = await Parsed(await Send(HttpMethod.Get, EmbedderCall, token: token));

        Assert.Equal((Large.Name, 2, 1), (cost.GetProperty("model").GetString(), cost.GetProperty("download").GetInt32(), cost.GetProperty("nodes").GetInt32()));
        Assert.Equal((HttpStatusCode)425, early.StatusCode);
        Assert.Equal((Small.Name, Large.Name), (started.GetProperty("active").GetString(), started.GetProperty("switch").GetProperty("to").GetString()));
        Assert.Equal(Large.Name, after.GetProperty("active").GetString());
        Assert.Equal(JsonValueKind.Null, after.GetProperty("switch").ValueKind);
        Assert.Equal(Large.Name, BrainSettings.Read(brain.Home).Embedder);
        Assert.Equal($"Settings: embedder is {Large.Name}\n", TempBrain.Git(brain.Home, "log", "-1", "--format=%s"));
        Assert.True(File.Exists(Path.Combine(ModelRoot, Large.Name, "weights.onnx")));
        Assert.Equal((0, 1), VectorsOf(Small, Large));

        await Send(HttpMethod.Get, "/api/search?q=meadow", token: token);
        Assert.Contains(loaded.Single().Calls, call => call.Contains(Large.Query("meadow")));

        var back = await Parsed(await Send(HttpMethod.Get, $"{EmbedderCall}/cost?model={Small.Name}", token: token));
        await Send(HttpMethod.Post, EmbedderCall, Json(new { model = Small.Name }), token: token, origin: server.Origin);
        await models.Job.ConfigureAwait(true);

        Assert.Equal(0, back.GetProperty("download").GetInt32());
        Assert.Equal(Small.Name, models.Embedder.Profile.Name);
        Assert.Equal(Small.Name, BrainSettings.Read(brain.Home).Embedder);
        Assert.Equal((1, 0), VectorsOf(Small, Large));
    }

    [Fact]
    public async Task AFailedDownloadLeavesSearchAndTheSettingsAsTheyWere()
    {
        var token = await SearchingOnSmall();
        time.Advance(WindowKeys.Arming);

        await Send(HttpMethod.Post, EmbedderCall, Json(new { model = Large.Name }), token: token, origin: server.Origin);
        await models.Job.ConfigureAwait(true);
        var after = await Parsed(await Send(HttpMethod.Get, EmbedderCall, token: token));

        var failed = after.GetProperty("switch");
        Assert.Equal(Small.Name, after.GetProperty("active").GetString());
        Assert.Equal(ModelSwitch.Failed, failed.GetProperty("phase").GetString());
        Assert.StartsWith("could not download", failed.GetProperty("error").GetString(), StringComparison.Ordinal);
        Assert.False(File.Exists(Path.Combine(brain.Home, BrainSettings.FileName)));
        Assert.False(Directory.Exists(Path.Combine(ModelRoot, Large.Name)));
        Assert.Equal((1, 0), VectorsOf(Small, Large));
    }

    [Fact]
    public async Task AModelThatDoesNotAnswerLeavesSearchOnTheOldOne()
    {
        var token = await SearchingOnSmall();
        FakeProfile.Mirror(MirrorRoot, Large);
        time.Advance(WindowKeys.Arming);
        down = true;

        await Send(HttpMethod.Post, EmbedderCall, Json(new { model = Large.Name }), token: token, origin: server.Origin);
        await models.Job.ConfigureAwait(true);
        var after = await Parsed(await Send(HttpMethod.Get, EmbedderCall, token: token));

        Assert.Equal(Small.Name, after.GetProperty("active").GetString());
        Assert.Contains("did not load or did not answer", after.GetProperty("switch").GetProperty("error").GetString(), StringComparison.Ordinal);
        Assert.Equal(Small.Name, BrainSettings.Read(brain.Home).Embedder);
        Assert.Equal((1, 0), VectorsOf(Small, Large));
    }

    [Fact]
    public async Task TheEmbeddingModelIsNoSettingThePageSavesAndASwitchToTheModelSearchRunsOnIsRefused()
    {
        var token = await SearchingOnSmall();
        time.Advance(WindowKeys.Arming);

        var saved = await Send(HttpMethod.Post, "/api/settings", Json(new { key = BrainSettings.EmbedderKey, value = Large.Name }), token: token, origin: server.Origin);
        var same = await Send(HttpMethod.Post, EmbedderCall, Json(new { model = Small.Name }), token: token, origin: server.Origin);
        var unknown = await Send(HttpMethod.Post, EmbedderCall, Json(new { model = "no-such-model" }), token: token, origin: server.Origin);

        Assert.Equal(HttpStatusCode.Conflict, saved.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, same.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, unknown.StatusCode);
        Assert.Equal(Small.Name, BrainSettings.Read(brain.Home).Embedder);
    }

    /// <summary>
    /// A window on a machine with only the small model's files, on a graph whose search ran once on the small model, so
    /// its vectors are in the index.
    /// </summary>
    private async Task<string> SearchingOnSmall()
    {
        var small = Path.Combine(ModelRoot, Small.Name);
        Directory.CreateDirectory(small);
        foreach (var file in Small.Files)
        {
            await File.WriteAllTextAsync(Path.Combine(small, file.Name), FakeProfile.Content(file)).ConfigureAwait(false);
        }

        brain.Embedder.Profile = Small;
        brain.Embedder.Meanings["meadow"] = 0;
        TempBrain.CommitAll(brain.Home);
        var token = await Trade(Launch()).ConfigureAwait(false);
        Assert.Equal(HttpStatusCode.OK, (await Send(HttpMethod.Get, "/api/search?q=meadow", token: token).ConfigureAwait(false)).StatusCode);
        Assert.Equal((1, 0), VectorsOf(Small, Large));
        return token;
    }

    private FakeEmbedder Embedder(EmbedderProfile profile)
    {
        var embedder = new FakeEmbedder { Profile = profile, Down = down };
        embedder.Meanings["meadow"] = 0;
        loaded.Add(embedder);
        return embedder;
    }

    /// <summary>How many vectors of each model the index holds.</summary>
    private (int, int) VectorsOf(EmbedderProfile one, EmbedderProfile other)
    {
        using var index = SearchIndex.Open(brain.IndexPath);
        return (index.Vectors(one.Name).Count, index.Vectors(other.Name).Count);
    }

    private static async Task<JsonElement> Parsed(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync().ConfigureAwait(false));
        return json.RootElement.Clone();
    }
}
