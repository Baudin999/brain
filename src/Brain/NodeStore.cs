namespace Brain;

/// <summary>The node files of one scope on disk.</summary>
internal static class NodeStore
{
    public static IEnumerable<Node> All(Scope scope) =>
        Directory.Exists(scope.GraphDir)
            ? Directory.EnumerateFiles(scope.GraphDir, Layout.NodePattern).Order(StringComparer.Ordinal).Select(Read)
            : [];

    public static void Write(Scope scope, Node node)
    {
        Directory.CreateDirectory(scope.GraphDir);
        AtomicFile.Write(PathOf(scope, node.Slug), NodeRenderer.Render(node));
    }

    /// <summary>The file of the node <paramref name="slug"/> in the scope, whether it exists or not.</summary>
    public static string PathOf(Scope scope, string slug) => Path.Combine(scope.GraphDir, slug + Layout.NodeExtension);

    /// <summary>The node's file as it is on disk now, or null when there is none.</summary>
    public static Node? Current(Scope scope, string slug)
    {
        var path = PathOf(scope, slug);
        return File.Exists(path) ? Read(path) : null;
    }

    public static void Delete(Scope scope, string slug) => File.Delete(PathOf(scope, slug));

    private static Node Read(string path) =>
        NodeParser.Parse(Path.GetFileNameWithoutExtension(path), File.ReadAllText(path));
}
