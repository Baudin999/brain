namespace Brain;

/// <summary>A node's three written parts, its title, body and why, each trimmed with every line ending \n.</summary>
internal sealed record NodeContent(string Title, string Body, string Why)
{
    public static NodeContent Of(Node node) => new(node.Title ?? string.Empty, node.Body, node.Why);

    /// <summary>Why the content cannot be written, or null when it can.</summary>
    public string? Problem() =>
        Titles.Problem(Title) ?? Titles.TextProblem(NodeParts.Body, Body) ?? Titles.TextProblem(NodeParts.Why, Why);

    /// <summary>The markdown in the file at <paramref name="path"/>, relative to the working directory; a missing file is refused.</summary>
    public static string ReadFile(BrainEnv env, string path)
    {
        var full = Path.GetFullPath(path, env.Cwd);
        return File.Exists(full) ? File.ReadAllText(full) : throw new BrainException($"no file {full}");
    }
}
