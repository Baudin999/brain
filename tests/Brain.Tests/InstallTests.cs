using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Runtime.Versioning;
using System.Text;
using System.Text.Json.Nodes;
using Brain.Commands;
using Brain.Installing;

namespace Brain.Tests;

/// <summary>
/// brain install, its update and brain uninstall against temp directories for Claude Code's config, the CLI's link, the
/// guard and the model. Nothing reaches the network: the model's mirror is a directory that does not exist, so the
/// download fails and the install goes on. brain runs on Linux, macOS and WSL, so file modes are Unix ones.
/// </summary>
[UnsupportedOSPlatform("windows")]
public sealed class InstallTests : IDisposable
{
    private const string YesFlag = "--yes";
    private const string HarnessFlag = "--harness";
    private const string PartsFlag = "--harness-parts";
    private readonly TempBrain brain = new();
    private readonly Installation at;

    public InstallTests()
    {
        var tools = Path.Combine(brain.Root, "tools");
        Directory.CreateDirectory(tools);
        foreach (var tool in new[] { "git", "jq", "claude" })
        {
            File.WriteAllText(Path.Combine(tools, tool), string.Empty);
        }

        at = new Installation(
            Path.Combine(brain.Root, "claude"),
            Path.Combine(brain.Root, "bin"),
            Path.Combine(brain.Root, "config", "brain", "guard"),
            brain.Home,
            Path.Combine(brain.Root, "model"),
            FakeProfile.Shipped,
            new Uri(Path.Combine(brain.Root, "no-mirror")).AbsoluteUri,
            Checkout: null,
            PackagedHarness: null,
            tools,
            FreePort());
    }

    private string ClaudeMd => Path.Combine(at.ClaudeHome, "CLAUDE.md");

    private string Settings => Path.Combine(at.ClaudeHome, "settings.json");

    /// <summary>Where a bare binary unpacks its harness: the data repo's cache.</summary>
    private string Unpacked => Path.Combine(brain.Home, "cache", "harness");

    private static SortedDictionary<string, byte[]> Embedded => HarnessRoot.Embedded();

    public void Dispose() => brain.Dispose();

    [Fact]
    public void EveryMissingPrerequisiteIsNamedAndNothingChanges()
    {
        var (code, _, errors) = Install(string.Empty, [YesFlag], at with { SearchPath = string.Empty });

        Assert.Equal(ExitCodes.Failure, code);
        Assert.Contains("brain needs git, and it is not on PATH", errors, StringComparison.Ordinal);
        Assert.Contains("brain needs jq, and it is not on PATH", errors, StringComparison.Ordinal);
        Assert.Contains("brain needs the claude CLI", errors, StringComparison.Ordinal);
        Assert.Contains("3 missing", errors, StringComparison.Ordinal);
        Assert.False(Directory.Exists(at.ClaudeHome));
    }

    [Fact]
    public void ItSaysWhatBrainIsAndShowsEachChangeBeforeAnyAnswerButYesChangesNothing()
    {
        Directory.CreateDirectory(at.ClaudeHome);
        File.WriteAllText(ClaudeMd, "# mine\n");

        var (code, output, _) = Install("\n\nk\nno\n", []);

        Assert.Equal(ExitCodes.Failure, code);
        Assert.True(output.IndexOf("never writes code itself", StringComparison.Ordinal) < output.IndexOf("+++ " + Settings, StringComparison.Ordinal));
        Assert.Contains("beta under active development", output, StringComparison.Ordinal);
        Assert.Contains("default to Opus, and every write to the\ngraph calls a Sonnet judge", output, StringComparison.Ordinal);
        Assert.Contains("    -# mine", output, StringComparison.Ordinal);
        Assert.Contains($"replace {ClaudeMd}: your file there becomes a link to {Path.Combine(Unpacked, "CLAUDE.md")}", output, StringComparison.Ordinal);
        Assert.Contains("You already have " + ClaudeMd + ", and it is yours", output, StringComparison.Ordinal);
        Assert.Contains("kept       " + ClaudeMd + " (yours)", output, StringComparison.Ordinal);
        Assert.Contains("running brain install again overwrites them, so editing them is at your own risk", output, StringComparison.Ordinal);
        Assert.EndsWith("Nothing was changed.\n", output, StringComparison.Ordinal);
        Assert.Equal([ClaudeMd], Directory.EnumerateFileSystemEntries(at.ClaudeHome));
        Assert.Equal("# mine\n", File.ReadAllText(ClaudeMd));
        Assert.False(Directory.Exists(Unpacked));
    }

