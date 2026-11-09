using Brain.Judging;
using Brain.Models;
using Brain.Search;
using Brain.Usage;

namespace Brain;

/// <summary>
/// The world one command runs in: the brain root, the transcripts, the user's home, the caller's directory, today, the
/// streams, the Claude Code session that runs the command, when there is one, the judge that reads every node before
/// it is written, the embedder search finds nodes by meaning with, the models a dream asks, the repo whose project
/// scope is in effect when <c>--project</c> named one, instead of the repo around the caller's directory, the refs the
/// command hands back for its usage line, when its call is metered, the brain skill's file, whose size is what loading it
/// costs, the program that runs the open agent: <c>claude</c>, or a stand-in a test names, and the brain code session
/// that runs the command, when one does.
/// </summary>
internal sealed record BrainEnv(
    string Home,
    string Transcripts,
    string UserHome,
    string Cwd,
    DateOnly Today,
    TextReader In,
    TextWriter Out,
    TextWriter Err,
    string? Session,
    IJudge Judge,
    IEmbedder Embedder,
    IModels Models,
    string? ProjectRepo = null,
    CallRefs? Refs = null,
    string? SkillFile = null,
    string Claude = ClaudeModels.Command,
    string? CodeSession = null);
