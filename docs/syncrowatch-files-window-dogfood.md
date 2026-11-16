# Syncrowatch Files Window Dogfood Checklist

Dogfood pass for the Files window feature, mapped to its spec acceptance criteria.
The automated part ran VM-level against a real scratch git repository (no GUI) on
2026-11-16; the GUI steps stay with the owner. Total owner time: about 10 minutes.

## Automated verification (VM level, real git, 2026-11-16)

Run against a scratch repo seeded with two commits by two identities:
`8e72f112` "add docs and notes" by Olga Ivanova <olga@remote.example> (stands in for
a pulled remote commit) and `4c51b0a2` "extend notes and add app" by the local
identity Test Driver. Tracked files: `docs/read me.md`, `notes.txt`, `src/app.cs`.

1. File list matches `git ls-files` byte-for-byte.
   Observed: VM list `[docs/read me.md, notes.txt, src/app.cs]` == `git ls-files -z` split.
2. Status glances from real projections.
   Observed, everything pushed: `docs/read me.md:clean`, `notes.txt:clean`,
   `src/app.cs:pending batch`. Observed, last push one commit behind: `notes.txt:unpushed`,
   `src/app.cs:unpushed`, `docs/read me.md:clean`.
3. Filter narrows the list case-insensitively.
   Observed: `SRC` → `[src/app.cs]`; no-match filter → empty list.
4. Timeline shows both authors for `notes.txt`.
   Observed: authors `[Test Driver, Olga Ivanova]`, subjects `[extend notes and add app,
   add docs and notes]`, short shas `[4c51b0a, 8e72f11]`.
5. Copy link builds a sha-pinned, credential-free URL with toast confirmation.
   Observed: `https://github.com/acme/repo/blob/4c51b0a2e5b26381822ee06074d17d7748ed2d44/docs/read%20me.md`
   from the credentialed remote `https://token123@github.com/acme/repo.git` — no token in
   the clipboard; toast `LinkCopied` "Link copied".
6. Copy link while a push is pending warns instead of staying silent.
   Observed: `PushPending` true; toast `LinkPending` "Link copied — not pushed yet" /
   "notes.txt may show a 404 until the push lands"; the URL still lands on the clipboard.
7. Non-github remote disables the action with a hint.
   Observed with `https://gitlab.com/acme/repo.git`: `CanCopyLink` false, hint
   "copy link needs a github.com https remote", no clipboard write, no toast.
8. Hostile shapes on the git seam (permanent tests in `GitLocalFileSeamTests.cs`):
   `ChangedPaths(A, B)` vs `(B, A)` on a rename return `[renamed.txt]` vs `[origin.txt]`
   (argument order pinned); paths with spaces and Cyrillic names flow through
   `ListTrackedFiles` / `LogPath` / `ChangedPaths` intact; a tracked-path commit with an
   empty subject never surfaces in the timeline (parser drops the malformed record —
   degraded-but-safe posture, now pinned).
9. Entry-point holder reuse: `FilesWindowViewTests` proves a second `Show` reuses the
   same window instance and a closed window yields a fresh one.

Suite evidence: `dotnet build Synchro.slnx` green (0 warnings); `dotnet test` green —
`Syncrowatch.Tests` 587/587 (583 + 4 new hostile-shape cases), `Synchro.Tests` 100/100.

## Owner manual steps (GUI — cannot run headless)

1. Build and run the app against a real bound folder with a github.com https remote:
   `dotnet run --project src/Syncrowatch.App`.
   Expected: tray icon appears, sync settles without errors.
2. Click the tray icon, pick "Open files".
   Expected: the Files window opens showing the real repo's tracked files.
3. Open the History window, use the "Open files" affordance there.
   Expected: the same Files window instance comes to front (no second window).
4. Select a committed file, click "Copy link", paste into a browser.
   Expected: github.com opens the file pinned at the current HEAD sha; the link
   requires repo access (ссылка для своих); no PAT anywhere in the URL.
5. Optional repeat with a staged push: commit locally without pushing, copy a link.
   Expected: the "not pushed yet" warning toast appears alongside the copy.

## Exit criteria

- Every automated item above recorded its observed values (task log, 2026-11-16).
- Every owner step ends with its expected result before the feature closes.
