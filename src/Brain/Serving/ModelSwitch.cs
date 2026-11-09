using System.Diagnostics.CodeAnalysis;
using Brain.Installing;
using Brain.Search;
using Brain.Usage;
using Microsoft.Data.Sqlite;

namespace Brain.Serving;

/// <summary>
/// Switches the embedding model search runs on, from the settings page, inside brain serve: it downloads the model when
/// its files are not on disk, then embeds every node of the sky again with it, beside the vectors search reads. Search
/// stays on the model it runs until every node has a vector of the new one; only then the setting names the new
/// model, search runs on it and the old model's vectors go. A failure, or brain serve stopping, leaves the model search
/// runs on as it was. The vectors a switch wrote before it stopped stay in the index, and the next switch to that
/// model starts from them. The embedder runs at its own thread limit, so a switch loads the machine as a lookup does.
/// </summary>
internal sealed class ModelSwitch(BrainEnv env, string modelRoot, string? mirror, IReadOnlyList<EmbedderProfile> profiles, Func<EmbedderProfile, IEmbedder> embedderOf)
    : IDisposable
{
    public const string Downloading = "download";
    public const string Embedding = "embed";
    public const string Failed = "failed";

    /// <summary>A model's size counts megabytes of a million bytes.</summary>
    private const long Megabyte = 1_000_000;

    private readonly Lock gate = new();
    private readonly List<IEmbedder> made = [];
    private IEmbedder current = env.Embedder;
    private SwitchView? state;

    /// <summary>The embedder search runs on now.</summary>
    public IEmbedder Embedder
    {
        get
        {
            lock (gate)
            {
                return current;
            }
        }
    }

    /// <summary>The switch that runs, or the last one when it ran into a failure; done when none runs.</summary>
    public Task Job { get; private set; } = Task.CompletedTask;

    /// <summary>The model search runs on, every model brain ships, and the switch that runs or failed last, if any.</summary>
    public EmbedderView View()
    {
        lock (gate)
        {
            return new EmbedderView(current.Profile.Name, [.. profiles.Select(profile => new EmbedderOffer(profile.Name, profile.Megabytes))], state);
        }
    }

    /// <summary>What a switch to <paramref name="name"/> costs: the megabytes it downloads, none when its files are on disk, and the nodes it embeds.</summary>
    public EmbedderCost Cost(string name)
    {
        var profile = EmbedderProfiles.Named(profiles, name);
        var download = EmbeddingModel.In(profile, profile.Directory(modelRoot)) ? 0 : profile.Megabytes;
        return new EmbedderCost(profile.Name, download, Sky.Load(env).Nodes.Count);
    }

    /// <summary>Starts the switch to <paramref name="name"/> and returns the view with it running; refused while one runs or when search runs on it.</summary>
    public EmbedderView Start(string name)
    {
        var profile = EmbedderProfiles.Named(profiles, name);
        lock (gate)
        {
            BrainException.ThrowIf(state is { Phase: not Failed } ? $"the switch to {state.To} still runs" : null);
            BrainException.ThrowIf(profile.Name == current.Profile.Name ? $"search runs on {profile.Name} already" : null);
            state = new SwitchView(profile.Name, Embedding, 0, 0, null);
            Job = Task.Run(() => Run(profile));
        }

        return View();
    }

    /// <summary>Disposes the models this switch loaded, unless a switch still runs on one: the process ends with it.</summary>
    public void Dispose()
    {
        if (!Job.IsCompleted)
        {
            return;
        }

        foreach (var embedder in made)
        {
            (embedder as IDisposable)?.Dispose();
        }
    }

    [SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "The switch's one boundary: every failure is shown on the settings page and recorded, never dropped with the task.")]
    private void Run(EmbedderProfile to)
    {
        try
        {
            Download(to);
            var embedder = Load(to);
            var sky = Sky.Load(env);
            using var index = SearchIndex.Open(SearchIndex.PathIn(env.Home));
            var vectors = IndexRefresh.Vectors(index, embedder, sky.Scopes, sky.Nodes, (done, total) => Report(Embedding, done, total));
            BrainException.ThrowIf(vectors is null ? $"the embedding model {to.Name} did not load or did not answer, so search stays on {Embedder.Profile.Name}" : null);
            BrainSettings.Change(env.Home, BrainSettings.EmbedderKey, to.Name);
            lock (gate)
            {
                current = embedder;
                state = null;
            }

            index.KeepVectorsOf(to.Name);
        }
        catch (BrainException refusal)
        {
            Fail(refusal.Message);
        }
        catch (SqliteException error)
        {
            Fail($"the search index failed: {error.Message}");
        }
        catch (Exception error)
        {
            Fail(Failures.Record(env.Home, $"brain serve: the switch to {to.Name}", error, session: null, env.Err));
        }
    }

    /// <summary>Downloads the model's files when they are not on disk, each checked against its pinned SHA-256.</summary>
    private void Download(EmbedderProfile to)
    {
        var directory = to.Directory(modelRoot);
        if (EmbeddingModel.In(to, directory))
        {
            return;
        }

        var total = to.Megabytes * Megabyte;
        Report(Downloading, 0, total);
        BrainException.ThrowIf(EmbeddingModel.Download(to, mirror, directory, bytes => Report(Downloading, Math.Min(bytes, total), total)));
    }

    /// <summary>A new embedder of the model, so one that did not load before, while its files were missing, loads now.</summary>
    private IEmbedder Load(EmbedderProfile to)
    {
        var embedder = embedderOf(to);
        lock (gate)
        {
            made.Add(embedder);
        }

        return embedder;
    }

    private void Report(string phase, long done, long total)
    {
        lock (gate)
        {
            state = state! with { Phase = phase, Done = done, Total = total };
        }
    }

    private void Fail(string error)
    {
        lock (gate)
        {
            state = state! with { Phase = Failed, Error = error };
        }
    }
}

/// <summary>The model search runs on, the models brain ships, and the switch that runs or failed last, if any.</summary>
internal sealed record EmbedderView(string Active, IReadOnlyList<EmbedderOffer> Models, SwitchView? Switch);

/// <summary>A model brain ships, by its name, and how many megabytes its files take.</summary>
internal sealed record EmbedderOffer(string Name, int Megabytes);

/// <summary>What a switch to a model costs: the megabytes it downloads, 0 when its files are on disk, and how many nodes it embeds again.</summary>
internal sealed record EmbedderCost(string Model, int Download, int Nodes);

/// <summary>A switch of models: where it goes, its phase, download, embed or failed, how far it is of how far it goes, and why it failed.</summary>
internal sealed record SwitchView(string To, string Phase, long Done, long Total, string? Error);

/// <summary>The window's ask to switch to the model it names.</summary>
internal sealed record SwitchRequest(string? Model);
