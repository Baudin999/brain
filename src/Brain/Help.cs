using System.Text;

namespace Brain;

/// <summary>One option of a command: its syntax as typed and what it does.</summary>
internal sealed record HelpFlag(string Syntax, string Description);

/// <summary>One command's help: the single source of its overview line, its full usage and its usage errors.</summary>
internal sealed record HelpEntry(string Name, string Args, string Summary)
{
    public IReadOnlyList<HelpFlag> Flags { get; init; } = [];

    public IReadOnlyList<string> Notes { get; init; } = [];

    /// <summary>Other forms of the command, each a full one-line usage.</summary>
    public IReadOnlyList<string> Alternatives { get; init; } = [];

    public IReadOnlyList<string> Examples { get; init; } = [];

    /// <summary>The one-line usage: the command, its arguments and each option in brackets.</summary>
    public string Usage => string.Join(' ', Parts);

    /// <summary>The usage as whole tokens, so a wrapped line never breaks inside a bracket.</summary>
    public IReadOnlyList<string> Parts => [Args.Length > 0 ? Name + " " + Args : Name, .. Flags.Select(flag => $"[{flag.Syntax}]")];
}

/// <summary>The text of <c>brain --help</c> and <c>brain &lt;command&gt; --help</c>, and the one usage line per command.</summary>
internal static class Help
{
    public const string HintOverview = "run brain --help for the commands";
    private const string Intro = "brain is a knowledge graph of plain-text nodes in git, shared by you and Claude.";
    private const string ProjectLine = "--project <name|path>";
    private const string ProjectText = "Put in front of any command: use that project's graph from any directory.";
    private const int NameColumn = 38;
    private const int Indent = 2;
    private const int Width = 100;

    internal static readonly (string Title, string[] Names)[] Groups =
    [
        ("Read the graph", ["context", "lookup", "nearest", "node", "walk"]),
        ("Write nodes", ["add", "edit", "update", "forget", "merge", "subjects"]),
        ("Edges", ["link", "unlink", "faded", "fold"]),
        ("Your yes, and the window", ["land", "serve", "ui"]),
        ("Read a project", ["workspace"]),
        ("Work with agents", ["code", "ask"]),
        ("Conversations", ["conversation", "dream"]),
        ("Upkeep", ["check", "voices", "migrate", "init"]),
        ("Your use of brain", ["stats", "record"]),
        ("Install", ["install", "uninstall"]),
    ];

