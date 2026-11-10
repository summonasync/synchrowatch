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

**Synchro — high-level code sync without git transport.**  
*The sealed package that carries features between machines no repo ever shared.*

`await SummonAsync();` — part of the summoner's kit  
overmind · grimoire · squeezr · **▸ synchro** · mindlash studio

---

## What it is

Synchro synchronizes code bidirectionally between two sides that share no git
and no infrastructure — a dev shop and its client, or two machines behind
air gaps. The unit of sync is a **feature**, not a diff: each side packs its
work into one encrypted, traceless `.synchr` file, a human carries it across,
and the receiving side's AI agent applies it — silently when nothing overlaps,
through a drop / replace / merge dialogue when it does.

No shared remote. No daemon. No traces of the sending machine on the
receiving side.

## How it works

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

## Two modes

| Mode | When | Alignment |
|------|------|-----------|
| **git** | the target directory is a real repository | sha alignment ledger — commits carry the feature semantics, diffs come from git |
| **plain** | just a folder, no repo required | hash-drift snapshot chain — zero drift applies straight, drift escalates |

Both modes run on the same encrypted store and the same package format.

## The zero-trace contract

- every byte written on the receiving machine is encrypted and indistinguishable
  from random data — no archive magic, no headers, no signatures;
- no metadata identifies the sending machine: no hostname, no user, no paths;
- the store hides from the client's `git status` without touching `.gitignore`;
- the tool and the agents never write to the client's git — read-only, always.

## Status

MVP under active development.

| shipped | next |
|---------|------|
| encrypted store + key ritual | apply pipeline (base protocol, drift, silent apply) |
| git-mode pack (manifest drafts) | conflict scene + drop/replace/merge dialogue |
| plain-mode pack + `.synchr` sealing | MCP server for Codex & Claude agents |
| cross-platform build (Ubuntu → linux-x64 + win-x64) | agent skill, E2E suite, release scripts |

## Quick start

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
