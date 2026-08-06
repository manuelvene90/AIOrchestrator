# AI Orchestrator — Design Spec

Date: 2026-08-06 · Status: APPROVED by owner (design presented in sections, approved verbally: "Let's go")

## Purpose

Generalize the proven two-agent supervision pattern (one expensive-model SUPERVISOR session reviewing, one cheap-model IMPLEMENTER session coding, coordinating through an append-only markdown duplex channel with self-waking file watchers — born as `SUPERVISION-CHANNEL.md` in the Da-Vinci-Fintech-Suite repo) into a reusable, N-implementer, Telegram-mirrored system that the owner can run on any of their machines.

## Core principle — the file protocol IS the system

Telegram and the WPF app are conveniences layered on top of the file protocol. If the app is not running, sessions still coordinate through channel files and the owner interacts by typing into the supervisor's terminal. Nothing about the protocol depends on the app or on Telegram being alive. Direct terminal interaction and Telegram interaction are both first-class.

## Why Telegram is a mirror, not a transport (locked)

Telegram Bot API hard constraints: (1) bots cannot create group chats, only forum topics inside a supergroup they administer; (2) bots never receive other bots' messages; (3) one bot token supports a single concurrent `getUpdates` poller (409 otherwise). Agent↔agent traffic through Telegram is therefore impossible with one bot and pointless with many. The bridge mirrors channel files outbound and routes owner messages inbound. Owner messages are structurally supervisor-only: implementers never read Telegram.

## Topology (locked)

Hub-and-spoke per orchestration: one supervisor, N implementers, one duplex channel file PER implementer (exact copy of the proven 1:1 protocol). Implementers never see each other's traffic. The supervisor additionally has a duplex channel with the OWNER (`owner-channel.md`): the bridge appends inbound Telegram messages there (waking the supervisor via its watcher), and mirrors the supervisor's entries in it back to Telegram. The unified "group conversation" exists only in the Telegram topic, where all spokes are merged chronologically with direction tags (`[imp-2 → sup]`, `[sup → imp-1]`, `[sup → owner]`).

## File layout (all outside any git tree)

```
~/.claude/supervision/
  config.json                  ← repos list, telegram settings reference, model defaults
  secrets.json                 ← bot token (never committed anywhere)
  statusline.ps1               ← installed status line script
  .bridge-state.json           ← byte offsets per tailed file + last telegram update id
  <orch-id>/
    session.json               ← repo path/name, created, telegram topic id, member roster + PIDs
    orchestrator.log.jsonl     ← structured per-orchestration log (bridge + lifecycle events)
    owner-channel.md           ← duplex owner ⇄ supervisor
    imp-<n>/
      channel.md               ← duplex supervisor ⇄ implementer n
```

## Components

### 1. `AIOrchestratorCoreLib` (net10.0, STRICT CODING_PATTERNS)
- **Config**: `IOrchestratorConfig` + loader (config.json + secrets.json merge). Repos seeded by installer from the owner's project registry.
- **SupervisionPaths**: single authority for every path above.
- **OrchestrationSessionStore**: scan/load/save `session.json`, create orchestration folders with seeded channel headers, allocate `imp-<n>` ids, track PIDs.
- **ChannelEntry_Parser**: parse `## [n] FROM <role> — <date> — <subject>` entries; count entries for append numbering.
- **ChannelTailer**: byte-offset tailing of every channel file under the supervision root; append detection; per-file accumulator that emits only COMPLETE entries (an entry is complete when the next header appears, or when the file has been quiet for 2 poll ticks). Offsets persisted in `.bridge-state.json`; truncation anomaly → reset offset, log warning.
- **TelegramApi_Client**: thin `HttpClient` wrapper — `sendMessage` (with `message_thread_id`), `createForumTopic`, `getUpdates` long-poll. Pure helpers split out for testability: `TelegramMessage_Chunker` (4096-char limit, split on line boundaries), update filtering/routing logic.
- **MirrorFormatter**: entry → tagged Telegram text (`〔orch-id〕 [imp-2 → sup] …`).
- **SpawnCommand_Builder** + **SessionSpawner**: build and execute `wt new-tab --title … --tabColor … -d <repo> powershell -NoProfile -Command "<set env; claude '/role id'>"`. Env vars: `AIORCH_ROLE`, `AIORCH_ID`, `AIORCH_MEMBER`. Tab colors: red family = supervisor, blue family = implementer. No `-NoExit`: the shell dies with claude, so the PID is a liveness signal. Fallback `Start-Process powershell` when `wt` is missing. Model per role from config (`claude --model <m>`).
- **MemberState_Resolver**: derive per-member development state from parsed channel entries: `NewNoTraffic`, `ImplementerWorking` (last entry FROM supervisor), `AwaitingSupervisorReview` (last FROM implementer), `WritingWindowOpen` (an announced window marker without a later close), `BlockedOnOwner` (blocked-marker in the latest implementer entry). Plus last-activity timestamp.
- **OrchestrationLog**: JSONL structured log per orchestration + global; raises events so the UI can display live.
- **BridgeEngine**: the loop. Every tick: discover channels → tail → mirror complete entries to the orchestration's topic (if Telegram configured) → long-poll `getUpdates` → route owner messages (filter: supergroup chat id + owner user id) by `message_thread_id` to the right `owner-channel.md`, appended as a numbered `FROM owner` entry. Telegram unreachable → exponential backoff, files remain truth, mirror catches up from offsets. All activity logged.

