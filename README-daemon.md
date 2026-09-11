# AIOrchestrator.Daemon — the headless host

The same bridge the WPF app runs (Telegram mirror, owner console, session watchdog), as a daemon.
One build serves all three OSes; the init system is detected at runtime. The WPF app on Windows is
unchanged — the two exclude each other through `~/.claude/supervision/.instance.lock`, so run one.

**Build & run anywhere** (needs the .NET 10 SDK; `DOTNET_ROOT=~/.dotnet` if the SDK is user-local):

    dotnet run --project AIOrchestrator.Daemon            # Ctrl-C = clean stop, sessions killed
    dotnet publish AIOrchestrator.Daemon -c Release -r <osx-arm64|linux-x64|win-x64> --self-contained -o <dir>
Options: `--root DIR` / `AIORCH_SUPERVISION_ROOT` (default `~/.claude/supervision`), `--claude-home DIR` /
`AIORCH_CLAUDE_HOME` (default `~/.claude`, where commands, hooks and settings.json are installed).
`--root` is exported to every session the daemon spawns, so they find their channels there.
`--claude-home` is INSTALL-ONLY: the `claude` CLI reads its own config dir (and its login) from
`CLAUDE_CONFIG_DIR`/`~/.claude`, so pointing this elsewhere without also installing the kit where the
CLI looks leaves the role commands unresolvable — measured 2026-09-06.

**Config**: `<root>/config.json` (repos, models, chat ids) and `<root>/secrets.json` (bot token, read by
the bridge only — never in a unit file, plist or log). `kit/install.sh` (macOS/Linux) or `kit/install.ps1`
(Windows) writes both interactively. Without a token the daemon runs in file-only mode.

**Logs**: stdout, one line per entry (`HH:mm:ss LEVEL [orch] message`; `<n>` journal priorities under
systemd) — plus the JSONL files the app has always written: `<root>/orchestrator-global.log.jsonl` and
`<root>/<orch-id>/orchestrator.log.jsonl`.
| OS | install | start / stop | logs |
|---|---|---|---|
| Linux (systemd) | publish `linux-x64` to `/opt/aiorchestrator`; copy `deploy/systemd/aiorchestrator.service` to `/etc/systemd/system/`, set `User=` + `HOME=` | `systemctl enable --now aiorchestrator` / `systemctl stop aiorchestrator` | `journalctl -u aiorchestrator -f` |
| macOS (launchd) | publish `osx-arm64` to `/usr/local/opt/aiorchestrator`; `sed "s#HOME_DIR#$HOME#g" deploy/launchd/com.aiorchestrator.daemon.plist > ~/Library/LaunchAgents/com.aiorchestrator.daemon.plist` | `launchctl load ~/Library/LaunchAgents/com.aiorchestrator.daemon.plist` / `launchctl unload …` | `~/Library/Logs/aiorchestrator/daemon.log` |
| Windows (service) | publish `win-x64` to `C:\aiorchestrator`; elevated: `deploy\windows\install-service.ps1 -BinaryPath C:\aiorchestrator\aiorchestrator-daemon.exe` | `Start-Service AIOrchestrator` / `Stop-Service AIOrchestrator` (`-Uninstall` removes it) | Event Viewer + the JSONL files |

The systemd unit is `Type=notify` with `WatchdogSec=60`: READY=1 is sent once the engine runs, WATCHDOG=1
every 30 s from the daemon's main loop, and the pings stop the moment the engine dies. Stopping the daemon
(SIGTERM, Ctrl-C, `launchctl unload`, `systemctl stop`) cancels the engine and kills every session it
spawned, exactly like closing the WPF window; the watchdog respawns them on the next start.

**Memory on a shared host**: this box also runs the bridge (and every other session the daemon is
managing) — an unbounded session can starve all of them. Measured 2026-09-07: a 6.5 GB allocator on
an 8 GB VPS OOM-killed the bridge three times and took every session down with it. Two layers:
- **The app caps each session's own memory** on Linux via `runners.sessionMemoryMax` in
  `config.json` — a cgroup limit around the spawned `claude` process, default `3G` (landing
  alongside this stage). A session that exceeds it is killed alone; the bridge and every other
  session keep running.
- **The unit should cap itself too**: add `OOMPolicy=continue` (the daemon survives an OOM kill
  inside its own cgroup instead of being torn down with it) and a `MemoryHigh`/`MemoryMax` pair
  sized under the box's RAM, so the daemon process can never eat the machine even if a session
  escapes its own limit. A **swapfile is advised** on small boxes — it turns a hard OOM kill into a
  slowdown instead. See `00_Infrastructure/aiorch-vps/` for the concrete unit and swapfile recipe.

A session that needs a plan tool via MCP gets it registered at **USER scope** for the service user
(`claude mcp add -s user …`), not project scope: a project's `.mcp.json` applies only inside that
project's folder, and the general supervisor runs in its own home directory, outside every project.

**Status line**: the daemon installs `statusline.sh` (bash + jq, no python3) on macOS/Linux, `statusline.ps1` on Windows, wired into `~/.claude/settings.json` through the matching interpreter.
**Measured on a MacBook (arm64, .NET SDK 10.0.400, warm NuGet cache), 2026-09-06**: `git clone` → `dotnet build`
→ daemon logging "Bridge started" = **6 s** (clone 0 s local, build 4 s, start 2 s); a self-contained
`osx-arm64` publish took 2 s more. Under launchd: `load` → first log line 5 s → `unload` → clean stop.
Not measured: a cold NuGet cache, Linux, Windows.

Known limit of this stage: sessions are still spawned through the Windows Terminal runner, so on
macOS/Linux the watchdog logs a spawn failure for the general supervisor until the print runner (stage 1) lands.
