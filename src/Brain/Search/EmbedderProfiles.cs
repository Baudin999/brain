using Brain.Search.Profiles;

namespace Brain.Search;

/// <summary>
/// The embedding models brain ships, and the one it runs. The settings name the model brain runs, the default one when
/// they name none; Program.cs runs the embedder <see cref="Running"/> gives, and search reads the profile from that
/// embedder. The settings page offers <see cref="All"/>.
/// </summary>
internal static class EmbedderProfiles
{
    public static IReadOnlyList<EmbedderProfile> All { get; } = [BgeSmall.Profile, ArcticM.Profile];

    /// <summary>The model a fresh install runs, and one whose settings name none.</summary>
    public static EmbedderProfile Default => BgeSmall.Profile;

    /// <summary>The model the settings of the data repo at <paramref name="home"/> name; refused when they cannot be read.</summary>
    public static EmbedderProfile Active(string home) => Named(All, BrainSettings.Read(home).Embedder);

    /// <summary>
    /// The embedder of the model the settings at <paramref name="home"/> name, its files under <paramref name="modelRoot"/>.
    /// When the settings cannot be read, or name no model, the process runs without one: search ranks by keywords and
    /// says why, and no default model embeds in its place, since the index may hold only another model's vectors.
    /// </summary>
    public static IEmbedder Running(string home, string modelRoot)
    {
        try
        {
            return new OnnxEmbedder(modelRoot, Active(home));
        }
        catch (BrainException refusal)
        {
            return new NoEmbedder(refusal.Message);
        }
    }

    /// <summary>The profile of <paramref name="profiles"/> under <paramref name="name"/>.</summary>
    public static EmbedderProfile Named(IReadOnlyList<EmbedderProfile> profiles, string name) =>
        profiles.FirstOrDefault(profile => profile.Name == name) ?? throw new BrainException($"brain ships no embedding model {name}");
}