### 2. `AIOrchestrator` (WPF, net10.0-windows, UI-relaxed patterns)
- Composition root in `App`: config load, CoreLib service construction, BridgeEngine on background task, single-instance named mutex (protects the single-`getUpdates` invariant).
- Main window: repo list ("Start Supervisor" per repo) · orchestration cards (members with role icon, state chip, last-activity; "Add Implementer"; "Open Folder") · live log panel (suite `ListBoxLoggerSimple` fed by CoreLib log events) · status bar (bridge/Telegram state).
- Suite reuse: `ProjectReference` → `Da-Vinci-Fintech-Suite/00_Shared/LoggingLib` (brings `ExtensionsAndMethodsLib` transitively). Convention: suite repo checked out at `..\..\manuelvene90\Da-Vinci-Fintech-Suite` relative to this repo; the installer verifies. CoreLib itself references NOTHING from the suite (testable standalone); only the app consumes suite UI/log components.

### 3. Role commands (`kit/commands/` → installed to `~/.claude/commands/`)
- `/supervisor <orch-id>` and `/implementer <orch-id>/imp-<n>`: full role protocol — boot sequence (read channels from top; you may be resuming), append-only entry format, watcher arming before EVERY turn end (background bash watcher generalized from the proven single-file version: supervisor watches all spokes + owner-channel; implementer watches its own channel for FROM-supervisor), announced writing/mutation windows, no-blanket-staging discipline, owner escalation via `owner-channel.md`, no-acknowledgment-chatter rule.

### 4. Status line kit (`kit/statusline/statusline.ps1`)
- Claude Code `statusLine` command (configured by installer in `~/.claude/settings.json`, absolute path). Reads stdin session JSON + `AIORCH_ROLE`/`AIORCH_ID`/`AIORCH_MEMBER` env: `🔴 SUPERVISOR · <orch>` vs `🔵 IMPLEMENTER imp-2 · <orch>` (ANSI colors), falling back to model + cwd for non-orchestrated sessions.

### 5. Installer (`kit/install.ps1`)
- Verifies `claude` CLI; creates `~/.claude/supervision/`; prompts for bot token / supergroup chat id / owner user id (skippable → Telegram-less mode); writes config + secrets; installs commands + statusline (merging `settings.json` with backup); seeds repo list; verifies suite repo location for building; prints the one-time Telegram setup steps (BotFather → supergroup → enable Topics → add bot as admin).

## Error handling
- App closed → protocol unaffected (sessions self-wake off files).
- Terminal closed → PID dead → member shown offline; respawn from app; channel survives; role command boot handles resume.
- Two app instances → second exits with a message (mutex).
- Telegram down → backoff + catch-up; never blocks file flow.
- Channel file truncated externally → offset reset + warning log (channels are append-only by protocol; truncation is an anomaly).

## Testing
Unit tests (xunit) for every pure part: parser, chunker, mirror formatter, spawn command builder, state resolver, tailer (temp files), session store (temp dir), inbound routing filter. Telegram HTTP and process spawning sit behind thin adapters and are exercised by a documented manual end-to-end script per machine.

