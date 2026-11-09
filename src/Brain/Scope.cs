namespace Brain;

/// <summary>
/// One graph in effect: its name, the directory of its nodes, its traversal log, the repo its commit refs must
/// resolve in, which only a project scope has.
/// </summary>
internal sealed record Scope(string Name, string GraphDir, string LogPath, string? Repo);
