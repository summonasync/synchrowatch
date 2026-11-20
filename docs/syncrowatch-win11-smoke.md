# Syncrowatch Win11 Smoke Checklist

Manual smoke pass for the Windows 11 machine, mapped to the Syncrowatch MVP spec acceptance criteria.
Run after every release build from Ubuntu. Total time: about 30 minutes.

## Prerequisites

- `dist/syncrowatch-<version>/win-x64/syncrowatch.exe` copied from the Ubuntu release build (no installer).
- A private GitHub repository with an ACL-scoped PAT (repo read/write).
- Two folders standing in for two machines (or two real machines bound to the same repo).
- Git for Windows installed (`git` resolvable on PATH).
- Evidence capture: a text file open for the before/after `git config --global --list` output.

## 1. Zero-install launch

1. Copy `syncrowatch.exe` to a fresh folder, run it by double-click.
   Expected: tray icon appears; no runtime, no installer, no missing-DLL errors.

## 2. Wizard E2E with a real PAT (spec AC: setup wizard)

1. Before starting the wizard, run `git config --global --list` and paste the output into the evidence file.
2. Open the wizard from the tray menu, paste the PAT.
   Expected: token accepted, step advances, credential is stored app-side.
3. Enter the private repo URL, click validate.
   Expected: validation passes and shows the default branch.
4. Pick or create an empty folder, keep auto mode, accept the default commit identity (Syncrowatch / syncrowatch@users.noreply.github.com), finish the binding.
5. Run `git config --global --list` again and compare with step 1.
   Expected: byte-identical output; nothing about `user.name`, `user.email` or credentials was added globally.
6. In the bound folder run `git config user.name` and `git config user.email`.
   Expected: the dedicated Syncrowatch identity from repo-local config, not the user's personal git identity.
7. Check the credential file permissions: `%APPDATA%\syncrowatch\github.pat`.
   Expected: the file inherits no broad access — open Properties > Security; only the current user (and SYSTEM/Administrators via inheritance from the user profile root) is listed. In a terminal: `icacls "%APPDATA%\syncrowatch\github.pat"` must not show `Everyone` or `Users`.

## 3. Public repo refusal (spec AC: hard refuse)

1. In the wizard enter the URL of any public GitHub repo and validate.
   Expected: hard refuse with a "publicly readable" explanation; the wizard stays on the repo step; no folder binding happens.

## 4. Auto mode: rapid edits are one commit (spec AC: batch)

1. In the bound folder create 5 small text files in quick succession (under 15 seconds).
2. Wait for the debounce window (default 20 s) plus one pull cycle, then check the repo on github.com.
   Expected: exactly one commit with a `sync:` message; the batch landed on GitHub (files visible on the default branch).

## 5. Two-sided sync cycle (spec AC: auto-merge convergence)

1. Bind a second folder (machine B stand-in) to the same repo with a different editable region of a shared text file.
2. Edit the first third of the file in folder A; wait for the cycle; edit the last third in folder B; wait again; sync folder A once more.
   Expected: both folders converge to the same combined content; folder B shows an auto-merge toast; the history window lists a merge event.

## 6. Conflict keep-both + resolution dialogue (spec AC: zero data loss)

1. Make both folders edit the same line of the same text file without syncing in between; let folder B hit the conflict first.
   Expected: conflict notification; the file keeps B's content in place; a dated `name (theirs ...)` copy appears next to it with A's content.
2. Open the resolution dialogue from the history window and pick keep copy.
   Expected: B's content stays at the canonical path and the dated copy survives on disk.
3. Produce a binary divergence (edit a `.png` on both sides) and let it conflict.
   Expected: no auto-merge attempt on the binary; keep-both copies appear; resolving take theirs restores A's exact bytes while `name (mine)` preserves B's bytes.
4. Delete a file in folder A while folder B edits it; sync both sides.
   Expected: delete-vs-edit conflict; the file is never silently destroyed; every path ends with both versions accounted for (tree, dated copy, or git history).
