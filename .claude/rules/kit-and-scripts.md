---
paths:
  - "kit/**"
  - "deploy/**"
---
# Kit, plugin and scripts

- `kit/` is the `aiorch` Claude Code plugin (skills per role, `bin/channel-append.sh`, hooks in each skill's frontmatter) and its local marketplace. The role protocols are Manu's method: **reorganise, never rewrite a rule**; a test counts normative sentences before and after.
- A rule that a session cannot see is not a rule: modes and roots are resolved with a command at boot, never assumed from an environment variable. Pointers to `reference/*.md` carry the resolution command.
- Executables in `bin/` are `100755` (a test enforces it): a non-executable helper made a session write the channel without a lock.
- Shell: `#!/usr/bin/env bash`, `set -euo pipefail` (mind `grep -q` + pipefail → SIGPIPE), and **macOS/BSD portability**: `md5sum` → `md5 -q` fallback; `date -d` is GNU-only (`date -j -f` on BSD); `stat -c` → `stat -f`; BSD `awk` rejects some `printf` forms; no `python3` in the statusline (Decision 19). The lock is `mkdir`, never `flock`.
- The installer is a version verifier: it never copies commands into `~/.claude/commands` (a local command beats a plugin skill for the slash word — measured); it removes the app's own old footprint and refuses to spawn on a mismatch, keeping the bridge up.
- `deploy/` holds the systemd unit, the launchd plist and the Windows service script; the VPS recipe lives outside this repo (`00_Infrastructure/aiorch-vps/`).
