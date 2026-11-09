using System.Globalization;
using Brain.Commands;
using Brain.Search;

namespace Brain.Installing;

/// <summary>
/// Where <c>brain install</c> puts things and where it reads them from: Claude Code's config directory, the directory
/// on PATH the CLI is linked into, the machine's guard, the brain root used when BRAIN_HOME is unset, the root of the
/// embedding models' directories, every model brain ships, the mirror they download from when
/// one is set, the checkout the binary was built from, when it was built from one, the harness a package put
/// beside the binary, when one did, the PATH the prerequisites are looked up on and the port of brain's window.
/// </summary>
internal sealed record Installation(
    string ClaudeHome,
    string LocalBin,
    string GuardPath,
    string DefaultBrainHome,
    string ModelDirectory,
    IReadOnlyList<EmbedderProfile> Models,
    string? ModelMirror,
    string? Checkout,
    string? PackagedHarness,
    string SearchPath,
    int Port)
{
    private const string LocalBinVariable = "LOCAL_BIN";
    private const string ConfigVariable = "XDG_CONFIG_HOME";
    private const string ModelUrlVariable = "BRAIN_MODEL_URL";
    private const string PathVariable = "PATH";

    /// <summary>The model the settings of the data repo at <paramref name="brainHome"/> name, which install downloads.</summary>
    public EmbedderProfile ModelOf(string brainHome) => EmbedderProfiles.Named(Models, BrainSettings.Read(brainHome).Embedder);

    /// <summary>Where every install's backups go, and its record.</summary>
    public string Backups => Path.Combine(ClaudeHome, "backups");

    /// <summary>The record of every target brain made and every backup of something that was yours.</summary>
    public string ManifestPath => Path.Combine(Backups, "brain-install.tsv");

    /// <summary>
    /// A backup directory of this run that no earlier run took: <paramref name="kind"/>, the time in epoch seconds and,
    /// when a run in the same second took that, a count.
    /// </summary>
    public string NewBackupDir(string kind)
    {
        var dir = Path.Combine(Backups, kind + "-" + DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture));
        var taken = dir;
        for (var count = 2; Directory.Exists(taken); count++)
        {
            taken = dir + "-" + count.ToString(CultureInfo.InvariantCulture);
        }

        return taken;
    }

    /// <summary>
    /// Where <paramref name="target"/> goes in the backup directory <paramref name="dir"/>: under its path in Claude
    /// Code's config directory, or under outside/ and its full path when it lives anywhere else.
    /// </summary>
    public string BackupOf(string dir, string target)
    {
        var relative = Path.GetRelativePath(ClaudeHome, target);
        return Path.Combine(dir, relative.StartsWith("..", StringComparison.Ordinal) || Path.IsPathRooted(relative)
            ? Path.Combine("outside", target.TrimStart('/'))
            : relative);
    }

    /// <summary>
    /// The installation of this process: CLAUDE_CONFIG_DIR, LOCAL_BIN, XDG_CONFIG_HOME and BRAIN_MODEL_URL move the
    /// targets.
    /// </summary>
    public static Installation FromProcess(BrainEnv env) => new(
        ProcessEnv.ClaudeHome(),
        Variable(LocalBinVariable) ?? Path.Combine(env.UserHome, ".local", "bin"),
        Path.Combine(Variable(ConfigVariable) ?? Path.Combine(env.UserHome, ".config"), "brain", "guard"),
        ProcessEnv.DefaultHome(),
        ProcessEnv.ModelDirectory(),
        EmbedderProfiles.All,
        Mirror(),
        ProcessEnv.Checkout(),
        ProcessEnv.PackagedHarness(),
        Environment.GetEnvironmentVariable(PathVariable) ?? string.Empty,
        ServeCommand.Port());

    /// <summary>The mirror the models download from, BRAIN_MODEL_URL, or null when it is unset.</summary>
    public static string? Mirror() => Environment.GetEnvironmentVariable(ModelUrlVariable) is { Length: > 0 } url ? url : null;

    /// <summary>The variable's value as a full path, or null when it is unset.</summary>
    private static string? Variable(string name) =>
        Environment.GetEnvironmentVariable(name) is { Length: > 0 } value ? Path.GetFullPath(value) : null;
}
