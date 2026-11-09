# A workspace

This file covers a workspace: one project folder, with its file tree, a reader that follows the disk live, git's
changes drawn in place and a git pane to stage, commit and push. A workspace is a brain code session's folder, shown in
brain code's window. It says how a workspace is served, what it reads and writes, its keys, how it is built from
commands and plugins, and what it records about its own speed. brain's own window is in [window.md](window.md), and
brain code's in [brain-code.md](brain-code.md).

## A session's folder

Each session of [brain code](brain-code.md#a-sessions-folder-its-workspace) runs in a folder, and that folder has its
workspace: its file tree on the left and the file open on the right, live as files change on disk. Sessions in the
same folder share one workspace. brain code shows it in a tab of its own, or beside the conversation; there is no
window of a workspace's own.

## Its server

Each workspace runs its own server, [`brain workspace --serve <path>`](cli.md#brain-workspace), a process of the same
binary, apart from brain code's. brain code starts it in the background the first time it shows the folder, with its
output in the data repo's `cache/workspace/<id>/serve.log`. It serves only its folder on a free port of 127.0.0.1, and
ends a few seconds after nothing shows it.

The server opens no window. brain code's server, with the key in the workspace's `cache/workspace/<id>`, asks it for a
launch, naming brain code's origin, and brain code's window opens the workspace's page in a frame on it. From then on
only that origin may frame the page. Like brain serve, the server takes calls only from its frame's session, which the
frame trades for the launch code once; brain code's window itself reads nothing of the folder. It writes nothing but
what the git pane asks of git: a stage, an unstage, a commit and a push.

## Reading

The tree lists a folder's entries once it is open, folders first, without `.git` and, in a git checkout, without what
git ignores. The reader draws markdown as brain's window does, mermaid diagrams included, and follows a link to another
file. Code and other text read plain, in the monospace font with their line numbers, and with no colour by keyword. A
binary file, or one over 2 MB, says so in place of its text. The server watches the folder through the system's file
events, inotify on Linux, and never polls; the tree and the open file follow what changed.

## Keys

Everything is a key. `/` opens the bar, which finds files and commands alike, each command with its key. The keys work
while the workspace has the focus; then its panes carry the focus line, and while brain code's conversation has it,
none does. A key with Alt the workspace does not bind is brain code's: `Alt+C`, `Alt+W` and `Alt+V` work from the
workspace too (see [brain-code.md](brain-code.md#keys)).

| Where | Key | What it does |
|---|---|---|
| tree | `j`, `k` | move |
| tree | `l`, `h` | open and close a folder |
| tree | `Enter` | open a folder or a file |
| reader | `j`, `k`, `Space`, `u`, `g`, `G` | scroll |
| reader | `n`, `N` | step through its links |
| reader | `Enter` | follow a link |
| anywhere | `Tab` | move between the panes |
| anywhere | `Backspace` | open the file before |
| anywhere | `Ctrl+B` | show or hide the tree |
| anywhere | `F5` | read the files again |
| anywhere | `F3` | show the probe |
| anywhere | `Alt+D` | switch the open file to its diff, and back |
| anywhere | `Ctrl+G` | open the git pane |
| anywhere | `Alt+S` | stage or unstage the file open |

## Git

In a git checkout the workspace shows what is uncommitted against HEAD: staged and unstaged changes together, and the
files git does not track yet. The tree colours each changed file by its state, added, changed, deleted or untracked, a
folder that holds changes in theirs, and lists a deleted file where it was.

Code has no colour by keyword, so the colour in the open file is git's: a changed or added run of lines takes its
state's wash behind the text and a bar in the margin, a changed paragraph, heading, list item, table, quote or code
block of markdown is coloured whole, and a line marks where lines were removed. `Alt+D` switches the file to its diff,
its removed lines read in place, and back; reading stays the default. A deleted file has nothing left to read, so it
opens on its diff, every line removed.

`Ctrl+G` opens the git pane under the tree:

- `j` and `k` move over the changed files;
- `s` or `Space` stages or unstages the whole file;
- `Enter` opens it;
- `c` puts the keys in the message, where `Enter` commits what is staged and `Esc` goes back;
- `C` commits and `P` pushes.

`Alt+S` stages or unstages the file open from anywhere. A commit takes only what the pane shows: in a workspace over one
folder of a large repo, what is staged outside the folder stays staged and out of the commit. A push sends only the
current branch to its upstream and never forces. A commit or a push git refuses says why in the pane.

The colours, the pane and the open file's marks follow the disk, git's index and its refs through the same watch, never
by polling: the watch takes a change to the index, HEAD or a ref as git moving, and watches a worktree's common git
folder beside the root. The colours are the `--git-*` tokens of `tokens.css`, a mark and a wash for each state, and
follow the Omarchy theme.

## How it is built

The workspace is built of five things:

- the workspace, which holds all its state;
- commands, each a name, a title, a budget and a run against the workspace;
- the keymap: a key, the focus it applies in and a command;
- views, which draw from the state with lit-html and never change it;
- plugins, which register commands, keys, views and renderers.

A key, the bar, a click, a link and a change on disk all end in executing a command. The tree, the reader, the bar, the
probe and git are plugins, and plugins come only from this codebase. A plugin can also say what a path is, as git says
changed, add entries a folder no longer holds, as git adds a deleted file, and mark the open file's lines.

## Records and budgets

Every command executed is a record: its name, its arguments, its source, how long it took until the change was on
screen, its outcome, and whether it went over its budget.

| What | Budget |
|---|---|
| a key | paints within one frame |
| a file opens | within 100 ms |
| the tree shows | within 200 ms |
| git's status | within 200 ms |
| a file's diff | within 100 ms |
| a commit, a push | none: they wait on hooks and the network |

The server records each file read, folder listing, watch event and git call the same way, and once a minute the CPU it
used, over budget past 1 percent of a minute in which the workspace made no call. The records go to the data repo's
`perf/workspace/`, a JSON line each in a file per day and workspace, kept 14 days and out of git; they never leave the
machine. `F3` shows the last five seconds of them in the probe, the one brain's window shows its frame times in.

## Related

- [window.md](window.md): brain's window, and the Stylebook a workspace shares.
- [brain-code.md](brain-code.md): brain code's window, which shows a session's folder as its workspace.
- [cli.md](cli.md#brain-workspace): the server's command.