    internal static readonly HelpEntry[] Entries =
    [
        new("context", string.Empty, "Show the scopes in effect, node counts and paths, and each agent's model; at most 8 lines."),
        new("lookup", "[words...]", "Search the graph by keyword and meaning; up to 10 sure or likely hits come back.")
        {
            Notes =
            [
                "Each line is a ref, its tier, sure or likely, and its title. A weak hit is left out, and a query with no sure or likely hit says so in one line.",
                "Travels no edge.",
                "Without the embedding model it says so in one line and ranks by keywords alone.",
            ],
            Flags = [new("--subject <S>", "Keep only nodes with subject S; with no words, list them all.")],
            Examples = ["brain lookup install script", "brain lookup --subject cli"],
        },
        new("nearest", "[<ref>]", "Show the 5 nodes nearest to a node, by meaning and keywords; every write shows them.")
        {
            Flags =
            [
                new("--scope <scope>", "Read a node's markdown from stdin or --file, as it would land in this scope."),
                new("--file <path>", "With --scope, read the markdown from this file."),
            ],
            Notes =
            [
                "Travels no edge. The node itself is left out.",
                "A project node is read against that project, global and personal; a personal or global node against personal, global and every project in projects.tsv.",
                "Without the embedding model it says so and ranks by keywords alone.",
            ],
            Examples = ["brain nearest cache-rule", "brain nearest --scope global < node.md"],
        },
        new("node", "<ref>", "Show a node: title, fields, why and edges, strongest first.")
        {
            Notes =
            [
                "A ref is a slug, searched in project, global, personal order, or scope:slug.",
                "Opening a node travels each edge whose other end this session already opened.",
            ],
        },
        new("walk", "<ref>", "Show the titles of a node's neighbours, within 4000 characters.")
        {
            Notes =
            [
                "Follows edges both ways: a node's own and every edge another node writes into it, strongest first.",
                "Travels no edge: showing an edge is not travelling it.",
            ],
            Flags = [new("--hops <1|2>", "How far to walk from the node.")],
        },
        new("add", "<scope> <slug> [\"<title>\"]", "Add a node; waits for your yes.")
        {
            Flags =
            [
                new("--why <text>", "Why the node is true or worth keeping."),
                new("--body <text>", "The node's text."),
                new("--subjects <a,b>", "Its subjects, the keywords a search finds it by; a node needs one."),
                new("--file <path>", "Read the node as markdown from this file."),
                new("--kind <information|idea>", "What the node is; information by default."),
                new("--named-by <user|claude|agent>", "Who named it."),
                new("--session <id>", "The Claude session that wrote it."),
            ],
            Notes =
            [
                "Without a title, add reads a node file's markdown from stdin or --file: \"subjects: [a, b]\" between \"---\" lines,",
                "the title line, the body, \"## Why\" and the why, then \"## Edges\" with a \"- rel -> target\" line per edge.",
                "The edges are checked as link checks them and land with the node, on your yes.",
            ],
            Examples =
            [
                "brain add project cache-rule \"Cache by hash\" --subjects build --why \"Builds were slow\"",
                "brain add global cache-rule < node.md",
            ],
        },
        new("edit", "<ref>", "Rewrite a node's title, body, why, subjects and edges from markdown; waits for your yes.")
        {
            Flags = [new("--file <path>", "Read the markdown from this file instead of stdin.")],
            Notes =
            [
                "The markdown is add's: subjects in frontmatter, the title line, the body, \"## Why\" and \"## Edges\".",
                "It replaces the node's subjects and edges too: an edge it leaves out goes. Kind and the other fields stay.",
            ],
            Examples = ["brain edit cache-rule < node.md"],
        },
        new("update", "<ref> [\"<title>\"]", "Change parts of a node in place; waits for your yes.")
        {
            Flags =
            [
                new("--why <text>", "New why."),
                new("--body <text>", "New body."),
                new("--kind <information|idea>", "New kind."),
                new("--subjects <a,b>", "New subjects; an empty value clears them."),
            ],
            Examples = ["brain update cache-rule --why \"Rebuilds took minutes\""],
        },
        new("forget", "<ref...>", "Remove nodes and every edge that points at them; all or nothing; waits for your yes.")
        {
            Notes = ["Exit 1 when a node is missing."],
        },
        new("merge", string.Empty, "Apply a block of + lines from stdin; all or nothing; waits for your yes.")
        {
            Notes =
            [
                "A node line is:     + scope:slug | title | kind | named-by | why     (the why is optional)",
                "An edge line is:    + scope:slug -rel-> scope:slug",
                "A subjects line is: + scope:slug [a, b]     (the node is new in the block or already exists)",
                "Every new node needs a subjects line. Its edge lines land with it, on your yes.",
                "Any other line is prose and is ignored. One bad line applies nothing and exits 1.",
            ],
            Examples = ["brain merge < report.txt"],
        },
        new("subjects", "[scope]", "List the subjects in use in the scopes in effect, with node counts.")
        {
            Notes = ["Read from the nodes' frontmatter. Reuse a word in use before you coin a near-synonym."],
            Examples = ["brain subjects project"],
        },
        new("link", "<from-ref> <rel> <to-ref>", "Add an edge from one node to another.")
        {
            Notes = ["Does nothing when the edge exists."],
            Examples = ["brain link cache-rule because build-speed"],
        },
        new("unlink", "<from-ref> <rel> <to-ref>", "Remove an edge; exit 1 when it does not exist.")
        {
            Examples = ["brain unlink cache-rule because build-speed"],
        },
        new("faded", "[N]", "List the N weakest edges, the candidates to doubt; N defaults to 10."),
        new("fold", string.Empty, "Fold the edge log of every scope to one line per edge."),
        new("land", "<id>", "Put a pending write before you in brain's window; print your answer.")
        {
            Notes = ["Run it alone, as its own command. It returns once you answered in the window."],
        },
        new("serve", string.Empty, "Serve brain's window on 127.0.0.1 and open it.")
        {
            Notes = ["Only the window lands a write."],
        },
        new("ui", string.Empty, "Open brain's window in place of any open one, starting it if needed.")
        {
            Flags = [new("--restart", "Restart a running brain serve on this build.")],
        },
        new("workspace", "--serve <path>", "Serve a project folder's workspace, which brain code shows; brain code starts it.")
        {
            Notes =
            [
                "A workspace opens no window of its own: brain code shows a session's folder as its workspace, in a tab or side by side.",
                "Each folder has a server of its own. It ends a few seconds after nothing shows it.",
                "Every command the workspace runs and the server's work go to the data repo's perf/workspace, outside git.",
            ],
            Examples = ["brain workspace --serve ~/Projects/lens"],
        },
        new("code", "[agent] [folder]", "Start a Claude Code session of an agent, brain unless named, in a folder, here unless named.")
        {
            Flags =
            [
                new("--resume <session-id>", "Move a Claude Code session that runs elsewhere, as in a terminal, into the window; it continues by its id."),
                new("--serve", "Run the window's server in the foreground; brain code starts it in the background."),
                new("--permission <session-id>", "The PermissionRequest hook of a session: wait for the user's answer in the window."),
            ],
            Notes =
            [
                "There is one brain code window. A second brain code adds a session to it and brings it forward.",
                "A tool call that would ask your permission waits in the window, in its session, until you allow or deny it.",
                "Each session runs your own logged-in claude. The server ends with its window, and every session with it.",
                "Every call the window makes and the server's work go to the data repo's perf/code, outside git.",
            ],
            Examples = ["brain code", "brain code .", "brain code immediate ~/x", "brain code ~/x immediate", "brain code --resume 0b5e0d2c-1f7a-4f7e-9a51-0c2f55b0a7d1"],
        },
        new("ask", string.Empty, "Ask the user questions in brain code's window, as JSON on stdin; only in a brain code session.")
        {
            Notes =
            [
                "The JSON is {\"wait\": true|false, \"questions\": [...]}. Each question is {\"text\", \"options\", \"recommend\", \"why\", \"multiple\", \"own\"}.",
                "text is markdown. options is optional, a list of {\"label\", \"description\"}, the description optional.",
                "recommend is what you would answer, an option's label when there are options, and why says why. Both are required.",
                "multiple lets the user choose more than one option. own is accepted and changes nothing: the user may always answer in his own words, beside an option or alone.",
                "wait true blocks until he answered every question, then prints his answers: run it in the background. wait false prints that the questions wait and returns; his answers come to the session as his next message.",
                "The user may close the questions without answering them. A wait true ask then returns at once and prints that he closed them and that his next message says what he wants; it exits 0. A wait false ask hears the same line as his next message.",
                "A bad document, or one outside a brain code session, is refused before it reaches the window, with what is wrong.",
            ],
            Examples = ["brain ask < ask.json"],
        },
        new("conversation", "start <scope|project>", "Start a conversation in personal, global or a project, with its first sitting.")
        {
            Flags =
            [
                new("--model <haiku|sonnet|opus|fable>", "The sitting's model; the one settings.json names for a new sitting, sonnet unless set."),
                new("--effort <low|medium|high|xhigh|max>", "The sitting's effort; the one settings.json names for a new sitting, low unless set."),
            ],
            Alternatives =
            [
                "conversation park <session-id> <scope|project>",
                "conversation sitting <ref> [--model <m>] [--effort <e>]",
                "conversation rename <ref> \"<title>\"",
                "conversation commit <ref>",
                "conversation remove <ref>",
                "conversation list",
            ],
            Notes =
            [
                "A ref is <scope|project>:<slug>. The slug is the day and an id, fixed at birth, so a new title never moves the folder.",
                "A project's conversations live in its docs/brain/conversations, personal and global ones in the data repo's conversations/, outside every graph.",
                "sitting ends the current sitting and starts the next. list rebuilds cache/conversations.json from every state.md.",
                "park starts a conversation from a Claude Code session, run with $CLAUDE_CODE_SESSION_ID: it reads ~/.claude/projects/<dir>/<session-id>.jsonl, keeps the user's words and the agent's answers, and writes them as the ended sitting 1's trail. It does not commit; the window's button or conversation commit does.",
                "commit stages that conversation's folder and nothing else, TEMP excluded, and refuses when anything else is staged. The message is its title and the sitting's date. It commits in the scope's repo: the project, or the data repo.",
                "remove deletes the conversation's whole folder, settled.md, state.md, trails, research and TEMP, and rebuilds the index. It refuses, deleting nothing, while a dream runs on it or brain serve has a live sitting agent on it. It does not commit: the deletion waits in the working tree, and conversation commit <ref> commits it.",
            ],
            Examples = ["brain conversation start personal", "brain conversation sitting brain:2026-11-01-a3f9c2 --model opus --effort high"],
        },
        new("dream", "<ref> [--deep]", "Distill a conversation's unsettled sittings into its settled.md.")
        {
            Notes =
            [
                "Haiku and Sonnet each draft the new settled.md. When both are sure and Sonnet finds the drafts agree, Sonnet's lands; otherwise Opus gets the sittings and both drafts, and its draft lands.",
                "A dream writes the conversation only, never the graph. state.md marks each sitting it settled, and the next sitting's seed carries only the unsettled ones.",
                "Every dream is logged in the data repo's dreams/<id>.json and committed there. It refuses while a sitting is open: end it first with brain conversation sitting.",
                "--deep dreams into brain: it settles the unsettled sittings first, when there are any, then starts the open agent on the deep-dream setting, Opus at medium effort unless set, in the conversation's repo. The agent searches the graph and updates, forgets and adds nodes, each write waiting for your yes in brain's window, one node at a time, and commits each write once it lands. It never writes settled.md or a trail, and never commits the conversation's folder. Its final words are printed, and logged and committed as a deep dream.",
            ],
            Examples = ["brain dream brain:2026-11-01-a3f9c2", "brain dream personal:2026-11-01-a3f9c2 --deep"],
        },
        new("check", string.Empty, "Validate every scope in effect; exit 1 on any error.")
        {
            Notes = ["The old node model counts as an error, and information without a why is a migration line."],
        },
        new("voices", string.Empty, "Sync your typed Claude messages into voices/."),
        new("migrate", string.Empty, "Move old-format nodes to the current format.")
        {
            Notes = ["Uses no model and no judge. A second run changes nothing."],
            Flags = [new("--dry-run", "Show what would change; change nothing.")],
        },
        new("init", "[name]", "Give this repo a docs/brain and register it in projects.tsv.")
        {
            Notes = ["Commits nothing."],
        },
        new("stats", string.Empty, "Report what brain cost and gave back, from the usage log; tokens are estimates.")
        {
            Flags =
            [
                new("--since <yyyy-mm-dd>", "Count only events from this day on."),
                new("--until <yyyy-mm-dd>", "Count only events up to and including this day."),
                new("--json", "Print the report as JSON."),
            ],
            Notes =
            [
                "Sections: saves, validator rounds, knowledge reuse, readiness, guards, cost, hit rate and rework.",
                "A section that tells the evidence opens with its line of the story and its first 5 cases; --json lists every case.",
                "A token is estimated as four characters of output.",
            ],
            Examples = ["brain stats", "brain stats --since 2026-11-01 --until 2026-11-30"],
        },
        new("record", string.Empty, "Log the Claude Code hook JSON on stdin in the usage log; the harness runs it.")
        {
            Flags =
            [
                new("--refused <hook>", "Log the tool call as refused by this hook, instead of what it carries."),
                new("--reason <text>", "With --refused: the first line the hook said."),
            ],
            Notes =
            [
                "An Agent call logs a spawn, a SendMessage a message, a Skill a skill load; any other tool logs nothing.",
                "A readiness check's stop logs its verdict; any other agent's stop logs nothing.",
            ],
        },
        new("install", string.Empty, "Install brain into Claude Code, showing each change first.")
        {
            Flags =
            [
                new("--yes", "Answer yes to everything and replace your own CLAUDE.md and settings.json."),
                new("--permission-mode <mode>", "The permission mode Claude Code starts in, without the menu."),
                new("--harness <dir>", "Link the parts --harness-parts names to this harness, not the installed one."),
                new("--harness-parts <list>", "With --harness: CLAUDE.md, agents, hooks, skills, comma-separated; all by default."),
                new("--harness-parts none", "Link every part to the installed harness, a part linked to a checkout included."),
            ],
            Notes =
            [
                "Everything it puts in Claude Code's config directory is a link but settings.json, which it writes.",
                "Run it again after your package manager updated brain; it keeps a part linked to a checkout there.",
                "Running it again overwrites edits to the installed harness.",
                "Whatever of yours stands in the way moves to its backups/; brain uninstall puts it back.",
                "Run brain uninstall before you remove brain's package: a package manager runs nothing per user.",
                "CLAUDE_CONFIG_DIR, LOCAL_BIN and BRAIN_HOME move the targets.",
            ],
        },
        new("uninstall", string.Empty, "Put back what brain install changed; your data stays.")
        {
            Flags = [new("--yes", "Answer yes to the question.")],
        },
    ];

