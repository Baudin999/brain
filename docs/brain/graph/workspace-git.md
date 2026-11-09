---
kind: idea
subjects: []
named-by: user
born: 2026-11-04
session: 812f9ff1-d454-4044-831e-354cf3c8e7cb
---
# The workspace shows what changed in git by colour, and git is a view of its own that fills the window

Changed means uncommitted against HEAD: staged and unstaged together, and untracked files. The tree colours each changed file by its state. In a file, the changed blocks are coloured: not only a bar in the margin, but the background of the text itself, in code and in rendered markdown alike, where a changed paragraph or block is coloured whole. In the user's words: "This is visual! We do not have syntax highlighting like other IDEs, so these visual cues are different."

Git is not a side bar. In the user's words: "Working with git is not 'just a side bar' it is an integral part of the experience." `Ctrl+g` replaces the whole viewport with the git view, the tree included, laid out as the user drew it:

```
| unstaged | toggle | staged | diff reading area | remote area |
| commit message + buttons   |                   |             |
```

Staging is moving a file between two lists: left is unstaged, right is staged. The diff is a real diff in the file: removed lines red, added lines green, inline among the unchanged lines. The remote area shows the branch and its upstream, the commits a push would send, those behind, and push. Push sends only the current branch to its upstream and never forces. Pull and fetch wait for their own design, since a pull can conflict. There is no staging of single hunks: the user no longer writes code, and whole files are enough.

A commit takes only what is staged. The workspace "should be ready for mono repos which can get huge", so status, diff and commit stay scoped to the workspace's root, even when it is a subfolder of its repo.

Added, changed, deleted and untracked are tokens in the shared stylebook. A branch against main belongs with worktrees, not here.

## Edges
- part-of -> brain-workspace
- refines -> workspace-reading
