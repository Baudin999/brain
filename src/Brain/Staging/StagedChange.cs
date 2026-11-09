namespace Brain.Staging;

/// <summary>
/// One node file a pending write changes: the node's ref as the prompt names it, the file's full path, its text on disk
/// when the write was staged, and the text the write leaves. Null is no file: a new node has no before, a forgotten one
/// no after.
/// </summary>
internal sealed record StagedChange(string Ref, string Path, string? Before, string? After);
