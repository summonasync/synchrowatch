```text
      ┌───────┐                            ┌───────┐
      │  dev  │       ▄▄▄▄▄▄▄▄▄▄▄▄▄        │  peer │
      │       ├───────▶│ .synchr │◀───────┤       │
      └───────┘        ▀▀▀▀▀▀▀▀▀▀▀▀▀        └───────┘

███████╗██╗   ██╗███╗   ██╗ ██████╗██╗  ██╗██████╗  ██████╗
██╔════╝╚██╗ ██╔╝████╗  ██║██╔════╝██║  ██║██╔══██╗██╔═══██╗
███████╗ ╚████╔╝ ██╔██╗ ██║██║     ███████║██████╔╝██║   ██║
╚════██║  ╚██╔╝  ██║╚██╗██║██║     ██╔══██║██╔══██╗██║   ██║
███████║   ██║   ██║ ╚████║╚██████╗██║  ██║██║  ██║╚██████╔╝
╚══════╝   ╚═╝   ╚═╝  ╚═══╝ ╚═════╝╚═╝  ╚═╝╚═╝  ╚═╝ ╚═════╝
```

**Synchro — two tools, one seal.**  
*The `synchro` CLI carries features between machines no repo ever shared —
sealed `.synchr` packages carried by hand, or direct repo-to-repo sync over
a local path. The `syncrowatch` tray keeps a folder in step with a private
GitHub repository — full git semantics, none of the git ceremony.*

`await SummonAsync();` — part of the summoner's kit  
overmind · grimoire · squeezr · **▸ synchro** · mindlash studio

---

## Two tools, one seal

Both tools answer the same problem from opposite ends: work has to move
without infrastructure both sides can trust.

Sometimes the transport itself must carry no meaning — a dev shop and its
client share no git, and the only thing allowed across is a file that reads
as random bytes. That is **synchro**: feature-level, bidirectional, sealed.
Sometimes the transport is fine and the ceremony is not — a folder should
sync with a private repository like a repo while the person sees toasts,
not commands. That is **syncrowatch**: a real git engine under the hood,
a tray icon on the surface.

And when a `.synchr` package lands in the repository syncrowatch watches,
it surfaces with the ready-to-run `synchro` command attached. One seal,
two ways in.

## synchro — the CLI

### What it is

