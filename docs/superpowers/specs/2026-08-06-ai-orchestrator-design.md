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

## v1 scope cuts (explicit)
Mac launcher (design permits, not built) · owner→implementer direct messaging (everything routes through the supervisor) · multiple supergroups · session auto-restart/watchdog · mirroring owner terminal input back to Telegram.
