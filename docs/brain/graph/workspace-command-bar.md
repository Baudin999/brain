---
kind: idea
subjects: [search]
named-by: user
born: 2026-11-04
session: 812f9ff1-d454-4044-831e-354cf3c8e7cb
---
# `/` opens one bar, like the macOS command bar, that finds files and commands both

One key to remember: opening a file is itself a command, so files and commands share one bar. While typing markdown, `/` is a character; `Esc` leaves typing and `/` opens the bar again.

Commands rank above files when a word in the command's title starts with what was typed, and among those, a command whose key works where the bar was opened comes first. Otherwise files rank as before. Typing "git" gives the git view first, "help" gives the help page above `Help.cs`, and "test" still finds the test files. A loose match in a long command title never beats a file spelled exactly.

## Edges
- part-of -> brain-workspace
- refines -> workspace-commands