## AMENDMENT (2026-08-06, same day, owner directives during implementation)

### General supervisor (always-on, Telegram-first)
A permanent GENERAL SUPERVISOR session — the owner's orchestration concierge — living at
`~/.claude/supervision/general/channel.md`, mirrored to the supergroup's **General topic** (always
exists, undeletable, owner pins it; messages there carry no `message_thread_id`, which is exactly
how the bridge routes them). The owner tells it "I need to work on skeleton client" from their
phone; it resolves the repo against `config.json` (`RepoQuery_Resolver`: exact match, else UNIQUE
substring, ambiguity → ask, never guess) and asks the app to act. Spawned from the app's
"Start General Supervisor" button (amber tab, `/general-supervisor`, works from the supervision root).

### The request-file protocol (`.requests/*.json`) — agents ask, the app executes
Agents cannot click the WPF app, so they drop JSON request files that the app's bridge engine
executes within one mirror tick (~2 s), always confirming (or failing) with a `FROM app` entry on
the requester's channel — which wakes the requester via its watcher. `FROM app` is a first-class
channel author (`ChannelAuthors.App`, tag `⚙ [app → …]`). Malformed request files are logged and
deleted, never allowed to wedge the loop; processed files are deleted after execution.

| Action | Requester | Effect | Confirmation lands on |
|---|---|---|---|
| `start-orchestration` (orchId, repo) | general supervisor | create orchestration + spawn supervisor terminal (+ topic on first mirror) | `general/channel.md` |
| `add-implementer` (orchId) | orchestration supervisor | allocate `imp-<n>`, seed channel, spawn terminal | that orchestration's `owner-channel.md` |
| `close-implementer` (orchId, memberId) | orchestration supervisor | mark member closed; channel kept on disk, no longer tailed | that orchestration's `owner-channel.md` |
| `close-orchestration` (orchId) | general supervisor | mark session closed + close the Telegram topic; folder kept as audit trail | `general/channel.md` |

Closed members/orchestrations: `ClosedUtc` on the member/session in `session.json`; the bridge
stops tailing them; the UI dims the card / greys the chip. Nothing is ever deleted.

### Consequences folded into the components
- `ISupervisionPaths` gains `GeneralFolder`/`GeneralChannelFile`/`RequestsFolder`; discovery treats
  `general` as a reserved orchestration id with owner-channel semantics and a null Telegram thread id.
- `IOrchestrationLauncher` is the shared execution seam for UI buttons AND request files.
- The `/general-supervisor` role command ships in the kit alongside `/supervisor` and `/implementer`.

## AMENDMENT 2 (2026-08-06, owner directives during the same build session)

### Session lifecycle — always-on while the app runs, everything dies with the app
- **PID files are the liveness truth**: every spawned shell writes its own `$PID` to a `.pid` file
  beside its channel (wt.exe delegates and exits, so its pid is useless). A `SessionWatchdog`
  (engine tick) respawns any dead required session with 45 s backoff and pid-recycling protection
  (the pid must still be a PowerShell process): the general supervisor (always), and the
  supervisor + non-closed implementers of every open orchestration.
- **App start** ⇒ watchdog brings everything up (general supervisor auto-starts). **App exit** ⇒
  `SessionTerminator` tree-kills every pid file under the supervision root. **Orchestration
  close** ⇒ its supervisor + implementers are killed too. State lives on disk; the next app start
  resumes everything.
- **Resume semantics:** general supervisor restarts with `claude --continue` (its cwd — the
  supervision root — is unique to it) falling back to the role command; supervisors/implementers
  restart through their role commands, whose boot re-reads the channels (`--continue` in a repo
  shared by several sessions could resume the wrong conversation).

### Orchestration ids & repo naming
- Ids are ALLOCATED (`OrchId_Allocator`): `repo-slug-n`, incremental per repo, derived from
  existing folders. Nobody types ids; the start request carries only the repo.
- Colloquial repo naming is the GENERAL SUPERVISOR's job (it is an LLM); `RepoQuery_Resolver`
  (exact, else unique substring, ambiguity → null → ask) is only the app-side safety net.
