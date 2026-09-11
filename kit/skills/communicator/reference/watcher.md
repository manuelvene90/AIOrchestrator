## The watcher — arm it before ending EVERY turn (definition of done)

Run with the Bash tool, `run_in_background: true`. Wakes you on owner traffic (narrate if Sup is
busy) AND on supervisor entries (your cue to go silent); the 180 s timeout drives the periodic
"still busy" updates — on a timeout wake with no new traffic, post an update ONLY if the
supervisor is busy AND the owner is still waiting on it since their last message.

```bash
ch="${AIORCH_SUPERVISION_ROOT:-$HOME/.claude/supervision}/$ARGUMENTS/owner-channel.md"
count() { grep -c "FROM owner\|FROM supervisor" "$ch"; }
base=$(count); start=$(date +%s)
until [ "$(count)" -gt "$base" ] || [ $(( $(date +%s) - start )) -ge 180 ]; do sleep 5; done
if [ "$(count)" -gt "$base" ]; then echo "NEW TRAFFIC — read owner-channel.md from your last read down, apply your behavior rules, RE-ARM this watcher."; else echo "TIMEOUT — if Sup is busy and the owner awaits a reply, post a short STATUS update, then RE-ARM this watcher."; fi
```

**On resume you may see notifications about orphaned background tasks from a previous session** —
old watchers, killed with that session. Ignore them and arm a fresh one.

Now execute the boot sequence.
