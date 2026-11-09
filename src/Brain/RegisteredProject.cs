namespace Brain;

/// <summary>One line of the project registry: the project's name, its repo root, its origin remote and the day it joined.</summary>
internal sealed record RegisteredProject(string Name, string RepoPath, string Remote, string Date)
{
    /// <summary>How a repo path under the user's home starts, so it reads the same after a restore.</summary>
    public const string HomeMark = "~";

    private static readonly string HomePrefix = HomeMark + Path.DirectorySeparatorChar;

    /// <summary>The repo root on this machine, with ~ expanded to <paramref name="userHome"/>.</summary>
    public string Root(string userHome) =>
        RepoPath.StartsWith(HomePrefix, StringComparison.Ordinal) ? Path.Combine(userHome, RepoPath[HomePrefix.Length..]) : RepoPath;
}