5. After resolving everything, sync both folders once.
   Expected: both folders converge; working trees clean; the history window shows the resolution events.

## 7. Semi-auto detection prompt and sync now (spec AC: semi-auto)

1. Switch the tray mode to semi-auto and make a local edit.
   Expected: within the debounce window (default 20 s) a "Local changes found" dialog appears exactly once with Sync now / Later; the tray tooltip reads "Synchronizing…" only while a sync actually runs.
2. Choose Later and wait out the debounce window.
   Expected: the dialog never re-appears for this batch; the change commits locally, the push stages silently — the tray tooltip shows the push pending note and the repo on github.com does not advance; no confirmation prompt is shown anywhere.
3. Click Sync now in the tray.
   Expected: the tooltip reads "Synchronizing…" while the cycle runs; afterwards the commit lands on github.com, the pending note clears and a "Sync completed" toast appears.
4. Make a second local edit and choose Sync now on the detection dialog.
   Expected: the edit is committed and pushed in one press with no prompt after the dialog itself; a "Sync completed" toast follows if the press actually transferred something (an up-to-date press stays quiet).
5. Switch to manual mode and touch a file.
   Expected: no watcher-driven commits, no pulls, no pushes and no detection dialog until Sync now is clicked explicitly.

## 8. `.syncignore` respected everywhere (spec AC: ignore)

1. In the bound folder create `.syncignore` with `drafts/` and `*.key`, plus `drafts/scratch.txt`, `api.key`, `bin/app.dll`, `.DS_Store` and one plain file.
2. Wait for a full cycle and check github.com.
   Expected: only `.syncignore` and the plain file landed; ignored paths are absent from every commit, including the bootstrap initial commit of a fresh binding.

## 9. Partial-write safety (spec AC: stability re-check)

1. Start a long copy of a large file into the bound folder so the file is still growing when the debounce window closes.
2. Wait for two full cycles.
   Expected: no mid-write commit of that file; after the copy finishes and the next window closes, a later batch carries the final content.

## 10. Relay notification (spec AC: relay handoff)

1. On the other machine (or via git directly), drop a `<project>/<name>.synchr` blob onto the `relay/synchr` branch and push.
2. Wait for the relay poll on the Win11 machine.
   Expected: a relay notification names the project and offers a ready-to-copy Synchro CLI command (`synchro apply "<project>/<name>.synchr" --peer "<identity>"`); the history window lists the seen package; re-polling does not re-notify.

## 11. Notifications + history window (spec AC: notifications)

1. Trigger an auto-merge, a conflict and a relay package in one session.
   Expected: three toast notifications with distinct content; the history window shows commits in/out, the merge, the conflict resolution and the pending/resolved items with timestamps.

## 12. Launch at login (feature 010)

1. Open Settings, tick **start Syncrowatch when I log in**.
   Expected: no error text appears; `reg query HKCU\Software\Microsoft\Windows\CurrentVersion\Run /v Syncrowatch` shows the exe path double-quoted.
2. Untick the checkbox.
   Expected: the Run value is gone (`reg query` reports it cannot find the value); no error text.
3. Tick it again, then sign out and back in (or reboot).
   Expected: syncrowatch starts with the session — the tray appears without a manual launch.
4. Re-run the full setup wizard (token → repo → folder), reopen Settings.
   Expected: the checkbox is still ticked — the wizard never resets the preference.
5. Move `syncrowatch.exe` to a different folder and run it from the new location.
   Expected: the startup re-assert rewrites the Run value to the new path (`reg query` confirms); after a sign-out/in the moved exe launches.

## Exit criteria

- Every numbered step above ends with its expected result.
- `git config --global --list` before/after evidence is unchanged.
- `icacls "%APPDATA%\syncrowatch\github.pat"` grants nothing beyond the current user profile inheritance.
- Launch-at-login steps leave the Run value matching the live exe path (or absent when unticked), and a wizard re-run preserves the flag.
- No data loss observed in any conflict, deletion or partial-write step.
