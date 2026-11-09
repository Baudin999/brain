using Brain.Commands;

namespace Brain.Installing;

/// <summary>
/// <c>brain install</c>: says what brain is, asks where its personal files go and which permission mode Claude Code
/// starts in, shows every change on disk with its diff, and asks whether to continue. Nothing is touched before that
/// yes. Then it makes every change, backing up whatever of the user's stood in the way and recording it for
/// <c>brain uninstall</c>, and restarts a running window on this build. Run again, after a package manager updated the
/// binary, it is the update: it unpacks a bare binary's harness again and puts the links right, and each part linked
/// to a checkout stays linked there unless the request says where it goes.
/// </summary>
internal sealed class Installer(BrainEnv env, Installation at, Choices choices, InstallRequest request)
{
    private const string AutoMode = "auto";
    private const string DefaultMode = "default";
    private static readonly string[] Needed = ["git", "jq", "claude"];

    private readonly Manifest manifest = new(at.ManifestPath);
    private readonly string backup = at.NewBackupDir("pre-install");
    private int linked;
    private int written;
    private int backedUp;
    private int kept;
    private string home = env.Home;
    private HarnessChoice harness = null!;

    public int Run()
    {
        if (Refusal() is { } refusal)
        {
            env.Err.WriteLine(refusal);
            return ExitCodes.Failure;
        }

        env.Out.WriteLine(InstallText.Banner);
        home = choices.Location("Where should brain keep your personal files, the data repo with your graph?", env.Home, env.UserHome);
        var installed = HarnessRoot.Installed(at, home);
        harness = request.Harness is { } dir
            ? new HarnessChoice(installed, HarnessRoot.At(dir), request.Parts ?? InstallPlan.Parts)
            : request.Parts is null ? HarnessChoice.Keeping(at.ClaudeHome, installed) : new HarnessChoice(installed, null, []);
        var plan = InstallPlan.Build(at, home, request.Mode ?? AskMode(), env.UserHome, harness);
        var placements = Show(plan);
        var changes = placements.Count + (plan.CreatesData ? 1 : 0) + plan.Model.Changes;
        if (changes == 0)
        {
            env.Out.WriteLine("Nothing to change.");
        }
        else
        {
            env.Out.WriteLine(InstallText.Continue(changes, backup, harness));
            if (!choices.Confirm("Do you want to continue?"))
            {
                env.Out.WriteLine("Nothing was changed.");
                return ExitCodes.Failure;
            }
        }

        var downloaded = Apply(placements, plan);
        var window = RestartWindow();
        env.Out.WriteLine($"install: {linked} linked, {written} written, {backedUp} backed up, {plan.InPlace.Count} already in place, {kept} kept, {downloaded} downloaded");
        if (!string.Equals(home, at.DefaultBrainHome, StringComparison.Ordinal))
        {
            env.Out.WriteLine($"Your data repo is at {home}, not at {at.DefaultBrainHome}, so brain finds it only through BRAIN_HOME. brain's settings.json sets it for Claude Code; your shell profile needs: export BRAIN_HOME={home}");
        }

        env.Out.WriteLine($"Undo it with brain uninstall. Run it before you remove brain's package: a package manager runs nothing per user, so it cannot take brain out of {at.ClaudeHome}.");
        if (window is not null)
        {
            env.Out.WriteLine();
            env.Out.WriteLine("install: brain's window was not restarted, so it still runs the build from before this install.");
            env.Out.WriteLine(window);
        }

        return ExitCodes.Ok;
    }

    /// <summary>Why brain cannot install here, naming every prerequisite that is missing; null when it can.</summary>
    private string? Refusal()
    {
        if (OperatingSystem.IsWindows())
        {
            return "brain install: brain runs on Linux, macOS and WSL, not on Windows itself. Install WSL and run brain and claude from there. Nothing was changed.";
        }

        var missing = Needed.Where(tool => !OnPath(tool)).Select(tool => tool == "claude"
            ? "brain install: brain needs the claude CLI, Claude Code, and it is not on PATH. Get it from https://claude.com/claude-code"
            : $"brain install: brain needs {tool}, and it is not on PATH. Install it with your package manager.").ToList();
        return missing.Count == 0
            ? null
            : string.Join('\n', missing.Append($"brain install: {missing.Count} missing. Install them and run brain install again. Nothing was changed."));
    }

