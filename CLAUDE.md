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
8. **Lifecycle (spec AMENDMENT 2, revised):** sessions are always-on while the app runs — pid files (written by the spawned shells) + `SessionWatchdog` respawn anything dead (general supervisor auto-starts); app exit tree-kills every session (and closes their terminal windows); app restart brings everything back. **Resume = fresh role-command re-entry for ALL roles, never `--continue`**: the general supervisor is stateless across launches by owner directive (memory = its own CLAUDE.md + the channel read as a LOG — closed/failed requests are never auto-retried on boot; `--continue` once re-ran a failed start and duplicated orchestrations).
9. **DND with catch-up:** mute pauses outbound Telegram by FREEZING tailer offsets — unmute (UI, request, or the owner texting anything) delivers all pending traffic in one burst. The general supervisor's "check-in ritual" (summary digest of all orchestrations + pending questions per topic) is protocol.
10. **Telemetry:** `statusline.ps1` doubles as probe — dumps raw statusline JSON to per-session `.usage.json`; UI shows per-member cost/task/worktree(`WORKTREE:` marker)/time-on-task; engine texts usage-limit alerts at 90/95/97/98/99/100% (schema-tolerant parser — NEEDS LIVE VERIFICATION that this Claude Code version exposes limit data). "Show session" foregrounds a terminal by title (sessions spawn one WT window each, `wt -w new`).
    **Usage figures have ONE reader** (`UsageTotals_Reader`): `Build_PerSourceTotals` is the
    per-session lifetime breakdown and `Build_OrchestrationTotals` is that list summed, so the
    cards, the detail window, `/tokens` and `/cost` can never disagree — and every source passes
    exactly once through the respawn accumulator. `/cost` is the money reading (per-session share
    + burn rate, suppressed under 15 min as meaningless); `/tokens` is the token reading.
11. **`/italian` toggles the translation layer** from the phone, and the app's status-bar
    checkbox mirrors it. Unlike 🌙/🔕 (passing state, in-memory) this one is PERSISTED to
    config.json — the provider reloads on the file's write stamp, so there is no in-memory copy to
    keep in step. Use `OrchestratorConfig_Factory.Create_WithItalianLayer` rather than restating
    every field.
12. **Channel headers are AGENT-WRITTEN — treat `[n]` and the timestamp as untrusted input.** Both
    are guesses unless the agent re-read the file: on 2026-08-10 `option-lab-2` carried two `[80]`
    and two `[81]` entries, and a supervisor stamped `2026-08-11 01:34` on an entry written at
    `15:20` the day before. The date field drives "time on task", and a future stamp used to render
    as "on task under a minute" indefinitely — through a SECOND copy of the duration wording in
    `SessionRows_Builder` that lacked the negative guard `SessionDuration_Formatter` always had.
    One implementation now (`Describe_SinceStamp_OrNull`, which returns null for a future stamp
    rather than a confident wrong number), and all five role commands require a fresh read for both
    fields. Never add a second copy of a formatter.
13. **Never compare a stored entry COUNT against a later live-file count.** `Channel_Compactor`
    moves older entries into a sibling `.archive.md`, so a live-file count is not monotonic. This
    silently broke "has the supervisor answered the owner yet": `option-lab-2` compacted 2 minutes
    after a delivery, 18 supervisor entries left the live file, and the pending could never clear —
    the owner was told their message was still waiting long after it was answered, and the
    supervisor was nudged for a failure that never happened. Count through
    `ChannelHistory_Counter.Count_Entries_ByAuthor`, which spans live + archive.
14. **Owner-facing repeats EDIT, they never stack.** The busy-supervisor narration used to send a
    new Telegram message every 3 minutes for as long as a turn ran; it now edits one line that
    counts up, like the turn-ended receipt always has. A repeat that is a notification is a
    waterfall, which is the thing this system exists to prevent.
15. **Alerts the owner cannot act on do not go to Telegram** (owner directive 2026-08-10). The
    PLAN.md ledger-shape complaint ("lines that lump several tasks together") now goes only to the
    supervisor's channel and the log — splitting a task line is the supervisor's job, so texting
    the owner about it was pure noise. Apply the same test to any new alert.

## Resolved Decisions (2026-08-06, owner)

- **UI framework: WPF** ("keep it simple") — `net10.0-windows`. The suite's `LoggingLib` ships a WPF `ListBoxLoggerSimple` control, which the app uses as its live log panel.
- **Coding patterns: follow the suite's `CODING_PATTERNS.md`** (read `CODING_PATTERNS_QUICKREF.md` in the suite repo before writing C# here). `AIOrchestratorCoreLib` = strict (triples, factories, immutability); the WPF app project = UI-relaxed (mutable observable properties allowed), same as the suite's App-project rule. NOTE: this repo has no pre-write hook — compliance is on the author.
- **Suite code reuse — RETIRED for now (2026-08-06 live-fix):** v1 referenced the suite's `LoggingLib` for its `ListBoxLoggerSimple` log panel, but that control is hard-designed light (white root background, pastel per-tag rows baked into its template) and cannot be dark-themed from outside; the app now ships its own dark log view (`Views/LogRowView` + `ActivityLogListBox`). **The repo currently builds standalone — no suite checkout required.** Coding patterns still follow the suite's `CODING_PATTERNS.md`; if suite libs are reused later, reference the MAIN checkout at `..\..\manuelvene90\Da-Vinci-Fintech-Suite` (never a worktree).
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
