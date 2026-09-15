Arm the one your `runners.communicator.wake` names; if you do not know, arm the watcher-mode script.

## Watcher mode — when you decide

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

## Ticket mode — when the app decides

**One deliberate change of shape, not just of path.** Watcher mode above is the one role command in
this kit that arms a `run_in_background` Bash task instead of a Monitor, and it has to re-arm itself
at the end of every turn. Ticket mode gives you the SAME persistent Monitor the other five roles use
instead — no re-arming, no 180 s timeout, no baseline race — because those are exactly the two
failure modes measured elsewhere in this kit (twenty-nine background watchers reaped in one day,
41+ minutes of unbroken survival for a Monitor across those same instants). This is a genuine
improvement over watcher mode's own shape, not merely a port of it; it ships dark behind the gate like
everything else in this series, so it changes nothing on a machine that states nothing.

```
Monitor(
  description: "wake ticket on orchestration $ARGUMENTS",
  persistent: true,
  command: <the script below>
)
```

```bash
# TICKET MODE. The app decides whether you should take a turn, with the same policy it uses for a
# headless session, and says so by writing this file. You carry the message; you do not decide.
#
# The ticket is read as RAW TEXT, never parsed as JSON — no jq, not guaranteed on every machine. Any
# change in its bytes is a new ticket; a missing file is silence, not an error.
ticket="${AIORCH_SUPERVISION_ROOT:-$HOME/.claude/supervision}/${AIORCH_ID:-}/.wake-communicator"
last=""
while true; do
  sleep 2
  now="$(cat "$ticket" 2>/dev/null)" || continue
  [ -z "$now" ] && continue
  [ "$now" = "$last" ] && continue
  last="$now"
  echo "WAKE — $now. Read owner-channel.md from your last entry down, apply your behavior rules."
done
```

**No `.meeting` check here, on purpose.** Watcher mode never had one for this role either — checked
2026-09-15, only the supervisor's watcher.md carries the `.meeting` rule today. Ticket mode conserves
what this file already did; it invents nothing.

**Adapted from `$ARGUMENTS` to `$AIORCH_ID`.** `$ARGUMENTS` does carry the orchestration id for a
communicator, so the literal plan text would have worked here too — this uses `$AIORCH_ID` instead
only so one script shape serves all six role commands, since `$ARGUMENTS` is not `<orch-id>` for
every role (solo and the general supervisor differ — see their own ticket-mode sections).

**No RE-ARM instruction, unlike watcher mode above.** A persistent Monitor keeps running after it
fires; do not arm a fresh one after each wake, and do not carry the 180 s "still busy" timeout logic
into this mode — that behaviour lived in the old polling shape and has no counterpart here. If Sup is
busy and the owner is still waiting when you wake, post the status update the watcher-mode section
above describes, then let the Monitor keep running.

Now execute the boot sequence.
