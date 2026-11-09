using Brain.Search;

namespace Brain.Installing;

/// <summary>A model file at the top of the model root that moves into its model's directory.</summary>
internal sealed record ModelMove(string From, string To);

/// <summary>
/// What an install does to the model root: the model the settings name, the files at its top, from before each model
/// had a directory of its own, that move into their model's directory, the ones it removes, and whether that model
/// downloads.
/// </summary>
internal sealed record ModelPlan(EmbedderProfile Profile, IReadOnlyList<ModelMove> Moves, IReadOnlyList<string> Removals, bool Downloads)
{
    public int Changes => Moves.Count + Removals.Count + (Downloads ? 1 : 0);
}

/// <summary>
/// The files of the embedding models, each model in its own directory under the model root. A download fetches the
/// files of an <see cref="EmbedderProfile"/>, checks each against its pinned SHA-256, and keeps nothing unless every
/// file matches. A mirror serves the same files at the same paths over http, https or file; the hashes stay pinned.
/// </summary>
internal static class EmbeddingModel
{
    private const string Part = ".part";
    private const int Block = 1 << 16;
    private static readonly TimeSpan Timeout = TimeSpan.FromMinutes(10);

    /// <summary>True when <paramref name="directory"/> holds every file of <paramref name="profile"/> with its pinned SHA-256.</summary>
    public static bool In(EmbedderProfile profile, string directory) =>
        profile.Files.All(file => Holds(Path.Combine(directory, file.Name), file));

    /// <summary>Where <paramref name="file"/> downloads from: its own url, or the same path under <paramref name="mirror"/>.</summary>
    public static string Source(ModelFile file, string? mirror) =>
        mirror is null ? file.Url.AbsoluteUri : mirror.TrimEnd('/') + file.Url.AbsolutePath;

    /// <summary>
    /// What an install into <paramref name="at"/> does to the model root. Every file at its top is from the layout
    /// before each model had a directory of its own: one that is a file of a model brain ships, by its name and pinned
    /// SHA-256, moves into that model's directory, and any other is removed. The model the settings of the data repo
    /// at <paramref name="brainHome"/> name downloads when its directory lacks a file that no move brings.
    /// </summary>
    public static ModelPlan Plan(Installation at, string brainHome)
    {
        var model = at.ModelOf(brainHome);
        var root = at.ModelDirectory;
        var moves = new List<ModelMove>();
        var removals = new List<string>();
        IEnumerable<string> loose = Directory.Exists(root) ? Directory.EnumerateFiles(root).Order(StringComparer.Ordinal) : [];
        foreach (var path in loose)
        {
            var name = Path.GetFileName(path);
            var hash = new Lazy<string>(() => Tree.HashFile(path));
            var to = at.Models
                .SelectMany(profile => profile.Files.Select(file => (Directory: profile.Directory(root), File: file)))
                .Where(each => each.File.Name == name && each.File.Sha256 == hash.Value)
                .Select(each => Path.Combine(each.Directory, name))
                .FirstOrDefault(target => !Holds(target, hash.Value));
            if (to is null)
            {
                removals.Add(path);
            }
            else
            {
                moves.Add(new ModelMove(path, to));
            }
        }

        var directory = model.Directory(root);
        var downloads = !model.Files.All(file =>
            Holds(Path.Combine(directory, file.Name), file) || moves.Any(move => move.To == Path.Combine(directory, file.Name)));
        return new ModelPlan(model, moves, removals, downloads);
    }

    /// <summary>
    /// Downloads every file of <paramref name="profile"/>, from <paramref name="mirror"/> when one is set, into a scratch
    /// directory beside <paramref name="directory"/> and moves it into place when every file matches its hash. Returns
    /// why it kept nothing, or null when it is in place. <paramref name="progress"/> hears how many bytes of every file
    /// have arrived so far.
    /// </summary>
    public static string? Download(EmbedderProfile profile, string? mirror, string directory, Action<long>? progress = null)
    {
        var part = directory + Part;
        if (Tree.Exists(part))
        {
            Tree.Delete(part);
        }

        Directory.CreateDirectory(part);
        using var client = new HttpClient { Timeout = Timeout };
        long arrived = 0;
        foreach (var file in profile.Files)
        {
            var source = Source(file, mirror);
            var path = Path.Combine(part, file.Name);
            var before = arrived;
            try
            {
                Fetch(client, new Uri(source), path, bytes => progress?.Invoke(arrived = before + bytes));
            }
            catch (Exception error) when (error is HttpRequestException or TaskCanceledException or IOException or UriFormatException)
            {
                Directory.Delete(part, recursive: true);
                return $"could not download {source}: {error.Message.TrimEnd('.')}";
            }

            if (!Holds(path, file))
            {
                Directory.Delete(part, recursive: true);
                return $"{file.Name} does not match its pinned SHA-256, so nothing was kept";
            }
        }

        if (Tree.Exists(directory))
        {
            Tree.Delete(directory);
        }

        Directory.Move(part, directory);
        return null;
    }

    private static bool Holds(string path, ModelFile file) => Holds(path, file.Sha256);

    private static bool Holds(string path, string sha256) => File.Exists(path) && Tree.HashFile(path) == sha256;

    /// <summary>Copies <paramref name="source"/> to <paramref name="path"/>, telling <paramref name="copied"/> the bytes so far after each block.</summary>
    private static void Fetch(HttpClient client, Uri source, string path, Action<long> copied)
    {
        using var file = File.Create(path);
        if (source.IsFile)
        {
            using var local = File.OpenRead(source.LocalPath);
            Copy(local, file, copied);
            return;
        }

        using var response = client.GetAsync(source, HttpCompletionOption.ResponseHeadersRead).GetAwaiter().GetResult();
        response.EnsureSuccessStatusCode();
        using var body = response.Content.ReadAsStream();
        Copy(body, file, copied);
    }

    private static void Copy(Stream from, Stream to, Action<long> copied)
    {
        var buffer = new byte[Block];
        long total = 0;
        int read;
        while ((read = from.Read(buffer)) > 0)
        {
            to.Write(buffer, 0, read);
            total += read;
            copied(total);
        }
    }
}
