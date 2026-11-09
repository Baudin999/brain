# brain's window

This file covers brain's window, the one place where you answer every graph write: how it is served and opened, how it
closes, what it records about its own speed, node review and the project focus, Answered, Home and its constellation,
the settings page, the chat, the registry of agents brain runs, the Omarchy theme and the Stylebook. What your answer
does is in [the-yes.md](the-yes.md). Other tools' inboxes and the Navarone tab are in [sources.md](sources.md), and
conversations and dreams in [conversations.md](conversations.md).

## Serving and opening

[`brain serve`](cli.md#brain-serve) serves the window on 127.0.0.1 only, port 47474 or `BRAIN_PORT`. It reads the graph and
the pending writes through the CLI's own code and answers a write only through the pending store's answer, so the
window can never write past the CLI's checks.

[`brain ui`](cli.md#brain-ui) opens the window, and starts `brain serve` in the background first when it is not running; its
output goes to the data repo's `cache/serve.log`. There is one window: a new window closes every window open before it.

`brain ui --restart` asks a running `brain serve` to restart, and `brain install` runs it. When a window is open, the
old server hands it on: it writes a fresh launch code for its successor in `cache/serve`, which only your account can
read, and says `moved` with the successor's launch URL on the window's own event stream. From then on it answers
nothing new. It frees its port, lands an answer already in flight and refuses one that comes later, so each answer
lands once. Then it starts its successor itself, from its own environment, on the binary now published in
`cache/publish` beside its own. The successor takes the handed code once as it starts, in place of opening a window.
The window waits until the successor serves its page and goes to the launch URL with `location.replace`, and the page
trades the code as at any launch. So the window you had is the window on the new build, and no second one opens. When
no window was open, or a handed code is more than a minute old, the successor opens a new window. `brain ui --restart`
then says which build the successor runs, and when that is not its own, such as from another worktree, the command
that serves its own instead.

## How a window closes

A window closes itself. brain serve says `close` on the event stream the window holds, the one a theme switch travels
on, once a newer window traded its launch code or before the server stops. A restart says `moved` instead, and the
window moves onto the new build rather than closing. A window that loses its server without that
word, because the server crashed or was killed, closes itself as well. The browser allows it because the launch file
opens the page with `location.replace`, so the window has one history entry. Nothing kills a browser process: every
brain window shares one, under brain's own profile. A window an alarm shut stays open to say so.

## The browser

The window is a Chromium-family browser in app mode, `--app`, with the window class `brain` and its own profile under
`cache/window`. `BRAIN_BROWSER` names the browser. Otherwise brain takes the first it finds on PATH of chromium,
chromium-browser, google-chrome-stable, google-chrome, brave-browser, brave, microsoft-edge-stable, microsoft-edge and
vivaldi, and on macOS then looks in /Applications for Chromium, Google Chrome, Brave Browser and Microsoft Edge.

Bind `brain ui` to a key of your own. On Hyprland:

```
bind = SUPER SHIFT, L, exec, brain ui
```

## The perf log

The window records how it runs, on every view, to the data repo's `perf/`, apart from the usage log: one CSV row per
second with the view, what moved, the frame times, the dropped and long frames, Home's paint time, the display frames
Home's sparks painted, the motion settings on, and the GPU's VRAM used and load. brain serve reads the GPU from one
`nvidia-smi` it keeps running at nice 19, pinned to CPUs 16 to 31 where the machine has them; without `nvidia-smi` those
columns are empty. A file never grows past 1 MB, `perf/.gitignore` keeps the folder out of git, and brain serve deletes
files older than 14 days when it starts. F3 shows or hides an overlay with a live graph of the frame times and the same
numbers.

## Node review

The window shows each write waiting for you in full: what the command says it does, then every node it writes, as it
will read, with its subjects and edges, an edit as a diff over the whole file, so every edge it drops shows, and every
node a forget or a merge touches. A node's change wraps at 120 characters.

### The inbox strip

The window filters on two axes, each picked in one place. The inbox is the first. A thin strip down the window's left
edge lists Home, node review, conversations, dreams, Answered, one inbox per source in `sources.tsv`, and Evidence at
its far end, each label turned to read upward, each with how much waits in it. brain code's window draws its strip with
the same component. `Ctrl+P` shows node review, and `H` and `L` step through the inboxes.

The window opens on the last inbox you opened, kept across a reload and a restart like the focus, and on Home when none
is remembered; a write or a question arriving never changes it. Evidence, Settings and the Stylebook sit at the strip's
far end, under Ask again.

### The project focus

The project is the second axis: one focus for the whole window, kept across a reload. The selector at the top of the
queue shows it. Clicking it or `Alt+P` opens a picker with fuzzy search over all, personal, global and every project in
`projects.tsv`, one row each: its name, the path of its graph on disk, how many node reviews wait for it and how many
disputes, the questions in the sources' inboxes. All shows the totals and no path, and the row of the focus carries the
accent bar. A project with nothing waiting can be picked too. The list scrolls when it is longer than the window, and
the arrow keys move the cursor through it. `Enter` applies it.

Focused on a project, every inbox, every list, the strip's counts and the "Waiting for you" count show only that
project's work, and nothing of another project shows anywhere. The project in focus filters every inbox but Home, which
shows every graph, and Evidence, since the usage log keeps no project. A write belongs to each graph its changes land
in, not to the session that staged it: a personal note staged from a project session waits under personal. A source's
question belongs to the project of its repo. In the queue each write carries a chip per graph it lands in, a project in
its own color, personal and global plain. The focus changes only what the window lists: every answer still names the
one write in view.

A project's hue comes from its name alone, so no other project moves it and it is the same on every machine. The theme
sets saturation and lightness so that every hue reads at 4.5:1 on the window's backgrounds and on a highlighted row.

### The reading pane

Beside the write a reading pane searches the graph with lookup's ranking (see [search.md](search.md)), each hit with a
highlighted snippet, and keeps a stack of nodes, pinned or not. While no search is typed, the pane shows the entries
nearest to the write in view, up to 10 per node it writes, under "Near this write" and the node's ref, from the query
`brain nearest` asks. Each entry carries its scope as a chip, a project in its own color. Typing a search replaces
them, and clearing it brings them back.

Clicking an entry, or `Enter` on it, opens it beside that node of the write, as two documents, not a diff, and `Esc`
closes it. The write stays in view and answers as before: nothing near a write changes what a yes lands. The judge
refused a duplicate or a contradiction before the write was staged; the window labels none, and you judge by reading.

### Keys

`Ctrl+K` opens the commands, each with its key:

| Key | What it does |
|---|---|
| `Y` | yes |
| `N` | no |
| `R` | no, with what to change |
| `J`, `K` | the next and previous write |
| `D` | the raw file or the reading |
| `/` | search |
| `Enter` | open a hit |
| `Shift+Enter` | keep a hit in the pane |
| `Ctrl+P` | back to the writes |

## Answered

Answered lists every write you answered in the last 2 days, the newest first, each with its answer and how long ago,
under the same project focus and the same chips as node review. A write opens as it was asked: what the command said it
does, every node in full, an edit as a diff and each node a forget removed, with the answer, your own words and the
time. `U` or the undo button undoes the answer, and `J` and `K` step through the list. A yes whose nodes changed since
shows each one as a diff from the text the yes left to the text now, and its undo refuses. What an undo does is in
[the-yes.md](the-yes.md#answered-and-undo).

## Home

Home shows the constellation: every node and every edge of personal, global and every cloned project in
`projects.tsv`, as one sky. The project focus does not filter it.

### How the sky is laid out

Personal is the core and global the ring around it. The projects lie around the ring, the largest placed first on a
golden-angle spread, a smaller one fitting between larger ones, and projects that share edges side by side so few edges
cross the middle. A project's place can shift when the sizes or the edges between projects change.

Each cluster grows from the middle out. Nodes are placed in the order their files first entered git, beside the older
neighbours of the same graph they entered with or at a spot their ref hashes to, each keeping its room from every node
placed before it, so the same graph draws the same cluster and a new node never moves an older one. A node that entered
with more neighbours settles nearer its cluster's heart, and one that entered with none leans toward the rim. A
cluster's first 64 nodes keep twice the room, so a small cluster reads star by star at the whole sky. Each cluster
swells from its middle out by a stretch that depends on a spot alone and only pushes nodes apart, so a large cluster
reads like a galaxy: a dense core and a sparse halo.

A node's neighbours are the edges its file held in the first commit in which it reads as a node, the migrate commit
for a node added in the old format, so a link made later moves no node and is only drawn. A node not committed yet
comes after every committed one, with the edges it holds now. Global grows in its own ring around the room of an empty
core, and the whole ring is pushed straight out a gap past the core's farthest node, so a new personal node moves every
global node out by one distance and turns none, and a global node moves no personal one. The order and the entry edges
are cached in `cache/sky`, and a new commit costs only its own walk.

### How it is drawn

Each cluster's name carries its node count and lies above the stars the zoom shows of it, rising smoothly as a halo
fades in. Information is a round star, an idea a four-pointed one, a node with only a title is hollow, and a node with
no edge wears a dotted ring. Each star sits on a disc of the sky's own color and grows with its edges. At the whole sky
a large cluster shows its glow, its edges faintly and its most connected stars, and the rest fade in as the view zooms
in; a small cluster shows every star at every zoom, and a lit or hovered star always shows.

A star's title lies right, left, above or below it or across a corner, wherever its box covers no star, no other title
and no cluster name. The selected node, its neighbours, the hovered node and the search hits are placed first and push
the rest aside. One of them with no free spot tries a shorter title before it is left out, and the selected node's
title is never left out, taking the spot that covers the fewest stars. The rest follow region by region of the screen,
the most connected of each region first, more of them as the view zooms in.

On a dark theme one still image lies behind the sky, a few faint stars and a soft nebula tied to no cluster:
`web/backdrop.png`, built into the binary and never repainted, so a file swapped in before a build changes it. The
starry background setting turns it off. Each cluster lies on a faint glow in its color, so personal reads as the core
and global as a ring around it; global's glow starts at the ring's inner edge, so the gap between them stays dark. On a
dark theme a star with 6 or more edges wears a soft, still halo in its cluster's color, brighter and wider with each
edge up to 40, and a star with fewer has none; the glow setting turns them off. Edges are quiet gray lines beneath the
stars, as thick as their weight, and light in their clusters' colors when hovered or when their node is selected.

### Light

Home teaches through light and never moves the constellation. An edge last travelled today carries a slow spark in its
cluster's color now and then, each edge at its own moment, on a canvas of its own. A running spark repaints only the
small box around it, and between runs nothing ticks; where a spark cannot run, with motion off, it only shows brighter.
The sparks are the only light that moves. A node born today shines a little brighter, with a soft light in its
cluster's color around it, and an edge [`brain faded`](cli.md#brain-faded) lists is drawn much fainter. Each has its own
setting: sparks, new stars and fading edges, on by default.

### Hover, select, zoom

Hovering a node shows its title, kind, scope, body and why, and holding `Shift` pins that panel so the pointer can move
into it and scroll it. Hovering an edge shows its relation and weight. A click selects a node, lighting it, its edges
and its neighbours while the rest dim, and a second click opens it in the main pane as a search hit opens, `Esc`
closing it. Scroll zooms, a drag pans and `Alt+R` shows the whole sky again; nothing can be dragged.

While the view moves nothing is painted: the finished sky, painted a quarter of the view past each edge, is moved and
scaled by a CSS transform, and once the view settles it is painted sharp. A pan or a zoom out that runs past that
quarter paints again, at most every 200 ms. With motion on, the view glides instead of jumping: `Alt+R` eases back to
the whole sky, a scroll zoom eases around the pointer, a pan let go while moving coasts to a stop, and any new input
takes over at once. The glide setting, on by default, turns it off, and it needs motion. Cluster names are set like a
star atlas, in small capitals with wide letter spacing and a quiet count beside them; the atlas setting, on by default,
turns that off.

### Search and the bar

On Home, `/` searches every graph with lookup's ranking, and every sure or likely node it finds, not only lookup's top
10, brightens and grows while the rest dim. Home reads the sky again each time it is shown, each time a write lands and
each time a node file of any graph changes, whatever changed it, keeping the selection, and a theme switch repaints it
at once. Looking is reading: nothing on Home travels an edge.

A bar along Home's bottom lists the projects with something waiting, node reviews or disputes, each with its counts. A
project with nothing waiting is not listed, and with nothing waiting anywhere the bar is empty. Clicking a project in it
focuses the window on that project.

## The settings page

``Alt+` `` opens the settings page from anywhere in the window, and `Esc` closes it. It names the model and effort of
each agent, of the judge, of each of the dreamer's two calls and of the deep dream, the model and effort a new chat
starts with and the ones a new sitting starts with, and whether Home draws its starry background and its motion. Under
Search it names the embedding model search ranks by meaning with, and lists every model brain ships.

`j` and `k` move, `gg` and `G` jump to the first and the last, `Enter` changes the setting in focus, and `h` and `l`
move between the dreamer's two models. A model setting opens the model selector, a small modal with the models in one
list and the efforts in the other: `j` and `k` move within a list, `h`, `l` or `Tab` move between them, `Enter` chooses
both and `Esc` closes it without a change.

Every setting names one of the efforts a sitting runs at: low, medium, high, xhigh or max, and the page shows the one
that runs. A `settings.json` written before that may name the effort `default`, which reads as medium.

### Switching the embedding model

The embedding model is a switch, not a choice saved as it is picked. Picking another model first says what the switch
costs: the download, about the profile's megabytes, when the model's files are not on disk, how many nodes it embeds
again, every embedding generated again, and that it takes CPU. `Enter` starts it and `Esc` changes nothing.

brain serve then downloads the model through the same pinned SHA-256 download as `brain install`, and embeds every node
of every graph Home shows with it, on a task inside brain serve's process and the embedder's two threads; the row shows
a progress bar for each step. The new vectors are written beside the old ones, and search, lookup and the nearest
entries stay on the old model and its bars until every node has a new vector. Only then does the setting name the new
model and commit it like any other; search moves to it and the old model's vectors are deleted, so switching back
embeds every node again.

A failed download, a model that does not answer or brain serve stopping leaves the old model in effect and the setting
as it was. The vectors a stopped switch wrote stay in the cache, and the next switch to that model starts from them.
Only nodes are embedded, not edges. The settings call refuses the embedding model's key, so nothing but a finished
switch changes it.

### How settings are saved, and the defaults

Each change is saved as it is made: brain serve writes `settings.json` at the data repo's root and commits it with
nothing else staged, so a fresh install with one pull has the same settings. `settings.json` is no graph node and
passes no gate. A missing file or key is the default:

| Setting | Default |
|---|---|
| builder, easy task | Sonnet, medium |
| builder, difficult task | Opus, medium |
| validator | Opus, medium |
| readiness check | Sonnet, medium |
| judge | Sonnet, medium |
| dreamer | Haiku and Sonnet, medium |
| deep dream | Opus, medium |
| chat | Sonnet, medium |
| sitting | Sonnet, low |
| starry background, motion, glow, sparks, new stars, fading edges, glide, atlas names | on |
| search | `EmbedderProfiles.Default` |

Medium is the model's own default effort. Gemma4 and GLM-5.3-flash are listed for the judge and the dreamer and cannot
be picked yet. The deep dream is the open agent, so it offers Claude's models only.

Home's starry background, a new chat, a new sitting, the judge, the dreamer, the deep dream and the agents follow the
settings now, each reading them when it starts or asks. [`brain context`](cli.md#brain-context) prints an `agents` line with
each agent's model and effort, and the open agent passes them with the Agent tool's `model` and `effort` when it sends
a builder, a validator or the readiness check. Motion is saved, and nothing reads it yet.

A `settings.json` that cannot be read, or that names an embedding model this brain does not ship, stops no reading:
every command runs without an embedding model, so lookup and the window rank by keywords and say why in one line. It
leaves the judge unreachable, so every write is refused until it is fixed.

## The chat

`Alt+A` summons the chat of the tab in view: the open agent, the same one a sitting runs, to get knowledge out of the
graph now. Every tab in the strip holds at most one chat, and several tabs' chats live at once: switching tabs ends
none, so you can answer in node review the writes a chat staged and come back.

A chat starts with the context of the page it is summoned on, what was in view when you pressed the key. Selecting
something else afterwards changes nothing, so close the chat and summon a new one.

| Summoned on | Its context |
|---|---|
| Home | the selected node, or the node pinned with Shift; with none, no context |
| an opened node, from a search result or a double click on Home | that node |
| node review | the waiting write in full and, for an edit, its diff; ask the chat to change it and it stages a new write, which you answer like any other |
| a source's inbox | the open problem as its generic blocks, which the chat may explain from every side while the judgment stays your click |
| Navarone | the escalation open, or the problem's story with the code the lens shows |
| Evidence | the lines unfolded or the details page; with none open, no context |
| Dreams | the open dream; with none open, no context |

A chat does not write `settled.md`. Conversations have their own agent, so `Alt+A` does nothing there.

A picker offers a sitting's models and efforts, Sonnet at medium effort unless picked, and the chat lies over the main
pane's right edge, over whatever tab is in view. `Ctrl+Enter` sends, `Esc` leaves the box, and Close ends the chat. The
agent starts on the first words, in the data repo, with the command a sitting's agent runs, and its answer streams in as
a sitting's does.

A chat keeps nothing: brain serve holds what was said only while it is open and writes no folder, no trail and no entry
in `cache/conversations.json`, and the window keeps no list of past chats. A chat lives until you close it or brain
serve stops; a new window shows the chats brain serve holds. Closing a chat with nothing running under it is immediate.
While an agent it sent or a process it started still runs, Close names each one, and whether it holds a build slot, and
stops them only on "Stop them and close". This is the same stop confirm brain code uses (see
[brain-code.md](brain-code.md)). A write the agent stages waits for your yes in node review like any other, and the
graph door refuses a session that names the chat calls or the registry, so a chat never answers for you.

## The registry and the keeper

brain serve keeps a registry of every agent it runs, in its memory only and never on disk, by tab: each tab's chat,
each conversation's live sitting and each model a running dream asks, with its model, effort and start, and what runs
under it: the agents it sent, from Claude Code's task events, and the processes it started. The window asks for it
every 3 seconds and marks each tab with a live agent `live` in the strip, as an inbox shows its count.

Every `claude` brain starts, an open agent or an ask such as the judge's or a dream's, goes through one door, which
starts it under `brain keep-agent`, a keeper that brain runs and you never do. Once claude ends, once brain stops it,
or once the brain process that started it is gone, a clean stop, a kill or a crash alike, the keeper ends what it
keeps: every process below it, every process it once saw below it that still runs as that process, and every process
left in its process group.

On Linux the keeper is the subreaper of everything below it, so a process the agent starts stays below it even in the
background or detached from its parent, and nothing a chat, a sitting, a dream or a CLI command's ask started outlives
brain serve or that command. Only a process another manager starts on the agent's request, such as systemd through
`systemd-run`, was never below the keeper and is not its to end. `./install.sh` restarting the window ends every chat.

macOS has no subreaper: an orphan goes to launchd and is no longer below the keeper. The keeper gets as close as it
can. It leads a process group of its own, which claude and everything claude starts join, and it reads the process
table with `ps` every second and remembers every process it saw below it, by pid and start, so a later process given
the same pid is left alone. A process still escapes when, within one second of its start, its parent ends and it moves
to a process group of its own, as a `setsid` and a double fork do. A process launchd starts on the agent's request,
such as through `open` or `launchctl`, was never below the keeper and escapes too. macOS reads no open files from
`/proc`, so there the window names no build slot.

## The Omarchy theme

On Omarchy the window wears the active Omarchy theme and follows a theme switch live, without a reload. `brain serve`
reads the palette from `~/.local/state/omarchy/current/theme/colors.toml`, through `omarchy-theme-color` when it is on
PATH, and watches `theme.name` beside it. It serves the palette as `/theme.css` over the window's own colors, with
Omarchy's look: the system monospace for everything but the reading text, square corners and no shadows.

Text that does not reach 4.5:1 on the theme's background, and a red and a green too alike to tell a yes from a no, keep
brain's own colors. Project chips take the mean saturation and lightness of the theme's red, green and accent, at least
25% saturated, with the lightness moved until every hue reads. A yes, a no, an add and a remove each carry a `+` or a
`−` or a strike-through, never color alone. brain writes nothing into Omarchy. Without Omarchy `/theme.css` is empty
and brain's own light and dark theme applies.

## The Stylebook and the page's parts

The Stylebook shows every token of `tokens.css`, the colors, text sizes, radii and space, and each named component in
its states: the button, the chip, the segmented switch, the row, a tab of the strip, current or pressed, a workspace's
tree row and git file row among its variants, the pane head, the icon button, the link, the fold, the field, the code
mark, a workspace's git change in the tree, in code and in markdown, and the palette, which is brain's command palette
and a workspace's bar alike, of `components.css`; and the select, the toggle, the field row, the option list and the
model selector, each drawn by its own sheet in brain's `web/components`.

Both shared sheets live in `src/Brain/Serving/shared`, which brain's window, brain code's and every workspace serve
alike, with lit-html, mermaid, the markdown, the diagrams, the probe and the quiet page. Settings is a page in
`web/pages`, an ES module the browser loads with no build step, made of components in `web/components`, each a script
with its sheet beside it. The gate holds the style to the tokens and the components (see [gates.md](gates.md)).

## Mermaid

The window draws mermaid with Mermaid 11.15.0, which brain serves from its own build as `/mermaid.js` and the page loads
only once a diagram is in view. The page's policy allows no inline style, so the window parses each drawn diagram apart
from the page, turns its stylesheet into a constructed one and sets each element's style through the DOM.

## Related

- [the-yes.md](the-yes.md): what your answer does, and who can answer.
- [sources.md](sources.md): other tools' inboxes and the Navarone tab.
- [conversations.md](conversations.md): the conversations and dreams inboxes.
- [costs.md](costs.md): the Evidence tab.
- [brain-code.md](brain-code.md): brain's other window, and [workspace.md](workspace.md): the workspace it shows.