    [Fact]
    public void ABareBinaryUnpacksItsHarnessIntoTheDataRepoAndLinksEveryPartToIt()
    {
        Directory.CreateDirectory(at.ClaudeHome);
        File.WriteAllText(ClaudeMd, "# mine\n");

        var (code, output, _) = Install(string.Empty, [YesFlag, "--permission-mode", "acceptEdits"]);

        Assert.Equal(ExitCodes.Ok, code);
        Assert.Equal(Path.Combine(Unpacked, "CLAUDE.md"), new FileInfo(ClaudeMd).LinkTarget);
        Assert.Equal(Path.Combine(Unpacked, "skills", "brain"), new DirectoryInfo(Path.Combine(at.ClaudeHome, "skills", "brain")).LinkTarget);
        Assert.Equal(Path.Combine(Unpacked, "hooks", "graph-door.sh"), new FileInfo(Path.Combine(at.ClaudeHome, "hooks", "graph-door.sh")).LinkTarget);
        Assert.Equal(Embedded["skills/brain/SKILL.md"], File.ReadAllBytes(Path.Combine(at.ClaudeHome, "skills", "brain", "SKILL.md")));
        Assert.True((File.GetUnixFileMode(Path.Combine(Unpacked, "hooks", "graph-door.sh")) & UnixFileMode.UserExecute) != 0);
        Assert.Null(new FileInfo(Settings).LinkTarget);
        Assert.Equal("acceptEdits", JsonNode.Parse(File.ReadAllText(Settings))!["permissions"]!["defaultMode"]!.GetValue<string>());
        var saved = new Manifest(at.ManifestPath).Lines().Single(line => line.Kind == Manifest.Saved);
        Assert.Equal(ClaudeMd, saved.Target);
        Assert.Equal("# mine\n", File.ReadAllText(saved.Detail));
        Assert.Contains($"new     {Unpacked}: the harness this binary carries, unpacked for the links below", output, StringComparison.Ordinal);
        Assert.Contains("Run it before you remove brain's package: a package manager runs nothing per user", output, StringComparison.Ordinal);
        Assert.Contains("no brain serve of this brain runs", output, StringComparison.Ordinal);
    }

    [Fact]
    public void APackagedHarnessIsLinkedWhereItStandsAndNothingIsUnpacked()
    {
        var packaged = Harness("package");

        var (code, output, _) = Install(string.Empty, [YesFlag], at with { PackagedHarness = packaged });

        Assert.Equal(ExitCodes.Ok, code);
        Assert.Contains($"links into {packaged}; an update of brain's package overwrites them", output, StringComparison.Ordinal);
        Assert.DoesNotContain("running brain install again overwrites", output, StringComparison.Ordinal);
        Assert.Equal(Path.Combine(packaged, "CLAUDE.md"), new FileInfo(ClaudeMd).LinkTarget);
        Assert.Equal(Path.Combine(packaged, "agents", "brain.md"), new FileInfo(Path.Combine(at.ClaudeHome, "agents", "brain.md")).LinkTarget);
        Assert.False(Directory.Exists(Unpacked));
    }

    [Fact]
    public void ASecondInstallChangesNothingAndNamesWhatIsInPlace()
    {
        Install(string.Empty, [YesFlag]);

        var (code, output, _) = Install(string.Empty, [YesFlag]);

        Assert.Equal(ExitCodes.Ok, code);
        Assert.Contains("install: 0 linked, 0 written, 0 backed up, ", output, StringComparison.Ordinal);
        Assert.Matches(@"skipped    [0-9]+ already in place: [^\n]*/cache/harness, CLAUDE\.md, settings\.json, agents/brain-ready\.md, agents/brain\.md, ", output);
        Assert.Empty(Directory.GetDirectories(at.Backups, "pre-install-*"));
    }

    [Theory]
    [InlineData("auto", "default")]
    [InlineData("acceptEdits", "acceptEdits")]
    [InlineData(null, "default")]
    public void AFirstInstallOffersTheCurrentModeButNeverAuto(string? current, string offered)
    {
        Directory.CreateDirectory(at.ClaudeHome);
        File.WriteAllText(Settings, current is null ? "{}" : "{\"permissions\": {\"defaultMode\": \"" + current + "\"}}");

        var (_, _, errors) = Install("\n\nk\nno\n", []);

        Assert.Contains($"Permission mode [{offered}]: ", errors, StringComparison.Ordinal);
    }

    [Fact]
    public void AReinstallOffersTheModeTheUserHasAutoIncluded()
    {
        Install(string.Empty, [YesFlag, "--permission-mode", "auto"]);

        var (_, output, errors) = Install("\n\nyes\n", []);

        Assert.Contains("Permission mode [auto]: ", errors, StringComparison.Ordinal);
        Assert.Contains("install: 0 linked, 0 written, 0 backed up, ", output, StringComparison.Ordinal);
    }

