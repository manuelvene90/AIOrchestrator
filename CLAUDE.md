# AI Orchestrator — AI Context

This file is loaded automatically by Claude Code at the start of every session in this repo. Read it fully before doing any work.

## What This Project Is

A portable orchestration kit that generalizes a proven two-agent supervision pattern to N agents, with Telegram as the owner's window into everything.

**The pattern being generalized** (born in the Da-Vinci-Fintech-Suite repo, file `SUPERVISION-CHANNEL.md` on the `portfolio-live-following` worktree — read it for the reference protocol): one SUPERVISOR Claude Code session (expensive model, fresh context, reviews and gates) and one IMPLEMENTER session (cheaper model, writes code) coordinate through an append-only markdown channel file. Each session arms a background file-watcher before ending every turn, so an append by one side wakes the other (duplex, self-waking, no human relay). The owner interfaces only with the supervisor, at high level. It works extremely well; this project productizes it.

**What this repo builds:**
0. **General supervisor** (added 2026-08-06) — an always-on concierge session with its own channel
   (`~/.claude/supervision/general/channel.md`) mirrored to the Telegram supergroup's General topic
   (pinned by the owner). The owner texts it "work on skeleton client" from their phone; it resolves
   the repo and starts/closes whole orchestrations via the request-file protocol below.
