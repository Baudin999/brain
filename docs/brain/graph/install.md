---
kind: information
subjects: []
named-by: user
born: 2026-11-02
session: 84ba6677-c4af-44cf-9737-601ab42eaa05
---
# brain install links brain's harness into the Claude config directory, after a guided walk through everything it changes

`brain install` is a guided installer in the terminal, built into the CLI. Everything it puts in ~/.claude is a link, never a copy. A packaged binary links to the harness its package put in `share/brain/harness` beside it. A bare binary has the harness embedded and unpacks it into the data repo's gitignored `cache/harness`, and links to that.

It informs, in this order. First what brain is: that Claude in every project then works as an open agent that talks and sends closed agents to build, and never writes code itself; that it is a beta under development; and what it costs, Opus by default and a Sonnet judge on every graph write. Then each change on disk, shown with its diff before it asks, and a link that changes target says so. Then a very clear: do you want to continue. It asks where to store personal files. Where someone already has a CLAUDE.md or settings, the choice to replace or keep them is made very very clear. It asks which permission mode you want; on a first install it never defaults to auto, and on a reinstall it defaults to the mode you have. Editing an installed harness file is at your own risk, and it says so. `brain uninstall` puts everything back the way it was.

From a checkout, ./install.sh asks first whether you install as a user or for development. As a user it builds the CLI and hands over to `brain install`. For development it runs `link.sh`, which asks which parts of the harness to link to the checkout, all of them by default, and the rest link to the installed harness. ./install.sh goes away once brain ships as a binary.

## Why
Links everywhere make one install path and show at a glance which files in ~/.claude are brain's; copies would drift from the source without anyone seeing. A bare binary unpacks into the data repo's cache because the user wants one brain directory, not two, and nothing generated under source control. The installer lives in the CLI because the CLI knows the harness and can explain it truthfully, where a bash script had to describe behaviour it does not own. The terminal comes first because it works everywhere, including headless machines where brain's window may not open. Brain changes how Claude works in every project, so a stranger who learns that only after installing would feel tricked. Replacing someone's CLAUDE.md or settings without a clear choice, and without a way back, would cost their trust before they ever used brain. ./install.sh asks user or development because a changed install script once turned the user's own links into copies and dropped his mode from auto without him choosing it; an install script must not change behaviour under its user.

## Edges
- refines -> open-source-install
