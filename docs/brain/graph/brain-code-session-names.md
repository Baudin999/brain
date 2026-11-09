---
kind: information
subjects: [brain-code, session-name, rename]
named-by: user
born: 2026-11-09
session: f0afbefd-49fe-4eba-be06-11e322bb6cb9
---
# A brain code session is headed by its name, or else by the first words of the user's first prompt

Each row in brain code's session list is headed by the session's name, the title Claude Code's `/rename` keeps in the transcript. Without a name, the first words of the user's first prompt head it. Hovering a row shows that first prompt in full. Double-click or F2 renames the session on its row, and the name reaches the session as `/rename`, so `claude --resume` and a session lifted from a terminal carry it too.

A name is trimmed, and then holds only letters and digits: no spaces, no strange characters, no line breaks. No two sessions in the window carry the same name. An invalid name is refused in the field and at the server, and nothing is sent.

## Why
It was hard to keep the agents clear, which one does what: several sessions of one agent in one folder looked identical. A name the user picks, or else his own first words, tells them apart. Refusing duplicates keeps that true.

## Edges
- part-of -> brain-code
- refines -> brain-code-several-sessions
- respects -> brain-code-lift-and-shift