    [Fact]
    public void ThePersonalFilesGoWhereTheUserSaysAndClaudeCodeIsToldWhere()
    {
        var elsewhere = Path.Combine(brain.Root, "elsewhere");

        var (code, output, _) = Install($"{elsewhere}\n3\nyes\n", []);

        Assert.Equal(ExitCodes.Ok, code);
        Assert.True(Directory.Exists(Path.Combine(elsewhere, ".git")));
        Assert.True(Directory.Exists(Path.Combine(elsewhere, "graph", "personal")));
        Assert.Equal(Path.Combine(elsewhere, "cache", "harness", "CLAUDE.md"), new FileInfo(ClaudeMd).LinkTarget);
        var settings = JsonNode.Parse(File.ReadAllText(Settings))!;
        Assert.Equal(elsewhere, settings["env"]!["BRAIN_HOME"]!.GetValue<string>());
        Assert.Equal("plan", settings["permissions"]!["defaultMode"]!.GetValue<string>());
        Assert.Contains("export BRAIN_HOME=" + elsewhere, output, StringComparison.Ordinal);
    }

    [Fact]
    public void TheHarnessOptionLinksTheNamedPartsThereAndTheRestToTheInstalledHarness()
    {
        var checkout = Harness("checkout");
        File.WriteAllText(Path.Combine(checkout, "settings.json"), """{"checkout": true}""");

        var (code, _, _) = Install(string.Empty, [YesFlag, HarnessFlag, checkout, PartsFlag, "CLAUDE.md,hooks"]);

        Assert.Equal(ExitCodes.Ok, code);
        Assert.Equal(Path.Combine(checkout, "CLAUDE.md"), new FileInfo(ClaudeMd).LinkTarget);
        Assert.Equal(Path.Combine(checkout, "hooks", "lib.sh"), new FileInfo(Path.Combine(at.ClaudeHome, "hooks", "lib.sh")).LinkTarget);
        Assert.Equal(Path.Combine(Unpacked, "skills", "brain"), new DirectoryInfo(Path.Combine(at.ClaudeHome, "skills", "brain")).LinkTarget);
        Assert.Equal(Path.Combine(Unpacked, "agents", "brain.md"), new FileInfo(Path.Combine(at.ClaudeHome, "agents", "brain.md")).LinkTarget);
        Assert.True(JsonNode.Parse(File.ReadAllText(Settings))!["checkout"]!.GetValue<bool>());
    }

    [Fact]
    public void EveryPartLinkedElsewhereUnpacksNothingAndWarnsOfNoOverwrite()
    {
        var checkout = Harness("checkout");

        var (_, output, _) = Install(string.Empty, [YesFlag, HarnessFlag, checkout]);

        Assert.Contains("Do you want to continue?", output, StringComparison.Ordinal);
        Assert.DoesNotContain("  Edits    ", output, StringComparison.Ordinal);
        Assert.Equal(Path.Combine(checkout, "skills", "brain"), new DirectoryInfo(Path.Combine(at.ClaudeHome, "skills", "brain")).LinkTarget);
        Assert.False(Directory.Exists(Unpacked));
    }

    [Theory]
    [InlineData(PartsFlag, "hooks")]
    [InlineData(PartsFlag, "none,hooks")]
    [InlineData(HarnessFlag, "/nowhere", PartsFlag, "settings")]
    public void HarnessPartsNeedAHarnessAndKnownNames(params string[] arguments)
    {
        Assert.Throws<BrainException>(() => Install(string.Empty, [YesFlag, .. arguments]));
        Assert.False(Directory.Exists(at.ClaudeHome));
    }

    [Fact]
    public void AHarnessWithoutSettingsIsRefused()
    {
        var (code, _, errors) = Install(string.Empty, [YesFlag, HarnessFlag, Path.Combine(brain.Root, "nowhere")]);

        Assert.Equal(ExitCodes.Failure, code);
        Assert.Contains("is no harness", errors, StringComparison.Ordinal);
        Assert.False(Directory.Exists(at.ClaudeHome));
    }

