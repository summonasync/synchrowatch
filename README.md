```text
      ┌────────┐      every change, quietly      ┌───────────────┐
      │ folder │ ─────────────────────────────▶  │ private repo  │
      └────────┘ ◀───────────────────────────────   └───────────────┘

███████╗██╗   ██╗███╗   ██╗ ██████╗██████╗  ██████╗ ██╗    ██╗ █████╗ ████████╗ ██████╗██╗  ██╗
██╔════╝╚██╗ ██╔╝████╗  ██║██╔════╝██╔══██╗██╔═══██╗██║    ██║██╔══██╗╚══██╔══╝██╔════╝██║  ██║
███████╗ ╚████╔╝ ██╔██╗ ██║██║     ██████╔╝██║   ██║██║ █╗ ██║███████║   ██║   ██║     ███████║
╚════██║  ╚██╔╝  ██║╚██╗██║██║     ██╔══██╗██║   ██║██║███╗██║██╔══██║   ██║   ██║     ██╔══██║
███████║   ██║   ██║ ╚████║╚██████╗██║  ██║╚██████╔╝╚███╔███╔╝██║  ██║   ██║   ╚██████╗██║  ██║
╚══════╝   ╚═╝   ╚═╝  ╚═══╝ ╚═════╝╚═╝  ╚═╝ ╚═════╝  ╚══╝╚══╝ ╚═╝  ╚═╝   ╚═╝    ╚═════╝╚═╝  ╚═╝
```

**syncrowatch — a watched folder that keeps itself in a private GitHub
repository.**  
*Full git semantics under the hood, none of the git ceremony on the
surface: the person sees toasts, not commands.*

`await SummonAsync();` — part of the summoner's kit  
overmind · grimoire · squeezr · synchro · **▸ syncrowatch** · summonasync studio

---

## What it is

A watched folder bound to a private GitHub repository, kept in sync by a
real git engine. The folder is a genuine repository with a remote; commits
carry a dedicated repo-local identity
(`Syncrowatch <syncrowatch@users.noreply.github.com>`), and the machine's
global git config is never touched. Built on .NET 10 and Avalonia, Fluent
dark remapped onto the brand language — ink, parchment, ember. Windows and
Linux, single self-contained binary, no installer.

## The wizard

First run opens a six-step wizard: token → repository → folder →
preferences → bootstrap → completed. Back navigation moves one step back
with everything entered preserved — the token step re-fills from the
stored credential, shown masked; re-entering replaces it. The bootstrap
step handles the case where both the folder and the repository already
hold content: explicit scenario cards decide whose state wins.

Tray → Settings opens the wizard while nothing is configured yet, and a
read-only preview of every setting (folder, repo, branch, mode, debounce,
poll, identity, masked token) once it is — plus one editable checkbox,
**launch at login**, which registers a per-user autostart entry (Windows
Run key or Linux XDG autostart) and re-asserts its path on every start.

## Three sync modes

- **auto** — changes sync on their own; debounce and poll intervals are
  configurable in preferences.
- **semi-auto** — syncrowatch detects local changes and asks: the detection
  prompt offers *Sync now* or *Later*.
- **manual** — nothing moves until *Sync now* is chosen from the tray menu.

## Tray, windows, toasts

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

## Where your data lives

Settings and state sit in a per-user application directory (`syncrowatch/`):
`settings.json`, `preferences.json`, `history.json`, `conflicts.json`. The
PAT stays on the machine too — a local credential file (`github.pat`),
never inside the bound repository, never in remote URLs. The repository
holds only what git holds: your files and their history.

## Requirements

- git available on PATH;
- a private GitHub repository and a PAT with read/write contents access.

## Status

MVP under active development.

- shipped: six-step wizard with back navigation · three sync modes ·
  per-state tray icons · history with live conflict resolution · files
  window with per-file history and share links · detection prompt · eight
  toast kinds · settings preview with a launch-at-login checkbox ·
  `.synchr` relay watching

## Building from source

Requires the .NET 10 SDK and git.

```bash
git clone https://github.com/summonasync/synchrowatch.git
cd synchrowatch
dotnet build Syncrowatch.slnx
dotnet test Syncrowatch.slnx
dotnet run --project src/Syncrowatch.App
```

Release-style builds, from Ubuntu targeting both desktops:

```bash
scripts/release-syncrowatch.sh <version>   # -> dist/syncrowatch-<version>/{linux-x64,win-x64}
```

## Docs

- [`docs/syncrowatch-win11-smoke.md`](docs/syncrowatch-win11-smoke.md) —
  Windows 11 smoke checklist, mapped to the MVP acceptance criteria
- [`docs/syncrowatch-files-window-dogfood.md`](docs/syncrowatch-files-window-dogfood.md) —
  Files window dogfood pass against a real repository

## Pairs with synchro

[`synchro`](https://github.com/summonasync/synchro) — sealed feature sync
for machines that share no git. When a sealed `.synchr` package lands in
the watched repository, syncrowatch surfaces it with the ready-to-run
`synchro` command attached. One seal, two ways in.

---

`await SummonAsync();` — part of the summoner's kit  
overmind · grimoire · squeezr · synchro · **▸ syncrowatch** · summonasync studio
