using Brain.Judging;
using Brain.Models;
using Brain.Search;

namespace Brain;

/// <summary>Builds the <see cref="BrainEnv"/> of the running process from its environment variables.</summary>
// navarone: 0000065
internal static class ProcessEnv
{
    private const string HomeVariable = "BRAIN_HOME";
    private const string TranscriptsVariable = "BRAIN_TRANSCRIPTS";
    private const string SessionVariable = "CLAUDE_CODE_SESSION_ID";
    private const string JudgeVariable = "BRAIN_JUDGE";
    private const string JudgeOff = "off";
    private const string ClaudeHomeVariable = "CLAUDE_CONFIG_DIR";
    private const string DefaultClaudeHome = ".claude";
    private const string Rules = "skills/brain/SKILL.md";
    private const string ModelFromBinary = "../model";
    private const string PackagedHarnessFromBinary = "../share/brain/harness";
    private const string CheckoutFromBinary = "../..";
    private const string DataVariable = "XDG_DATA_HOME";
    private const string DefaultData = ".local/share";
    private const string BrainDir = "brain";
    private const string Transcripts = "projects";

    /// <summary>
    /// The environment of this process, finding nodes by meaning with <paramref name="embedder"/>, which the caller owns.
    /// The brain skill installed in Claude Code's config directory is the file the judge reads its rules from.
    /// </summary>
    public static BrainEnv Capture(IEmbedder embedder)
    {
        var home = Home();
        var models = new ClaudeModels(ClaudeModels.Command);
        return new(
            home,
            Setting(TranscriptsVariable, Path.Combine(ClaudeHome(), Transcripts)),
            UserHome(),
            WorkingDirectory(),
            DateOnly.FromDateTime(DateTime.Now),
            Console.In,
            Console.Out,
            Console.Error,
            Session(),
            Judge(home, models),
            embedder,
            models,
            SkillFile: Path.Combine(ClaudeHome(), Rules),
            CodeSession: CodeSession());
    }

    /// <summary>The brain root: BRAIN_HOME, or the default when it is unset.</summary>
    public static string Home() => Setting(HomeVariable, DefaultHome());

    /// <summary>The working directory, which fails when it was removed, such as an agent's worktree a shell was left in.</summary>
    private static string WorkingDirectory()
    {
        try
        {
            return Directory.GetCurrentDirectory();
        }
        catch (FileNotFoundException gone)
        {
            throw BrainException.Failed("the working directory no longer exists; cd into one that does", gone);
        }
    }

    /// <summary>The Claude Code session that runs this process, from CLAUDE_CODE_SESSION_ID; null in the user's own terminal.</summary>
    public static string? Session() => Environment.GetEnvironmentVariable(SessionVariable) is { Length: > 0 } session ? session : null;

    /// <summary>The brain code session that runs this process, from the variable its server sets; null outside one.</summary>
    private static string? CodeSession() => Environment.GetEnvironmentVariable(Code.CodeSession.AskVariable) is { Length: > 0 } session ? session : null;

    /// <summary>
    /// The brain root as far as it is known without the working directory, which may be gone: BRAIN_HOME when it is a
    /// full path, the default when it is unset, and null for a relative one. It never throws, so a failure can be recorded.
    /// </summary>
    public static string? KnownHome()
    {
        var value = Environment.GetEnvironmentVariable(HomeVariable);
        if (!string.IsNullOrEmpty(value))
        {
            return Path.IsPathFullyQualified(value) ? value : null;
        }

        var data = Environment.GetEnvironmentVariable(DataVariable);
        return string.IsNullOrEmpty(data) ? Path.Combine(UserHome(), DefaultData, BrainDir)
            : Path.IsPathFullyQualified(data) ? Path.Combine(data, BrainDir) : null;
    }

    /// <summary>Claude Code's config directory: CLAUDE_CONFIG_DIR, or ~/.claude when it is unset.</summary>
    public static string ClaudeHome() => Setting(ClaudeHomeVariable, DefaultClaudeHome);

    /// <summary>
    /// The harness a package put beside the binary, share/brain/harness one directory above the binary's own, such as
    /// /usr/share/brain/harness; null for a bare binary, which carries its harness embedded.
    /// </summary>
    public static string? PackagedHarness()
    {
        var harness = FromBinary(PackagedHarnessFromBinary);
        return Directory.Exists(harness) ? harness : null;
    }

    /// <summary>Where brain install puts the embedding models, a directory for each: cache/model, beside the binary's cache/publish.</summary>
    public static string ModelDirectory() => FromBinary(ModelFromBinary);

    /// <summary>The brain root when BRAIN_HOME is unset: brain under XDG_DATA_HOME, or under ~/.local/share.</summary>
    public static string DefaultHome() => Path.Combine(Setting(DataVariable, DefaultData), BrainDir);

    /// <summary>
    /// The checkout the binary was published from, two directories above cache/publish/brain, when it holds the harness
    /// and the bin/brain launcher; null for a binary that lives anywhere else.
    /// </summary>
    public static string? Checkout()
    {
        var checkout = Path.TrimEndingDirectorySeparator(FromBinary(CheckoutFromBinary));
        return Directory.Exists(Path.Combine(checkout, "harness")) && File.Exists(Path.Combine(checkout, "bin", "brain")) ? checkout : null;
    }

    /// <summary>
    /// The judge, unless BRAIN_JUDGE is off. It reads its rules from the installed harness: the brain skill in Claude
    /// Code's config directory, so a package and a bare binary judge alike. It asks the model <paramref name="home"/>'s
    /// settings name.
    /// </summary>
    private static IJudge Judge(string home, IModels models) =>
        string.Equals(Environment.GetEnvironmentVariable(JudgeVariable), JudgeOff, StringComparison.Ordinal)
            ? new OpenJudge()
            : new ClaudeJudge(Path.Combine(ClaudeHome(), Rules), models, home);

    /// <summary>A path relative to the binary.</summary>
    private static string FromBinary(string relative) =>
        Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, relative));

    /// <summary>The variable's value as a full path; when it is unset, <paramref name="fallback"/>, relative to the user's home unless it is full itself.</summary>
    private static string Setting(string variable, string fallback)
    {
        var value = Environment.GetEnvironmentVariable(variable);
        return string.IsNullOrEmpty(value) ? Path.Combine(UserHome(), fallback) : Path.GetFullPath(value);
    }

    private static string UserHome() => Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
}
