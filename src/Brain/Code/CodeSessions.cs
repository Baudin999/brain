using System.Text.RegularExpressions;

namespace Brain.Code;

/// <summary>
/// The sessions of brain code's window: the registry of what runs, oldest first, in memory. A session is started here,
/// from the window's own new session or from <c>brain code</c>, and stops when the window closes it or the server ends.
/// The registry reaches the disk only when the user reloads onto a new build: it is handed on, written as the handover
/// the successor resumes every session from, and from then on no session starts here. Each change of a session goes to
/// <c>changed</c> with its id, and each question a session asked that ended unanswered goes to <c>unanswered</c> with
/// the session's id.
/// </summary>
internal sealed partial class CodeSessions(string program, ClaudeFiles claude, Action<string> changed, Action<string, CodeQuestions> unanswered) : IDisposable
{
    /// <summary>The agent a session runs when it names none: the open agent, as a terminal's <c>claude --agent brain</c>.</summary>
    public const string OpenAgent = "brain";

    private readonly Lock gate = new();
    private readonly List<CodeSession> sessions = [];
    private bool handed;

    /// <summary>
    /// Starts a session of <paramref name="named"/>, the <see cref="OpenAgent"/> when it names none, in
    /// <paramref name="cwd"/>, or continues the Claude Code session <paramref name="resume"/>, which runs in the folder its
    /// transcript names. Refused for an agent that is no file's name, a folder that is not there, a folder other than the one a moved
    /// session ran in, a session id that is none, and a session the window already shows.
    /// </summary>
    // navarone: 0000180
    public CodeSession Start(string? named, string? cwd, string? resume)
    {
        var agent = named ?? OpenAgent;
        lock (gate)
        {
            BrainException.ThrowIf(handed ? "brain code is reloading on its new build: start the session once its new window is open" : null);
        }

        BrainException.ThrowIf(!AgentName().IsMatch(agent) ? $"{agent} is no agent's name: an agent is a file's name, as brain or immediate" : null);
        var folder = cwd;
        if (resume is not null)
        {
            folder = Continued(resume, cwd);
        }

        if (folder is null)
        {
            throw new BrainException($"the session {resume} has no folder to continue in: name it");
        }

        if (!Directory.Exists(folder))
        {
            throw new BrainException($"{folder} is no folder");
        }

        var session = CodeSession.Start(program, agent, folder, resume, claude, changed, unanswered);
        Add(session);
        return session;
    }

    /// <summary>
    /// Hands every session on for a reload to <paramref name="keep"/>, the oldest first, with the id of
    /// <paramref name="open"/>, the one the window shows, as the successor knows it; refused while a session runs work
    /// the reload would stop. Once <paramref name="keep"/> kept it, no session starts here; when it fails, the sessions
    /// stay as they were.
    /// </summary>
    public void Hand(string? open, Action<CodeHandover> keep)
    {
        lock (gate)
        {
            foreach (var session in sessions)
            {
                BrainException.ThrowIf(session.View().Runs() ? $"the session {session.Id} of {session.Agent} still runs: reload once no session runs" : null);
            }

            var carried = sessions.Select(session => session.Carried()).ToList();
            var shown = sessions.Find(session => session.Id == open);
            keep(new CodeHandover(carried, shown?.Carried().Id));
            handed = true;
        }
    }

    /// <summary>
    /// Takes over the sessions a reload handed on, in their order: each that was alive continues by its id, with its
    /// agent and in its folder, and each that had ended stays ended as it was shown. One that will not continue shows
    /// ended, with why.
    /// </summary>
    public void Resume(CodeHandover handover)
    {
        foreach (var carried in handover.Sessions)
        {
            if (!carried.Alive)
            {
                Add(CodeSession.Ended(carried, claude, changed, unanswered));
                continue;
            }

            try
            {
                Start(carried.Agent, carried.Cwd, carried.Id);
            }
            catch (BrainException refusal) when (!refusal.IsFailure)
            {
                Add(CodeSession.Ended(carried with { Problem = "it did not continue on the new build: " + refusal.Message }, claude, changed, unanswered));
            }
        }
    }

    private void Add(CodeSession session)
    {
        lock (gate)
        {
            sessions.Add(session);
        }

        changed(session.Id);
    }

    /// <summary>The folder the Claude Code session <paramref name="resume"/> continues in: where its transcript says it ran, or <paramref name="cwd"/> when that is not known; refused when <paramref name="cwd"/> differs.</summary>
    private string? Continued(string resume, string? cwd)
    {
        if (!ClaudeFiles.IsSessionId(resume))
        {
            throw new BrainException($"{resume} is no Claude Code session id");
        }

        if (Find(resume) is not null)
        {
            throw new BrainException($"the session {resume} is in the window already");
        }

        var ran = RanIn(resume);
        if (ran is null)
        {
            return cwd;
        }

        if (cwd is not null && cwd != ran)
        {
            throw new BrainException($"the session {resume} runs in {ran}, not in {cwd}");
        }

        return ran;
    }

    /// <summary>The folder the transcript of the session <paramref name="resume"/> says it ran in, when it is still there.</summary>
    private string? RanIn(string resume)
    {
        var path = claude.Session(resume);
        if (path is null)
        {
            return null;
        }

        var ran = new Transcript(path).Cwd();
        if (ran is null || !Directory.Exists(ran))
        {
            return null;
        }

        return Path.TrimEndingDirectorySeparator(ran);
    }

    /// <summary>The session <paramref name="id"/>; refused when the window has none of that id.</summary>
    public CodeSession Of(string? id)
    {
        if (Find(id) is { } session)
        {
            return session;
        }

        throw new BrainException($"no session {id} in the window");
    }

    /// <summary>
    /// Names the session <paramref name="id"/> <paramref name="name"/> by sending <c>/rename</c> into it, so Claude Code
    /// keeps the name in the transcript, where <c>claude --resume</c> finds it too. Refused when the name breaks
    /// <see cref="SessionName"/>'s rule against the other sessions' names, and once the session stopped.
    /// </summary>
    public void Rename(string? id, string? name)
    {
        var session = Of(id);
        var others = new List<string>();
        foreach (var other in All())
        {
            if (other != session && other.Name() is { } taken)
            {
                others.Add(taken);
            }
        }

        session.Say("/rename " + SessionName.Checked(name, others), []);
    }

    /// <summary>Every session, the oldest first.</summary>
    public IReadOnlyList<CodeSession> All()
    {
        lock (gate)
        {
            return [.. sessions];
        }
    }

    /// <summary>Stops the session <paramref name="id"/> and takes it from the window.</summary>
    public void Close(string? id)
    {
        var session = Of(id);
        lock (gate)
        {
            sessions.Remove(session);
        }

        session.Dispose();
        changed(session.Id);
    }

    /// <summary>Stops every session, with everything each started: the server ends.</summary>
    public void Dispose()
    {
        foreach (var session in All())
        {
            session.Dispose();
        }

        lock (gate)
        {
            sessions.Clear();
        }
    }

    /// <summary>The session <paramref name="id"/>, or null when the window has none of that id.</summary>
    public CodeSession? Find(string? id)
    {
        lock (gate)
        {
            return sessions.Find(session => session.Id == id);
        }
    }

    [GeneratedRegex("^[A-Za-z0-9][A-Za-z0-9_.-]*$")]
    private static partial Regex AgentName();
}