Synchro synchronizes code bidirectionally between two sides that share no
git and no infrastructure — a dev shop and its client, or two machines
behind air gaps. The unit of sync is a **feature**, not a diff: each side
packs its work into one encrypted, traceless `.synchr` file, a human
carries it across, and the receiving side's AI agent applies it — silently
when nothing overlaps, through a drop / replace / merge dialogue when it
does. Two repos of one project that *are* reachable by path skip the
package entirely and sync directly, master to slave — see
[two transports](#two-transports).

No shared remote. No daemon. No traces of the sending machine on the
receiving side.

### How it works

1. **`synchro init`** — creates the encrypted local store (`.synchro/`),
   detects the mode, and runs the key ritual: one side generates the channel
   key, the peer adopts it out-of-band. On git repos the store hides itself
   from `git status` via `.git/info/exclude` — `.gitignore` is never touched.
2. **`synchro pack`** — computes changes since the last common point and
   seals them into a single `.synchr` file: AES-GCM, no magic bytes, no
   headers, no machine-identifying metadata — indistinguishable from random
   data.
3. **You carry the file.** Flash drive, mail, carrier pigeon — the transport
   is deliberately human.
4. **`synchro apply`** *(in progress)* — verifies, checks for drift, applies
   non-overlapping features silently and escalates overlapping or
   semantically similar ones to the human: drop, replace, or merge. Merging
   drafts through `git merge-file`; the agent resolves what remains.

### Two transports

**Hand-carry** — `synchro pack` seals a `.synchr` file, a human moves it, `synchro
apply` lands it. For machines that share nothing at all.

**Direct master-slave** — two repos of one project reachable by path (same machine or
a mounted share). One side is the master: it holds the encrypted store, the full sync
history and the per-slave ledger; the slave is a stateless folder. `synchro sync`
exchanges feature deltas bidirectionally; they land uncommitted on a remembered
`feature-sync` branch on the receiving side. No `.synchr` file is produced on this
path — the master's encrypted store is the only artifact Synchro writes here, and the
tool never commits in either repo (one consented exception: a plain folder joining as
a slave gets `git init` plus a single neutral bootstrap commit).

```bash
# in the master repo — once
synchro init
synchro add slave ../client-repo

# every sync — conflicts ask, Enter accepts master-wins; --yes skips the ask
synchro sync [slave] [--yes]
synchro status [slave]                # both inboxes, ledger hint, last operation
synchro log [slave]                   # sync history
synchro rollback [slave] [--master]   # discard an inbox — tree returns to the base branch
```

The human commits the inbox branches when the landed work looks right; `synchro ui`
covers the same flow in a localhost console.

### Two modes

| Mode | When | Alignment |
|------|------|-----------|
| **git** | the target directory is a real repository | sha alignment ledger — commits carry the feature semantics, diffs come from git |
| **plain** | just a folder, no repo required | hash-drift snapshot chain — zero drift applies straight, drift escalates |

Both modes run on the same encrypted store and the same package format.

### The zero-trace contract

- every byte written on the receiving machine is encrypted and indistinguishable
  from random data — no archive magic, no headers, no signatures;
- no metadata identifies the sending machine: no hostname, no user, no paths;
- the store hides from the client's `git status` without touching `.gitignore`;
- the tool and the agents never commit in a peer's git — the only exception is the
  consented bootstrap commit when a plain folder joins as a slave.

### Commands

- `synchro init` — create the encrypted local store and run the key ritual:
  `--peer <name>` generates the channel key, `--key <hex>` adopts the peer's.
  Bare `synchro init` declares the repo the sync **master**.
- `synchro add slave <path>` — register a slave repo for direct sync: path
  validation, sync-branch name, plain folders join via a consented bootstrap.
- `synchro pack` — seal changes since the last common point; `--full` for a
  first-transfer baseline.
- `synchro scene` — the conflict scene: inspect what overlaps, resolve
  drop / replace / merge, commit the outcome.
- `synchro serve` — stdio MCP server; coding agents drive status, scene and
  apply through it.
- `synchro ui` — localhost web console over the same state: dashboard, sync
  history, inboxes and live conflict scenes; free port by default (`--port`
  to override), loopback only.
- `synchro apply` — *(in progress)* the receiving side of the pipeline.
- `synchro sync [<slave>] [--yes]` — direct bidirectional master-slave sync:
  conflicts ask, Enter accepts master-wins; deltas land uncommitted on the
  remembered sync branch.
- `synchro rollback [<slave>] [--master]` — discard a sync inbox, tree back
  to the base branch.
- `synchro status [<slave>]`, `synchro log [<slave>]` — per-slave config,
  inbox state and sync history for the direct path.
- `synchro install` — register the running binary as the `synchro` command:
  per-user copy, PATH registration, upgrade report on re-run (`--path-fix`
  for a missing Linux PATH entry, `--force` to re-point a foreign name).
- `synchro uninstall` — remove the installed binary, the command name and
  its PATH entries; stores and settings stay untouched.

### Quick start

```bash
# side A — generates the channel key (share the hex out-of-band, once)
synchro init --peer <your-name>

# side B — adopts the peer key
synchro init --key <hex> --peer <their-name>

# pack committed work into a sealed package
synchro pack            # delta since the last common point (git-mode)
synchro pack --full     # first-transfer baseline
```

Requires: git installed (repo initialization is *not* required).
Windows and Linux, single self-contained binary.

## syncrowatch — the tray

### What it is

A watched folder bound to a private GitHub repository, kept in sync by a
real git engine. The folder is a genuine repository with a remote; commits
carry a dedicated repo-local identity
(`Syncrowatch <syncrowatch@users.noreply.github.com>`), and the machine's
global git config is never touched. Built on .NET 10 and Avalonia, Fluent
dark remapped onto the brand language — ink, parchment, ember. Windows and
Linux, single self-contained binary, no installer.

### The wizard

First run opens a six-step wizard: token → repository → folder →
preferences → bootstrap → completed. Back navigation moves one step back
with everything entered preserved — the token step re-fills from the
stored credential, shown masked; re-entering replaces it. The bootstrap step handles the
case where both the folder and the repository already hold content:
explicit scenario cards decide whose state wins.

Tray → Settings opens the wizard while nothing is configured yet, and a
read-only preview of every setting (folder, repo, branch, mode, debounce,
poll, identity, masked token) once it is — plus one editable checkbox,
**launch at login**, which registers a per-user autostart entry (Windows
Run key or Linux XDG autostart) and re-asserts its path on every start.

### Three sync modes

- **auto** — changes sync on their own; debounce and poll intervals are
  configurable in preferences.
- **semi-auto** — syncrowatch detects local changes and asks: the detection
  prompt offers *Sync now* or *Later*.
- **manual** — nothing moves until *Sync now* is chosen from the tray menu.

### Tray, windows, toasts

- The tray icon reflects the sync state: idle, syncing, conflict, error,
  pending push — symbolic glyphs on Linux, colored on Windows.
- **History** lists every sync event with filter chips (pulled, pushed,
  merges, conflicts, failures) and a live conflict panel: a
  drop / replace / merge decision happens in place, no CLI roundtrip.
- **Files** lists every tracked file with a status glance and a per-file
  timeline of commits and authors. *Copy link* produces a sha-pinned GitHub
  URL — credential-free, even though the configured remote carries the PAT.
- Toasts cover eight kinds: auto-merged, sync completed, conflict, failure,
  relay package, relay warning, link copied, link pending.
- The relay watcher polls the repository for incoming `.synchr` packages
  and surfaces each one with the ready-to-run `synchro` CLI command.

### Where your data lives

Settings and state sit in a per-user application directory (`syncrowatch/`):
`settings.json`, `preferences.json`, `history.json`, `conflicts.json`. The
PAT stays on the
machine too — a local credential file (`github.pat`), never inside the
bound repository, never in remote URLs. The repository holds only what git
holds: your files and their history.

### Requirements

- git available on PATH;
- a private GitHub repository and a PAT with read/write contents access.

### Screenshots

![Setup wizard, token step](design/impl-smoke/final/01-wizard-token-start.png)

![Files window, empty state](design/impl-smoke/final/03-files-empty.png)

![Semi-auto detection prompt](design/impl-smoke/final/04-detection-prompt.png)

The full evidence set — wizard states, the tray icon matrix, all eight
toast kinds — lives in [`design/impl-smoke/`](design/impl-smoke/).

## Status

MVP under active development.

**synchro (CLI)**

- shipped: encrypted store + key ritual · git-mode pack (manifest drafts) ·
  plain-mode pack + `.synchr` sealing · conflict scene with
  drop / replace / merge · stdio MCP server (`serve`) · local web UI (`ui`) ·
  agent skill, E2E suite, cross-platform release builds · direct master-slave
  sync (`add slave`, `sync`, `rollback`, `status`, `log`) with dirty landing
  on a remembered sync branch
- in progress: `apply` (base protocol, drift, silent apply, conflict
  escalation)

**syncrowatch (tray)**

- shipped: six-step wizard with back navigation · three sync modes ·
  per-state tray icons · history with live conflict resolution · files
  window with per-file history and share links · detection prompt · eight
  toast kinds · settings preview with a launch-at-login checkbox ·
  `.synchr` relay watching

## Install

Prebuilt binaries live on the
[releases page](https://github.com/summonasync/synchro/releases) — the
repository is private, so access is required. All binaries are
self-contained single-file executables: no .NET runtime, no installer.

- **syncrowatch** — latest published binaries on **v0.1.6**: `syncrowatch`
  (linux-x64) and `syncrowatch.exe` (win-x64), shipped with a checksum
  manifest.
- **synchro** — download the release for your OS (`synchro` for linux-x64,
  `synchro.exe` for win-x64) and let the binary install itself.

Install the synchro CLI (Linux):

```bash
chmod +x synchro
./synchro install        # add --path-fix when ~/.local/bin is not on PATH
```

Windows (PowerShell): `.\synchro.exe install`.

`synchro install` copies the binary into the per-user install location and
registers the `synchro` command on PATH — per-user, no admin, no network:

- Linux: a versioned copy under `~/.local/share/synchro/` behind a
  `~/.local/bin/synchro` symlink. When `~/.local/bin` is missing from
  PATH, rerun with `--path-fix` — a marked PATH block lands in
  `~/.profile`. Open a new shell (or re-source `~/.profile`) and
  `synchro` resolves.
- Windows: `%LOCALAPPDATA%\Programs\synchro\synchro.exe`, the user PATH
  updated in the registry. Open a new terminal and `synchro` resolves.
- Upgrade is the same command: run `install` from the newer binary and the
  report shows `previous -> new` versions.
- `synchro install --force` re-points an existing `synchro` name synchro
  did not create — with a warning naming where it pointed.
- `synchro uninstall` removes the binary, the command name and its PATH
  entries. `.synchro` stores and syncrowatch settings are never touched.

Verify the checksum after download — Linux:

```bash
sha256sum syncrowatch
# compare against the linux-x64/syncrowatch line in sha256-0.1.6.txt
chmod +x syncrowatch && ./syncrowatch
```

Windows (PowerShell):

```powershell
Get-FileHash .\syncrowatch.exe -Algorithm SHA256
# compare against the win-x64/syncrowatch.exe line in sha256-0.1.6.txt
.\syncrowatch.exe
```

## Building from source

Requires the .NET 10 SDK and git.

```bash
git clone https://github.com/summonasync/synchro.git
cd synchro
dotnet build Synchro.slnx
dotnet test Synchro.slnx
```

```bash
dotnet run --project src/Synchro.App      # synchro CLI
dotnet run --project src/Syncrowatch.App  # syncrowatch tray
```

Release-style builds, from Ubuntu targeting both desktops:

```bash
scripts/release.sh                    # synchro -> dist/{linux-x64,win-x64} + sha256 manifest
scripts/release-syncrowatch.sh 0.1.7  # syncrowatch -> dist/syncrowatch-0.1.7/{linux-x64,win-x64}
```

## Docs

- [`docs/WIN11-SMOKE.md`](docs/WIN11-SMOKE.md) — synchro manual smoke
  checklist for a clean Windows 11 machine
- [`docs/syncrowatch-win11-smoke.md`](docs/syncrowatch-win11-smoke.md) —
  syncrowatch Windows 11 smoke checklist, mapped to the MVP acceptance
  criteria
- [`docs/syncrowatch-files-window-dogfood.md`](docs/syncrowatch-files-window-dogfood.md) —
  Files window dogfood pass against a real repository
- [`design/impl-smoke/`](design/impl-smoke/) — screenshot evidence: wizard
  states, tray icon matrix, toasts, settings

---

`await SummonAsync();` — part of the summoner's kit  
overmind · grimoire · squeezr · **▸ synchro** · mindlash studio
