---
kind: information
subjects: []
named-by: user
born: 2026-11-02
session: 84ba6677-c4af-44cf-9737-601ab42eaa05
---
# On Arch Linux brain installs from the AUR, with its binary in /usr/bin and its harness in /usr/share/brain/harness

The AUR package puts `brain` in `/usr/bin` and the harness in `/usr/share/brain/harness`, owned by root, and never touches a home directory. After `pacman` you run `brain install`. Before removing the package you run `brain uninstall`, because pacman's remove hook, `pre_remove`, runs once as root for the whole machine and has no business in each user's ~/.claude.

## Why
`/usr/share` is where a package's own files belong on Linux, so pacman owns the harness and an upgrade replaces it while the links stay valid. Root ownership means a user cannot edit the installed harness by accident.

## Edges
- part-of -> install
- runs-on -> linux