    [Fact]
    public void ALinkThatChangesTargetSaysSoAndBrainsOwnLinksNeedNoBackup()
    {
        var checkout = Checkout();
        Install(string.Empty, [YesFlag, HarnessFlag, checkout]);

        var (code, output, _) = Install(string.Empty, [YesFlag, PartsFlag, "none"]);

        Assert.Equal(ExitCodes.Ok, code);
        Assert.Contains($"relink  {ClaudeMd}: it links to {Path.Combine(checkout, "CLAUDE.md")} now, and will link to {Path.Combine(Unpacked, "CLAUDE.md")}", output, StringComparison.Ordinal);
        Assert.Equal(Path.Combine(Unpacked, "skills", "brain"), new DirectoryInfo(Path.Combine(at.ClaudeHome, "skills", "brain")).LinkTarget);
        Assert.DoesNotContain("backed up  ", output, StringComparison.Ordinal);
        Assert.Empty(Directory.GetDirectories(at.Backups, "pre-install-*"));
    }

    [Fact]
    public void ACopyAnEarlierInstallWroteBecomesALinkAndNeedsNoBackup()
    {
        var copy = Path.Combine(at.ClaudeHome, "skills", "brain");
        var files = new SortedDictionary<string, byte[]>(StringComparer.Ordinal) { ["SKILL.md"] = Embedded["skills/brain/SKILL.md"] };
        Tree.Write(copy, files);
        new Manifest(at.ManifestPath).Record(Manifest.Made, copy, Manifest.HashPrefix + Tree.Hash(files));

        var (_, output, _) = Install(string.Empty, [YesFlag]);

        Assert.Contains($"replace {copy}: brain's copy there becomes a link to {Path.Combine(Unpacked, "skills", "brain")}", output, StringComparison.Ordinal);
        Assert.Equal(Path.Combine(Unpacked, "skills", "brain"), new DirectoryInfo(copy).LinkTarget);
        Assert.Empty(Directory.GetDirectories(at.Backups, "pre-install-*"));
    }

    [Fact]
    public void ThePrivateHarnessLinksInAndBrainsOwnNamesWin()
    {
        Directory.CreateDirectory(Path.Combine(brain.Home, "harness", "rules"));
        Directory.CreateDirectory(Path.Combine(brain.Home, "harness", "skills", "brain"));
        File.WriteAllText(Path.Combine(brain.Home, "harness", "rules", "mine.md"), "# a rule\n");
        File.WriteAllText(Path.Combine(brain.Home, "harness", "guard"), "#!/bin/sh\n");
        File.SetUnixFileMode(Path.Combine(brain.Home, "harness", "guard"), UnixFileMode.UserRead | UnixFileMode.UserExecute);

        var (_, output, _) = Install(string.Empty, [YesFlag]);

        Assert.Equal(Path.Combine(brain.Home, "harness", "rules", "mine.md"), new FileInfo(Path.Combine(at.ClaudeHome, "rules", "mine.md")).LinkTarget);
        Assert.Equal(Path.Combine(brain.Home, "harness", "guard"), new FileInfo(at.GuardPath).LinkTarget);
        Assert.Contains("(brain ships skills/brain itself)", output, StringComparison.Ordinal);
        Assert.Equal(Path.Combine(Unpacked, "skills", "brain"), new DirectoryInfo(Path.Combine(at.ClaudeHome, "skills", "brain")).LinkTarget);
    }