    /// <summary>The one-line usage of a command, as its refusals print it.</summary>
    public static string UsageOf(string name) => Find(name)!.Usage;

    /// <summary>The hint under a usage error: how to see that command's help, or the overview for an unknown one.</summary>
    public static string HintFor(string name) => Find(name) is null ? HintOverview : $"run brain {name} --help for its flags and examples";

    public static bool IsHelpFlag(string argument) => argument is "--help" or "-h";

    public static HelpEntry? Find(string name) => Entries.FirstOrDefault(entry => string.Equals(entry.Name, name, StringComparison.Ordinal));

    /// <summary>The overview: what brain is, then every command in its group.</summary>
    public static string Overview()
    {
        var text = new StringBuilder();
        text.AppendLine(Intro).AppendLine().AppendLine("usage: brain [--project <name|path>] <command> [arguments]");
        foreach (var (title, names) in Groups)
        {
            text.AppendLine().AppendLine(title);
            foreach (var entry in names.Select(name => Find(name)!))
            {
                text.AppendLine(Row(entry.Name + (entry.Args.Length > 0 ? " " + entry.Args : string.Empty), entry.Summary));
            }
        }

        text.AppendLine().AppendLine("Global flag").AppendLine(Row(ProjectLine, ProjectText));
        text.AppendLine().Append("Run brain <command> --help for a command's flags and examples.");
        return text.ToString();
    }