1. **Orchestrator app** (this solution) — a desktop control app that owns:
   - The repo list (seeded from the owner's project registry; editable).
   - "Start supervisor" per repo: spawns a Claude Code terminal session (`wt new-tab -d <repo> -- claude "/supervisor <orch-id>"`) and creates the orchestration session.
   - "Add implementer" per orchestration: spawns N implementer terminals, each with its own id (`claude "/implementer <orch-id>/imp-<n>"`).
   - The **Telegram bridge loop** (background, in-process): tails all channel files → mirrors appends to Telegram; long-polls `getUpdates` → routes owner messages into supervisor inbox files.
   - Status: which sessions/orchestrations are alive, last activity per channel.
2. **Claude Code role commands** — `/supervisor <id>` and `/implementer <id>` installed user-level (`~/.claude/commands/`), available in every project. Each loads the full role protocol (channel paths, watcher command, report format, staging discipline).
3. **Installer** — sets up a new machine: prompts for bot token / supergroup chat id / owner user id, writes local config, installs the role commands, seeds the repo list.

## Architecture Decisions (locked so far — brainstorm in progress, spec pending)

1. **Telegram is a MIRROR + owner console, never the agent↔agent transport.** Hard Bot API constraints force this and it is also the better design: bots cannot create group chats (only forum topics), bots never see other bots' messages, and one bot token allows a single concurrent `getUpdates` poller. Agent↔agent traffic stays on local channel files (append-only, auditable, watcher-wakeable — the proven mechanism). The bridge mirrors outbound and injects inbound.
2. **One Telegram forum supergroup, one topic per orchestration session.** The owner creates the supergroup once (Topics enabled, bot as admin); the app creates a topic per orchestration id via `createForumTopic`. Owner messages typed into a topic are inherently supervisor-only, because implementers never read Telegram at all.
3. **Central channel home, outside every git tree:** `~/.claude/supervision/<orch-id>/` containing `session.json` (repo path, member roster + PIDs, telegram topic id), `owner-channel.md` (duplex owner ⇄ supervisor: bridge appends inbound Telegram messages, supervisor appends replies/questions, bridge mirrors the supervisor's entries back to Telegram), `orchestrator.log.jsonl` (structured per-orchestration log), and `imp-<n>/channel.md` (one duplex channel per implementer). This kills the accidental-`git add -A` hazard the in-repo channel file had, and makes bridge discovery trivial (watch one folder; new orch-id folder ⇒ create topic).
4. **Hub-and-spoke topology, one supervisor : N implementers.** Each implementer gets its OWN channel file with the supervisor — an exact copy of the proven 1:1 duplex protocol. Implementers never see each other's traffic. The unified "group conversation" view exists only in the Telegram mirror, where the bridge merges all spokes chronologically tagged `[sup → imp-2]`, `[imp-1 → sup]`, etc.
5. **The app spawns sessions in real terminals** (Windows Terminal `wt new-tab`, fallback `Start-Process`), passing the role slash command as the initial prompt so the session knows its role and id from message one. Mac later via `osascript`; same design, different launcher line.
6. **Portable kit:** everything a new machine needs (app, role commands, config template) installs from this repo (`kit/install.ps1`). Owner uses this across multiple machines.
7. **Request-file protocol (`~/.claude/supervision/.requests/*.json`)** — agents ask, the app executes (~2 s), and confirms with a first-class `FROM app` channel entry that wakes the requester's watcher. Actions: `start-orchestration` (repo only — ids are auto-allocated `repo-slug-n`) + `close-orchestration` (general supervisor), `add-implementer` + `close-implementer` (orchestration supervisors), `set-telegram-muted` (any supervisor — DND). Closing marks `ClosedUtc` in session.json (audit trail kept, tailing stops, UI dims, terminals killed); the orchestration close also closes its Telegram topic. See the spec's AMENDMENT sections.
8. **Lifecycle (spec AMENDMENT 2):** sessions are always-on while the app runs — pid files (written by the spawned shells) + `SessionWatchdog` respawn anything dead (general supervisor auto-starts; resume = `--continue` for general, channel re-read for the rest); app exit tree-kills every session; app restart brings everything back.
9. **DND with catch-up:** mute pauses outbound Telegram by FREEZING tailer offsets — unmute (UI, request, or the owner texting anything) delivers all pending traffic in one burst. The general supervisor's "check-in ritual" (summary digest of all orchestrations + pending questions per topic) is protocol.
10. **Telemetry:** `statusline.ps1` doubles as probe — dumps raw statusline JSON to per-session `.usage.json`; UI shows per-member cost/task/worktree(`WORKTREE:` marker)/time-on-task; engine texts usage-limit alerts at 90/95/97/98/99/100% (schema-tolerant parser — NEEDS LIVE VERIFICATION that this Claude Code version exposes limit data). "Show session" foregrounds a terminal by title (sessions spawn one WT window each, `wt -w new`).

## Resolved Decisions (2026-08-06, owner)

- **UI framework: WPF** ("keep it simple") — `net10.0-windows`. The suite's `LoggingLib` ships a WPF `ListBoxLoggerSimple` control, which the app uses as its live log panel.
- **Coding patterns: follow the suite's `CODING_PATTERNS.md`** (read `CODING_PATTERNS_QUICKREF.md` in the suite repo before writing C# here). `AIOrchestratorCoreLib` = strict (triples, factories, immutability); the WPF app project = UI-relaxed (mutable observable properties allowed), same as the suite's App-project rule. NOTE: this repo has no pre-write hook — compliance is on the author.
- **Suite code reuse:** the WPF app takes `ProjectReference`s into the sibling suite repo (`..\..\manuelvene90\Da-Vinci-Fintech-Suite` relative to this repo; main checkout, NOT a worktree). v1 references `00_Shared/LoggingLib` (+ `ExtensionsAndMethodsLib` transitively). CoreLib stays suite-free so its tests run standalone.
- **Per-orchestration logging + live state view** (owner directive mid-design): every orchestration writes `orchestrator.log.jsonl`; the app shows a live log panel and per-member state chips (implementer working / awaiting review / writing window open / blocked on owner) derived from the channel files.
- **Dual interaction:** Telegram AND direct terminal typing are both first-class; the file protocol works with the app closed.

## Design Spec

**`docs/superpowers/specs/2026-08-06-ai-orchestrator-design.md` is the approved design** — read it before changing architecture. This file stays the quick context; the spec is the authority.

## Repository Structure

```
AIOrchestrator.slnx        ← solution (repo root = solution level)
AIOrchestrator/            ← the desktop app project (currently the raw VS template)
CLAUDE.md                  ← this file
docs/superpowers/specs/    ← design specs (pending)
```

## Conventions

- The repo root is `C:\Users\Gianpiero\source\repos\AIOrchestrator` (solution level), not the inner project folder.
- Machine-local secrets (bot token, chat ids) live in gitignored `*.local.json` / `secrets.json` — never committed, never hard-coded.
- Multi-line git commits via `git commit -F <tempfile>` (Windows PowerShell mangles `-m` with here-strings).