    [Fact]
    public void TheHookCommandsFollowTheConfigDirectory()
    {
        Install(string.Empty, [YesFlag]);

        var commands = JsonNode.Parse(File.ReadAllText(Settings))!["hooks"]!.AsObject()
            .SelectMany(each => each.Value!.AsArray()).SelectMany(matcher => matcher!["hooks"]!.AsArray()).Select(hook => hook!["command"]!.GetValue<string>()).ToList();
        Assert.Contains("~/claude/hooks/refusal.sh ~/claude/hooks/graph-door.sh", commands);
        Assert.Contains("~/claude/hooks/usage.sh", commands);
        Assert.DoesNotContain(commands, command => command.Contains("~/.claude/", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("/u/.claude", "~/.claude/")]
    [InlineData("/u/cfg/claude", "~/cfg/claude/")]
    [InlineData("/etc/claude", "/etc/claude/")]
    [InlineData("/etc/my claude", "'/etc/my claude'/")]
    public void TheConfigDirectoryIsSpelledAsTheShellReadsIt(string claudeHome, string spelled) =>
        Assert.Equal(spelled, SettingsFile.Spelled(claudeHome, "/u"));

    [Fact]
    public void ARerunUnpacksTheHarnessAgainOverEditsAndOffersTheModeTheUserHas()
    {
        Install(string.Empty, [YesFlag, "--permission-mode", "auto"]);
        File.AppendAllText(Path.Combine(at.ClaudeHome, "skills", "brain", "SKILL.md"), "an edit\n");

        var (code, output, errors) = Install("\n\nyes\n", []);

        Assert.Equal(ExitCodes.Ok, code);
        Assert.Contains("Permission mode [auto]: ", errors, StringComparison.Ordinal);
        Assert.Contains($"replace {Unpacked}: the harness this binary carries, unpacked again", output, StringComparison.Ordinal);
        Assert.Equal(Embedded["skills/brain/SKILL.md"], File.ReadAllBytes(Path.Combine(Unpacked, "skills", "brain", "SKILL.md")));
        Assert.Equal("auto", JsonNode.Parse(File.ReadAllText(Settings))!["permissions"]!["defaultMode"]!.GetValue<string>());
        Assert.Contains("install: 0 linked, 1 written, 0 backed up, ", output, StringComparison.Ordinal);
        Assert.Empty(Directory.GetDirectories(at.Backups, "pre-install-*"));
    }

    [Fact]
    public void ARerunKeepsEachPartLinkedToACheckoutWhereItIs()
    {
        var checkout = Checkout();
        File.WriteAllText(Path.Combine(checkout, "settings.json"), """{"checkout": true}""");
        Install(string.Empty, [YesFlag, HarnessFlag, checkout, PartsFlag, "CLAUDE.md,hooks"]);

        var (code, output, _) = Install(string.Empty, [YesFlag]);

        Assert.Equal(ExitCodes.Ok, code);
        Assert.Contains("install: 0 linked, 0 written, 0 backed up, ", output, StringComparison.Ordinal);
        Assert.Equal(Path.Combine(checkout, "CLAUDE.md"), new FileInfo(ClaudeMd).LinkTarget);
        Assert.Equal(Path.Combine(checkout, "hooks", "lib.sh"), new FileInfo(Path.Combine(at.ClaudeHome, "hooks", "lib.sh")).LinkTarget);
        Assert.Equal(Path.Combine(Unpacked, "skills", "brain"), new DirectoryInfo(Path.Combine(at.ClaudeHome, "skills", "brain")).LinkTarget);
        Assert.True(JsonNode.Parse(File.ReadAllText(Settings))!["checkout"]!.GetValue<bool>());
    }

    [Fact]
    public void ARerunFollowsAPackagedHarnessThatMoved()
    {
        Install(string.Empty, [YesFlag], at with { PackagedHarness = Harness("old") });
        var moved = Harness("new");

        Install(string.Empty, [YesFlag], at with { PackagedHarness = moved });

        Assert.Equal(Path.Combine(moved, "CLAUDE.md"), new FileInfo(ClaudeMd).LinkTarget);
        Assert.Equal(Path.Combine(moved, "skills", "brain"), new DirectoryInfo(Path.Combine(at.ClaudeHome, "skills", "brain")).LinkTarget);
    }

    [Fact]
    public void UninstallPutsBackWhatWasThereAndLeavesNoBackups()
    {
        Directory.CreateDirectory(at.ClaudeHome);
        File.WriteAllText(ClaudeMd, "# mine\n");
        File.WriteAllText(Settings, """{"theme": "dark"}""");
        Install(string.Empty, [YesFlag]);

        var (code, output, _) = Uninstall(YesFlag);

        Assert.Equal(ExitCodes.Ok, code);
        Assert.Equal("# mine\n", File.ReadAllText(ClaudeMd));
        Assert.Equal("""{"theme": "dark"}""", File.ReadAllText(Settings));
        Assert.Equal([ClaudeMd, Settings], Directory.EnumerateFileSystemEntries(at.ClaudeHome).Order(StringComparer.Ordinal));
        Assert.False(Directory.Exists(Unpacked));
        Assert.Contains($"removed    {Unpacked}", output, StringComparison.Ordinal);
        Assert.True(Directory.Exists(Path.Combine(brain.Home, "graph")));
    }

    [Fact]
    public void UninstallRemovesTheDirectoriesBrainCreated()
    {
        var checkout = Path.GetDirectoryName(Checkout())!;
        Install(string.Empty, [YesFlag], at with { Checkout = checkout });
        Assert.True(File.Exists(Path.Combine(at.LocalBin, "brain")));

        var (code, _, _) = Uninstall(YesFlag);

        Assert.Equal(ExitCodes.Ok, code);
        Assert.False(Directory.Exists(at.ClaudeHome));
        Assert.False(Directory.Exists(at.LocalBin));
        Assert.False(Directory.Exists(Path.Combine(brain.Home, "cache")));
        Assert.False(Directory.Exists(at.ModelDirectory));
    }

    [Fact]
    public void TheDefaultModelDownloadsFromAMirrorIntoADirectoryOfItsOwn()
    {
        var profile = FakeProfile.Shipped[0];
        var mirror = FakeProfile.Mirror(Path.Combine(brain.Root, "mirror"), profile);
        var model = Path.Combine(at.ModelDirectory, profile.Name);
        var (code, output, errors) = Install(string.Empty, [YesFlag], at with { ModelMirror = mirror });

        Assert.Equal(ExitCodes.Ok, code);
        Assert.DoesNotContain("install: ", errors, StringComparison.Ordinal);
        Assert.Contains($"new     {model}: the embedding model search ranks by meaning with, {profile.Name}, about 1 MB", output, StringComparison.Ordinal);
        Assert.Contains($"        {mirror}/fake/weights.onnx\n", output, StringComparison.Ordinal);
        Assert.Contains($"downloaded {model} ({profile.Name})", output, StringComparison.Ordinal);
        Assert.Equal(["tokens.txt", "weights.onnx"], Directory.EnumerateFiles(model).Select(Path.GetFileName).Order(StringComparer.Ordinal));

        Uninstall(YesFlag);

        Assert.False(Directory.Exists(at.ModelDirectory));
    }

    /// <summary>The model install downloads is the one the data repo's settings.json names, and no other.</summary>
    [Fact]
    public void TheModelTheSettingsNameIsTheOneThatDownloads()
    {
        var profile = FakeProfile.Shipped[1];
        File.WriteAllText(Path.Combine(brain.Home, BrainSettings.FileName), $$"""{"{{BrainSettings.EmbedderKey}}":"{{profile.Name}}"}""");
        var mirror = FakeProfile.Mirror(Path.Combine(brain.Root, "mirror"), profile);

        var (code, output, errors) = Install(string.Empty, [YesFlag], at with { ModelMirror = mirror });

        Assert.Equal(ExitCodes.Ok, code);
        Assert.DoesNotContain("install: ", errors, StringComparison.Ordinal);
        Assert.Contains($"downloaded {Path.Combine(at.ModelDirectory, profile.Name)} ({profile.Name})", output, StringComparison.Ordinal);
        Assert.Equal([Path.Combine(at.ModelDirectory, profile.Name)], Directory.EnumerateFileSystemEntries(at.ModelDirectory));
    }

    /// <summary>
    /// An install from before each model had a directory of its own left the model's files at the top of the model
    /// root. They move into their model's directory, so nothing downloads again, and nothing else stays at the top.
    /// </summary>
    [Fact]
    public void AModelFileFromBeforeModelDirectoriesMovesInAndAnyOtherFileGoes()
    {
        Directory.CreateDirectory(at.ModelDirectory);
        var profile = FakeProfile.Shipped[0];
        File.WriteAllText(Path.Combine(at.ModelDirectory, "weights.onnx"), FakeProfile.Content(profile.Weights));
        File.WriteAllText(Path.Combine(at.ModelDirectory, "tokens.txt"), FakeProfile.Content(profile.Tokenizer));
        File.WriteAllText(Path.Combine(at.ModelDirectory, "stray.bin"), "a file of no model brain ships");
        var model = Path.Combine(at.ModelDirectory, profile.Name);

        var (code, output, errors) = Install(string.Empty, [YesFlag]);

        Assert.Equal(ExitCodes.Ok, code);
        Assert.DoesNotContain("install: ", errors, StringComparison.Ordinal);
        Assert.Contains($"move    {Path.Combine(at.ModelDirectory, "weights.onnx")} -> {Path.Combine(model, "weights.onnx")}: ", output, StringComparison.Ordinal);
        Assert.Contains($"remove  {Path.Combine(at.ModelDirectory, "stray.bin")}: ", output, StringComparison.Ordinal);
        Assert.Contains(", 0 downloaded\n", output, StringComparison.Ordinal);
        Assert.Equal([model], Directory.EnumerateFileSystemEntries(at.ModelDirectory));
        Assert.Equal(FakeProfile.Content(profile.Weights), File.ReadAllText(Path.Combine(model, "weights.onnx")));
        Assert.Equal(FakeProfile.Content(profile.Tokenizer), File.ReadAllText(Path.Combine(model, "tokens.txt")));

        Uninstall(YesFlag);

        Assert.False(Directory.Exists(at.ModelDirectory));
    }

    [Fact]
    public void UninstallMovesAnEditedUnpackedHarnessAside()
    {
        Install(string.Empty, [YesFlag]);
        File.AppendAllText(Path.Combine(at.ClaudeHome, "skills", "brain", "SKILL.md"), "an edit\n");

        var (code, output, _) = Uninstall(YesFlag);

        Assert.Equal(ExitCodes.Ok, code);
        Assert.Contains("(it changed since brain wrote it)", output, StringComparison.Ordinal);
        var moved = Directory.GetFiles(at.Backups, "SKILL.md", SearchOption.AllDirectories).Single(path => path.Contains("uninstall-", StringComparison.Ordinal) && path.Contains("/skills/brain/", StringComparison.Ordinal));
        Assert.EndsWith("an edit\n", File.ReadAllText(moved), StringComparison.Ordinal);
        Assert.False(Directory.Exists(Path.Combine(at.ClaudeHome, "skills")));
    }

    [Fact]
    public void UninstallUndoesTheRecordOfAnInstallFromBeforeBrainInstall()
    {
        var checkout = Path.Combine(brain.Root, "checkout");
        Directory.CreateDirectory(checkout);
        File.WriteAllText(Path.Combine(checkout, "CLAUDE.md"), "# brain's\n");
        Directory.CreateDirectory(Path.Combine(at.ClaudeHome, "backups", "pre-install-1"));
        File.WriteAllText(Path.Combine(at.ClaudeHome, "backups", "pre-install-1", "settings.json"), "{}");
        File.CreateSymbolicLink(ClaudeMd, Path.Combine(checkout, "CLAUDE.md"));
        File.WriteAllText(Settings, """{"agent": "brain"}""");
        var manifest = new Manifest(at.ManifestPath);
        manifest.Record(Manifest.Made, ClaudeMd, Path.Combine(checkout, "CLAUDE.md"));
        manifest.Record(Manifest.Saved, Settings, Path.Combine(at.ClaudeHome, "backups", "pre-install-1", "settings.json"));
        manifest.Record(Manifest.Made, Settings, Manifest.Generated);

        var (code, _, _) = Uninstall(YesFlag);

        Assert.Equal(ExitCodes.Ok, code);
        Assert.False(File.Exists(ClaudeMd));
        Assert.Equal("{}", File.ReadAllText(Settings));
    }

    [Fact]
    public void UninstallWithoutARecordHasNothingToUndo()
    {
        var (code, output, _) = Uninstall(YesFlag);

        Assert.Equal(ExitCodes.Ok, code);
        Assert.Contains("there is no install record", output, StringComparison.Ordinal);
    }

    [Fact]
    public void TheRecordKeepsWhatStandsNowAndEveryBackupOfYours()
    {
        var manifest = new Manifest(at.ManifestPath);
        manifest.Record(Manifest.Made, "/t", "/a");
        manifest.Record(Manifest.Made, "/t", "sha256:1");
        manifest.Record(Manifest.Saved, "/t", "/first");
        manifest.Record(Manifest.Saved, "/t", "/second");
        manifest.Record(Manifest.Saved, "/t", "/second");

        Assert.Equal(
            [new ManifestLine(Manifest.Made, "/t", "sha256:1"), new ManifestLine(Manifest.Saved, "/t", "/first"), new ManifestLine(Manifest.Saved, "/t", "/second")],
            manifest.Lines());
    }

    [Fact]
    public void AnEditedSettingsFileIsTheUsersToReplaceOrKeepAndUninstallLosesNeitherVersion()
    {
        Directory.CreateDirectory(at.ClaudeHome);
        File.WriteAllText(Settings, """{"theme": "dark"}""");
        Install(string.Empty, [YesFlag]);
        File.AppendAllText(Settings, "\n");
        var edited = File.ReadAllText(Settings);

        var (_, output, _) = Install(string.Empty, [YesFlag]);

        Assert.Contains("You already have " + Settings + ", and it is yours", output, StringComparison.Ordinal);
        Assert.NotEqual(edited, File.ReadAllText(Settings));

        var (code, uninstalled, _) = Uninstall(YesFlag);

        Assert.Equal(ExitCodes.Ok, code);
        Assert.Equal("""{"theme": "dark"}""", File.ReadAllText(Settings));
        var later = Assert.Single(Directory.GetFiles(at.Backups, "settings.json", SearchOption.AllDirectories));
        Assert.Equal(edited, File.ReadAllText(later));
        Assert.Contains($"kept       {later} (your {Settings} as a later install found it", uninstalled, StringComparison.Ordinal);
    }

    [Fact]
    public void SettingsMergeObjectsAppendListsAndTakeTheModeLast()
    {
        var merged = SettingsFile.Merge(
            [Encoding.UTF8.GetBytes("""{"a": {"b": 1}, "l": [1], "permissions": {"defaultMode": "auto"}}"""), Encoding.UTF8.GetBytes("""{"a": {"c": 2}, "l": [2]}""")],
            "plan",
            brainHome: null,
            "~/.claude/");

        Assert.Equal(
            "{\n  \"a\": {\n    \"b\": 1,\n    \"c\": 2\n  },\n  \"l\": [\n    1,\n    2\n  ],\n  \"permissions\": {\n    \"defaultMode\": \"plan\"\n  }\n}\n",
            Encoding.UTF8.GetString(merged));
    }

    [Fact]
    public void EveryPreToolUseCommandHookRunsThroughRefusalOnceFromTheConfigDirectory()
    {
        var merged = SettingsFile.Merge(
            [
                Encoding.UTF8.GetBytes("""{"hooks": {"PreToolUse": [{"matcher": "Bash", "hooks": [{"type": "command", "command": "a.sh"}, {"type": "command", "command": "~/.claude/hooks/refusal.sh b.sh"}]}], "PostToolUse": [{"hooks": [{"type": "command", "command": "~/.claude/hooks/c.sh"}]}]}}"""),
                Encoding.UTF8.GetBytes("""{"hooks": {"PreToolUse": [{"matcher": "Edit", "hooks": [{"type": "command", "command": "d.sh"}]}]}}"""),
            ],
            "default",
            brainHome: null,
            "/c/");

        var hooks = JsonNode.Parse(merged)!["hooks"]!;
        Assert.Equal(
            ["/c/hooks/refusal.sh a.sh", "/c/hooks/refusal.sh b.sh", "/c/hooks/refusal.sh d.sh"],
            hooks["PreToolUse"]!.AsArray().SelectMany(matcher => matcher!["hooks"]!.AsArray()).Select(hook => hook!["command"]!.GetValue<string>()));
        Assert.Equal("/c/hooks/c.sh", hooks["PostToolUse"]![0]!["hooks"]![0]!["command"]!.GetValue<string>());
    }

    [Fact]
    public void TheDiffShowsEachChangeWithItsContext()
    {
        Assert.Equal(
            ["--- f", "+++ f", "@@ -1,3 +1,3 @@", " a", "-b", "+B", " c"],
            LineDiff.Unified("f", "a\nb\nc\n", "a\nB\nc\n"));
        Assert.Equal(["--- /dev/null", "+++ f", "@@ -0,0 +1,2 @@", "+a", "+b"], LineDiff.Unified("f", null, "a\nb\n"));
        Assert.Empty(LineDiff.Unified("f", "same\n", "same\n"));
        var far = Enumerable.Range(1, 20).Select(line => line.ToString(CultureInfo.InvariantCulture)).ToArray();
        var changed = far.ToArray();
        changed[1] = "two";
        changed[18] = "nineteen";
        var hunks = LineDiff.Unified("f", string.Join('\n', far), string.Join('\n', changed));
        Assert.Equal(["@@ -1,5 +1,5 @@", "@@ -16,5 +16,5 @@"], hunks.Where(line => line.StartsWith("@@", StringComparison.Ordinal)));
    }

    /// <summary>A harness root at <paramref name="name"/>/harness, holding the harness this binary carries, as a package or a checkout would.</summary>
    private string Harness(string name)
    {
        var root = Path.Combine(brain.Root, name, "harness");
        Tree.Write(root, Embedded);
        return root;
    }

    /// <summary>The harness of a checkout: harness/ beside the bin/brain launcher, as ./link.sh links to.</summary>
    private string Checkout()
    {
        var harness = Harness("checkout");
        Directory.CreateDirectory(Path.Combine(harness, "..", "bin"));
        File.WriteAllText(Path.Combine(harness, "..", "bin", "brain"), "#!/bin/sh\n");
        return harness;
    }

    private (int Code, string Out, string Err) Install(string input, string[] arguments, Installation? into = null)
    {
        using var reader = new StringReader(input);
        using var output = new StringWriter(CultureInfo.InvariantCulture);
        using var errors = new StringWriter(CultureInfo.InvariantCulture);
        var code = InstallCommand.Run(brain.Env(reader, output, errors), arguments, into ?? at);
        return (code, output.ToString(), errors.ToString());
    }

    private (int Code, string Out, string Err) Uninstall(params string[] arguments)
    {
        using var output = new StringWriter(CultureInfo.InvariantCulture);
        using var errors = new StringWriter(CultureInfo.InvariantCulture);
        var code = UninstallCommand.Run(brain.Env(TextReader.Null, output, errors), arguments, at);
        return (code, output.ToString(), errors.ToString());
    }

    private static int FreePort()
    {
        using var probe = new TcpListener(IPAddress.Loopback, 0);
        probe.Start();
        return ((IPEndPoint)probe.LocalEndpoint).Port;
    }
}
