namespace Brain;

/// <summary>The three scopes. Personal answers to the user, global to the world, project to the code.</summary>
internal static class ScopeNames
{
    public const string Personal = "personal";
    public const string Global = "global";
    public const string Project = "project";

    public static readonly IReadOnlyList<string> All = [Project, Global, Personal];

    public static bool IsScope(string name) => All.Contains(name, StringComparer.Ordinal);
}
