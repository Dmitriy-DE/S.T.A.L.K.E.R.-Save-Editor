# CS-6: local X-Ray editing

The Avalonia desktop window accepts an explicit money value and confirmed
ammo-stack quantities for X-Ray releases with writable `edit_money` and
`edit_stacks` capabilities. It prepares both edits through the shared Core
writers. S.T.A.L.K.E.R. 2 stays read-only until its corresponding writers are
available.

Saving replaces only the selected local file. Before replacement, the editor
checks the source SHA again, creates an `_ORIGINAL.sav` backup and an
`_EDITED.sav` recovery copy beside the selected file, and writes a journal
with `prepared` status. It checks the source SHA once more immediately before
atomic replacement, rereads the file, checks the output SHA and requested
fields, then changes the journal status to `verified`. The save list skips the
two recovery artifacts.

If creation of the original backup fails, replacement is not attempted. If
verification fails after replacement, the exception reports the sibling
backup, recovery copy, and journal; the journal remains `prepared`. Tests use
synthetic X-Ray and S2 fixtures. No live game or Steam session is involved.
