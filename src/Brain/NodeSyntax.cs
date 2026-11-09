namespace Brain;

/// <summary>The markdown a node file is written in, shared by the parser and the renderer.</summary>
internal static class NodeSyntax
{
    public const string Fence = "---";
    public const string TitlePrefix = "# ";
    public const string HeadingPrefix = "## ";
    public const string ItemPrefix = "- ";
    public const string FieldSeparator = ": ";
    public const string Arrow = " -> ";
    public const string WhyHeading = "Why";
    public const string EdgesHeading = "Edges";
    public const char NewLine = '\n';

    /// <summary>True for a line that opens a section of the node file, so a body or a why may not hold one.</summary>
    public static bool IsSection(string line) => IsHeading(line, WhyHeading) || IsHeading(line, EdgesHeading);

    /// <summary>True for the line <c>## name</c>, trailing space aside.</summary>
    public static bool IsHeading(string line, string name) =>
        string.Equals(line.TrimEnd(), HeadingPrefix + name, StringComparison.Ordinal);
}