- The general supervisor runs a cheap model by default (`generalSupervisorModel`, default
  "sonnet") and stays mostly silent. Every new orchestration's supervisor GREETS the owner naming
  the full repo directory (mapping verification) and invites instructions.

### Do-Not-Disturb with catch-up burst (the away-from-PC model)
- Mute (UI checkbox, or any supervisor dropping `{"action":"set-telegram-muted","muted":true}` on
  the owner's texted request) pauses ALL outbound Telegram. Implementation: the engine skips
  tailing entirely, freezing offsets — so unmute (UI, request, or the owner texting ANYTHING,
  which auto-unmutes) delivers every pending entry in one catch-up burst, including supervisors'
  waiting questions in their topics. Inbound always works.
- The check-in ritual is a first-class flow in the general supervisor's role command: text →
  auto-unmute → "make a summary" → compact digest (per orchestration: state, progress from
  boundary reports, last activity, pending owner questions + which topic) → answer → "DND on".

### Telemetry & alerts (the status line is the probe)
- `statusline.ps1` renders role identity AND dumps the raw statusline JSON to the session's
  `.usage.json`. The UI reads `cost.total_cost_usd` per member; the engine scans all usage files
  every 60 s, extracts limit percentages tolerantly (`LimitData_Parser` — schema varies by Claude
  Code version; no data ⇒ idle), and texts the General topic on crossing 90/95/97/98/99/100%
  (`LimitAlert_Tracker`, per-window dedup, reset below 50%). ⚠ NEEDS LIVE VERIFICATION: whether
  this Claude Code version's statusline payload carries limit data.

### Session-manager UI
- Cards show implementer count + orchestration age; per-member second line: current task (last
  supervisor brief subject), worktree (the brief's `WORKTREE: <path>` marker line — protocol),
  time on task, session cost. "🖥 Show" per row foregrounds that session's terminal
  (`TerminalWindow_Focuser`, Win32 EnumWindows by title; sessions spawn one WT window each via
  `wt -w new` precisely to make titles matchable). Settings window manages bot token / chat id /
  owner id / models. Mute checkbox in the status bar (synced both ways with agent requests).

### Images from the owner (screenshots of bugs)
- Owner photo messages are downloaded by the bridge (getFile + file endpoint) into `media/` beside
  the target channel; the appended owner entry carries the caption plus an `IMAGE: <path>` line.
  Supervisors Read the file to inspect it (Claude Code reads images natively) and pass the path
  into implementer briefs. Download failure degrades to a text entry noting the failure.

### Repo knowledge source (CORRECTED 2026-08-06, owner)
- **The general supervisor's knowledge is machine-portable and lives in ITS OWN home:** every
  general session runs in `~/.claude/supervision/general/` (same folder, always), whose
  `CLAUDE.md` auto-loads as its persistent memory — the app seeds a bare knowledge file on first
  run, and the agent maintains it itself (repo map, colloquial names, owner preferences).
- Colloquial repo names resolve against `config.json` + that knowledge file; the emitted request
  carries the exact configured repo name.
- **Fresh machine:** the seed file is bare → the general supervisor says so in its greeting and
  asks the owner where to learn the repo landscape. Machine-local sources (e.g. this machine's
  user-level `~/.claude/CLAUDE.md` project registry) are possible INITIALIZATION inputs the owner
  may point it at — never a general rule, never assumed to exist. What it learns gets recorded in
  its own `CLAUDE.md`.
- (Per-orchestration supervisors are the complement: their knowledge context is the REPO they run
  in — its `CLAUDE.md` — plus whatever the machine's user-level config loads.)

### Worktree stewardship (protocol)
- The orchestration SUPERVISOR owns worktree lifecycle (creation, merging, removal) and the
  implementer→worktree mapping; one worktree per implementer by default; merges only after review;
  removals only when merged/discarded; owner instructions override. Implementers never manage
  worktrees on their own initiative.

### Periodic owner updates (protocol)
- On request, a supervisor switches its watcher to the timeout variant (e.g. 1800 s) and appends a
  concise status entry to its owner-channel on each quiet timeout — mirrored to Telegram.

## v1 scope cuts (explicit)
Mac launcher (design permits, not built) · owner→implementer direct messaging (everything routes through the supervisor) · multiple supergroups · session auto-restart/watchdog · mirroring owner terminal input back to Telegram.
