using System.Text.Json.Serialization;

namespace Brain.Staging;

/// <summary>
/// A write that passed its gates and waits for the user's yes: its id, the lines the command says once it lands, the
/// node files it changes, and where it was staged from. Brain's window shows the user every change in full, so what
/// lands is what the user read. A write staged before origins were kept has none: its origin is unknown.
/// </summary>
internal sealed record PendingWrite(string Id, IReadOnlyList<string> Lines, IReadOnlyList<StagedChange> Changes, WriteOrigin? Origin = null);

/// <summary>
/// Where a write was staged from: the Claude Code session that ran the command, when there was one. Which project a
/// write is for is the graph its changes land in, so the origin keeps no project.
/// </summary>
internal sealed record WriteOrigin(string? Session);

/// <summary>A write before the user: the write, the SHA-256 of its file, which an answer names, and when it was asked.</summary>
internal sealed record AskedWrite(PendingWrite Write, string Digest, DateTime AskedAt);

/// <summary>
/// The user's answer to a write, as <c>brain land</c> returns it: yes with the lines of what landed, no with the user's
/// own words when they said what to change, or refused with the reason nothing landed after a yes.
/// </summary>
internal sealed record PendingAnswer(string Id, string Verdict, string? Reason, IReadOnlyList<string> Lines, string? Refusal)
{
    public const string Yes = "yes";
    public const string No = "no";
    public const string Refused = "refused";
}

/// <summary>
/// A write the user answered, kept for two days so a mistaken answer can be found and undone: the write exactly as it
/// was asked, the answer, when it was given, and when it was undone, once it was.
/// </summary>
internal sealed record AnswerRecord(PendingWrite Write, PendingAnswer Answer, DateTime AnsweredAt, DateTime? UndoneAt = null);

/// <summary>
/// What an undo did: the verdict it undid, the commits an undone yes made, and why it could not commit, when it could not.
/// An undone yes whose files read as their repo's HEAD already made no commit and failed nothing.
/// </summary>
internal sealed record Undone(string Undid, IReadOnlyList<string> Commits, string? NotCommitted);

/// <summary>A write the user answered, with the SHA-256 of its record, which an undo names.</summary>
internal sealed record AnsweredWrite(AnswerRecord Record, string Digest);

/// <summary>The JSON a pending write, its answer and the record of its answer are kept in, generated at build time.</summary>
[JsonSourceGenerationOptions(WriteIndented = true)]
[JsonSerializable(typeof(PendingWrite))]
[JsonSerializable(typeof(PendingAnswer))]
[JsonSerializable(typeof(AnswerRecord))]
internal sealed partial class PendingJson : JsonSerializerContext;