    private bool OnPath(string tool) =>
        at.SearchPath.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries).Any(dir => File.Exists(Path.Combine(dir, tool)));

    /// <summary>Asks which permission mode Claude Code starts in.</summary>
    private string AskMode()
    {
        env.Err.WriteLine();
        env.Err.WriteLine($"Which permission mode should Claude Code start in? It sets permissions.defaultMode in {Path.Combine(at.ClaudeHome, "settings.json")}.");
        env.Err.WriteLine();
        return choices.Mode(CurrentMode(anyMode: manifest.Lines().Any(line => line.Kind == Manifest.Made && line.Target == Path.Combine(at.ClaudeHome, "settings.json"))));
    }

    /// <summary>
    /// The mode settings.json sets now, or default when it sets none Claude Code knows. Only on a reinstall, or with
    /// <paramref name="anyMode"/>, can that be auto: a first install never offers auto first, which is the user's to pick.
    /// </summary>
    private string CurrentMode(bool anyMode) =>
        SettingsFile.Mode(Path.Combine(at.ClaudeHome, "settings.json")) is { } mode && (anyMode || mode != AutoMode) && Choices.Modes.Any(each => each.Mode == mode)
            ? mode
            : DefaultMode;

    /// <summary>
    /// Shows every change with its diff, and asks replace or keep where a CLAUDE.md or settings.json of the user's own
    /// stands. Returns the placements to make.
    /// </summary>
    private List<Placement> Show(InstallPlan plan)
    {
        env.Out.WriteLine();
        env.Out.WriteLine("What this install changes on your file system, each change with its diff. Nothing is changed before you say yes at the end.");
        env.Out.WriteLine();
        var placements = new List<Placement>();
        foreach (var placement in plan.Placements)
        {
            var brains = placement.Unpacks || Brains(placement.Target);
            if (placement.Loss is { } loss && Tree.Exists(placement.Target) && !brains)
            {
                Write(placement.Shown(brains));
                env.Out.WriteLine(InstallText.ReplaceOrKeep(placement.Target, backup, loss));
                if (!choices.Replace(placement.Target))
                {
                    env.Out.WriteLine($"kept       {placement.Target} (yours)");
                    kept++;
                    continue;
                }
            }
            else
            {
                Write(placement.Shown(brains));
            }

            placements.Add(placement);
        }

        if (plan.CreatesData)
        {
            env.Out.WriteLine($"new     {home}: your data repo, with graph/personal, graph/global and voices, made a git repo with git init");
            env.Out.WriteLine();
        }
        else
        {
            env.Out.WriteLine($"skipped    data ({home} exists and is kept as it is)");
        }

        if (plan.InPlace.Count > 0)
        {
            var names = plan.InPlace.Select(target => target.StartsWith(at.ClaudeHome + "/", StringComparison.Ordinal) ? target[(at.ClaudeHome.Length + 1)..] : target);
            env.Out.WriteLine($"skipped    {plan.InPlace.Count} already in place: {string.Join(", ", names)}");
        }

        ShowModel(plan.Model);

        foreach (var note in plan.Notes)
        {
            env.Out.WriteLine(note);
        }

        return placements;
    }

    /// <summary>Shows each model file that moves or goes, and the download of the model the settings name, with the source of each file.</summary>
    private void ShowModel(ModelPlan model)
    {
        foreach (var move in model.Moves)
        {
            env.Out.WriteLine($"move    {move.From} -> {move.To}: a model file from before each model had a directory of its own");
        }

        foreach (var path in model.Removals)
        {
            env.Out.WriteLine($"remove  {path}: a file at the top of {at.ModelDirectory}, which holds only the models' directories now");
        }

        if (model.Downloads)
        {
            var directory = model.Profile.Directory(at.ModelDirectory);
            env.Out.WriteLine($"new     {directory}: the embedding model search ranks by meaning with, {model.Profile.Name}, about {model.Profile.Megabytes} MB, each file checked against its pinned SHA-256, from");
            foreach (var file in model.Profile.Files)
            {
                env.Out.WriteLine("        " + EmbeddingModel.Source(file, at.ModelMirror));
            }
        }

        if (model.Changes > 0)
        {
            env.Out.WriteLine();
        }
    }

    private void Write(IEnumerable<string> lines)
    {
        foreach (var line in lines)
        {
            env.Out.WriteLine(line);
        }

        env.Out.WriteLine();
    }

    /// <summary>Makes every change and returns how many models it downloaded.</summary>
    private int Apply(List<Placement> placements, InstallPlan plan)
    {
        if (plan.CreatesData)
        {
            DataRepo.Create(home);
            env.Out.WriteLine("created    " + home);
        }

        RecordCreated(at.ManifestPath);
        foreach (var placement in placements)
        {
            MoveAside(placement);
            RecordCreated(placement.Target);
            placement.Make();
            manifest.Record(Manifest.Made, placement.Target, placement.Detail);
            if (placement.LinkTo is null)
            {
                env.Out.WriteLine("wrote      " + placement.Target);
                written++;
            }
            else
            {
                env.Out.WriteLine($"linked     {placement.Target} -> {placement.LinkTo}");
                linked++;
            }
        }

        return Model(plan.Model);
    }

    /// <summary>
    /// Moves and removes the model files at the top of the model root, then downloads the model the settings name when the plan
    /// says so. Returns how many models it downloaded. The model root is recorded as downloaded once it holds a model,
    /// so uninstall removes every model in it.
    /// </summary>
    private int Model(ModelPlan model)
    {
        foreach (var move in model.Moves)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(move.To)!);
            File.Move(move.From, move.To, overwrite: true);
            env.Out.WriteLine($"moved      {move.From} -> {move.To}");
        }

        foreach (var path in model.Removals)
        {
            File.Delete(path);
            env.Out.WriteLine("removed    " + path);
        }

        var directory = model.Profile.Directory(at.ModelDirectory);
        if (model.Moves.Count > 0 || (!model.Downloads && EmbeddingModel.In(model.Profile, directory)))
        {
            manifest.Record(Manifest.Made, at.ModelDirectory, Manifest.Downloaded);
        }

        if (!model.Downloads)
        {
            return 0;
        }

        RecordCreated(directory);
        if (EmbeddingModel.Download(model.Profile, at.ModelMirror, directory) is { } failure)
        {
            env.Err.WriteLine($"install: {failure}; brain searches by keywords until brain install downloads the model");
            return 0;
        }

        manifest.Record(Manifest.Made, at.ModelDirectory, Manifest.Downloaded);
        env.Out.WriteLine($"downloaded {directory} ({model.Profile.Name})");
        return 1;
    }

    /// <summary>
    /// Clears the placement's target. What is brain's, such as a link brain made or the harness a bare binary unpacked,
    /// is deleted; anything else, an edit of what brain made included, moves into this install's backup directory and
    /// is recorded, so uninstall puts it back or names it. So a backup directory holds only what was the user's.
    /// </summary>
    private void MoveAside(Placement placement)
    {
        var target = placement.Target;
        if (!Tree.Exists(target))
        {
            return;
        }

        if (placement.Unpacks || Brains(target))
        {
            Tree.Delete(target);
            return;
        }

        var saved = at.BackupOf(backup, target);
        manifest.Record(Manifest.Saved, target, saved);
        Tree.Move(target, saved);
        env.Out.WriteLine($"backed up  {target} -> {saved}");
        backedUp++;
    }

    /// <summary>Records each directory above <paramref name="target"/> that does not exist yet, so uninstall removes the ones it leaves empty.</summary>
    private void RecordCreated(string target)
    {
        var missing = new List<string>();
        for (var dir = Path.GetDirectoryName(target); dir is not null && !Directory.Exists(dir); dir = Path.GetDirectoryName(dir))
        {
            missing.Add(dir);
        }

        missing.ForEach(dir => manifest.Record(Manifest.Made, dir, Manifest.Created));
    }

    /// <summary>
    /// True when brain made what stands at <paramref name="target"/> and nothing changed it since, so replacing it takes
    /// nothing of the user's. A link into a harness this install links to, the checkout or the data repo's harness is
    /// brain's too.
    /// </summary>
    private bool Brains(string target)
    {
        if (manifest.StillMade(target))
        {
            return true;
        }

        var source = Tree.LinkTarget(target);
        return source is not null && (harness.Holds(source) ||
            (at.Checkout is not null && source.StartsWith(at.Checkout + "/", StringComparison.Ordinal)) ||
            source.StartsWith(Path.Combine(home, "harness") + "/", StringComparison.Ordinal));
    }

    /// <summary>
    /// Puts a running brain serve of the chosen brain on this build. Returns why it could not, with the command that
    /// fixes it, or null.
    /// </summary>
    private string? RestartWindow()
    {
        try
        {
            UiCommand.Run(env with { Home = home }, restart: true, at.Port);
            return null;
        }
        catch (BrainException refusal)
        {
            return "brain ui: " + refusal.Message;
        }
    }
}