    /// <summary>One command's full help: usage, what it does, each flag, notes and examples.</summary>
    public static string For(HelpEntry entry)
    {
        var text = new StringBuilder();
        var usages = new[] { entry.Parts }.Concat(entry.Alternatives.Select(alternative => (IReadOnlyList<string>)[alternative]));
        foreach (var parts in usages)
        {
            text.AppendLine(Wrap(["usage: brain " + parts[0], .. parts.Skip(1)], string.Empty, new string(' ', Indent * 2)));
        }

        text.AppendLine().Append(entry.Summary);
        AppendBlock(text, "Flags", entry.Flags.Select(flag => Row(flag.Syntax, flag.Description)));
        AppendBlock(text, "Notes", entry.Notes.Select(note => Wrap(note.Split(' '), new string(' ', Indent), new string(' ', Indent))));
        AppendBlock(text, "Examples", entry.Examples.Select(example => new string(' ', Indent) + example));
        return text.ToString();
    }

    private static void AppendBlock(StringBuilder text, string title, IEnumerable<string> lines)
    {
        var rows = lines.ToList();
        if (rows.Count > 0)
        {
            text.AppendLine().AppendLine().AppendLine(title).Append(string.Join(Environment.NewLine, rows));
        }
    }

    private static string Row(string left, string right)
    {
        var head = new string(' ', Indent) + left;
        var prefix = new string(' ', NameColumn);
        return head.Length >= NameColumn
            ? head + Environment.NewLine + Wrap(right.Split(' '), prefix, prefix)
            : Wrap(right.Split(' '), head.PadRight(NameColumn), prefix);
    }

    /// <summary>Joins the tokens with spaces, starting after <paramref name="first"/> and breaking before <see cref="Width"/>.</summary>
    private static string Wrap(IReadOnlyList<string> tokens, string first, string rest)
    {
        var lines = new List<string>();
        var line = first;
        var fresh = true;
        foreach (var token in tokens)
        {
            if (!fresh && line.Length + 1 + token.Length > Width)
            {
                lines.Add(line);
                line = rest;
                fresh = true;
            }

            line += (fresh ? string.Empty : " ") + token;
            fresh = false;
        }

        lines.Add(line);
        return string.Join(Environment.NewLine, lines);
    }
}
