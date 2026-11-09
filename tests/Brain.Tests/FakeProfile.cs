using System.Security.Cryptography;
using System.Text;
using Brain.Search;

namespace Brain.Tests;

/// <summary>
/// The profile of a model that does not exist, on a scale unlike any real model's, so a test of search or install
/// proves it reads the profile it is given and no real model's numbers or files.
/// </summary>
internal static class FakeProfile
{
    private const string Url = "https://models.invalid/";
    private const string Fake = "fake";
    private const string Other = "other";

    public static readonly EmbedderProfile Profile = new(
        "fake-model",
        File(Fake, "weights.onnx"),
        File(Fake, "tokens.txt"),
        Megabytes: 1,
        TokenizerKind.WordPiece,
        QueryPrompt: "find: ",
        PassagePrompt: string.Empty,
        Pooling.FirstToken,
        Dimension: 8,
        MaxTokens: 64,
        new Bars(Close: 0.95f, Agree: 0.9f, Meant: 0.5f, Floor: 0.4f, Blend: 0.6));

    /// <summary>
    /// Fake models under the names of the models brain ships, the default first, since settings.json names only those.
    /// The first has the fake model's files; the second has files of its own, under other/, and its own query prompt.
    /// </summary>
    public static readonly IReadOnlyList<EmbedderProfile> Shipped =
    [
        Profile with { Name = EmbedderProfiles.Default.Name },
        Profile with
        {
            Name = EmbedderProfiles.All.Single(profile => profile != EmbedderProfiles.Default).Name,
            Weights = File(Other, "weights.onnx"),
            Tokenizer = File(Other, "tokens.txt"),
            Megabytes = 2,
            QueryPrompt = "other: ",
        },
    ];

    /// <summary>A mirror under <paramref name="root"/> that serves the files of <paramref name="profile"/>, as a file url.</summary>
    public static string Mirror(string root, EmbedderProfile profile)
    {
        foreach (var file in profile.Files)
        {
            var path = root + file.Url.AbsolutePath;
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            System.IO.File.WriteAllText(path, Content(file));
        }

        return new Uri(root).AbsoluteUri;
    }

    /// <summary>What a fake model's file holds: its url's path, such as fake/weights.onnx, so every file differs.</summary>
    public static string Content(ModelFile file) => file.Url.AbsolutePath.TrimStart('/');

    /// <summary>A file of a fake model under <paramref name="dir"/>, pinned to the SHA-256 of its <see cref="Content"/>.</summary>
    private static ModelFile File(string dir, string name) =>
        new(new Uri(Url + dir + "/" + name), Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(dir + "/" + name))));
}
