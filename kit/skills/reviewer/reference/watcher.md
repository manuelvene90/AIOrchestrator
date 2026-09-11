## The watcher — ONE persistent Monitor, armed at boot (definition of done)

Arm it ONCE, at the end of your boot sequence, with the **Monitor** tool and `persistent: true`,
substituting your ids:

```
Monitor(
  description: "supervisor traffic on my channel",
  persistent: true,
  command: <the script below>
)
```

```bash
ch="${AIORCH_SUPERVISION_ROOT:-$HOME/.claude/supervision}/<orch-id>/<member-id>/channel.md"

# Sets FP, or returns non-zero with FP_ERR naming the command that failed. A read that FAILED is
# not a read that saw something different — see "A failed read is not a change" below.
read_fp() {
  FP=""; FP_ERR=""
  local size hash
  if ! size="$(wc -c < "$ch" 2>/dev/null)" || [ -z "$size" ]; then FP_ERR="wc -c"; return 1; fi
  if ! hash="$(md5sum "$ch" 2>/dev/null)"  || [ -z "$hash" ]; then FP_ERR="md5sum"; return 1; fi
  # Trimmed with parameter expansion, never a pipe into tr: a pipe would hand the `if !` above the
  # exit status of tr, and a failed read would start reporting itself as a successful one.
  size="${size// /}"
  FP="$size ${hash%% *}"
}

# Was this change nothing but OUR OWN append? channel-append.sh records, inside the lock, the size
# the channel had when our current unbroken run of writes started and the fingerprint it left
# behind. BOTH must match: the fingerprint alone would swallow a brief that landed just before our
# own entry, because the file would still carry exactly the fingerprint our write left. Anything
# foreign in between moves `start` past the last size we saw, and we fire.
#
# The record is per AUTHOR — the supervisor writes to this same channel, and its append must still
# wake you.
self_write_suppresses() {
  local record start after
  record="$ch.self-write.reviewer"
  [ -f "$record" ] || return 1
  start="$(grep -m1 '^start=' "$record" 2>/dev/null | cut -d= -f2- | tr -d ' ')"
  after="$(grep -m1 '^after=' "$record" 2>/dev/null | cut -d= -f2-)"
  [ -n "$start" ] && [ -n "$after" ] || return 1
  [ "$after" = "$FP" ] || return 1
  [ "$start" -le "${prev%% *}" ] 2>/dev/null
}

# The watcher drops a FACT; the APP writes the record. Never write the log file from here.
mark_unreadable() {
  local orch="${AIORCH_SUPERVISION_ROOT:-$HOME/.claude/supervision}/${AIORCH_ID:-}"
  [ -n "${AIORCH_ID:-}" ] && [ -d "$orch" ] || return 0
  printf '%s\n%s\n%s\n%s\n%s\n%s\n' "watcher" "the channel fingerprint" "$1 failed" \
    "${AIORCH_MEMBER:-}" "" "took the fingerprint as unknown rather than as a change" \
    > "$orch/.guard-not-in-force" 2>/dev/null
  return 0
}

prev=""; fails=0
if read_fp; then prev="$FP"; else fails=1; mark_unreadable "$FP_ERR"; fi
while true; do
  sleep 5
  if read_fp; then
    fails=0
    if [ -n "$prev" ] && [ "$FP" != "$prev" ] && ! self_write_suppresses; then
      echo "YOUR CHANNEL CHANGED — read from your last entry down, act on it, append your report."
    fi
    prev="$FP"
  else
    fails=$((fails + 1))
    if [ "$fails" -eq 1 ]; then mark_unreadable "$FP_ERR"; fi
    if [ "$fails" -eq 12 ]; then
      echo "WATCHER BLIND — your channel has been unreadable for about a minute ($FP_ERR failing). This is NOT a change notification: read the file yourself, and expect the machine to be out of memory or disk."
    fi
  fi
done
```

**A failed read is not a change — this is the defect the old loop had.** The old one discarded both
commands' exit statuses and always returned success, so a `wc` or `md5sum` that could not run
produced an empty or partial fingerprint, which compared unequal to the real one and fired. **One
failed read produced exactly two phantom wakes** — one going into the failure, one coming out — and
nothing anywhere recorded that a read had failed. Measured on 2026-08-14: a channel untouched for 27
minutes woke its member four times. It is worst on a machine that is out of memory, which is exactly
when forks fail and when real traffic matters most.

**YOUR OWN APPEND IS NOT TRAFFIC.** A fingerprint cannot tell whose write changed the file, so every
report you wrote used to wake you — and a wake is a full context reload, spent to learn that you had
written something you already knew you had written. `channel-append.sh` now records, from inside the
lock, the size the channel had when your current unbroken run of writes started and the fingerprint
it left; the watcher fires unless BOTH say the change was yours alone. The two-fact rule is the
load-bearing part: if the supervisor writes and you append a minute later, the file still carries
exactly the fingerprint your write left, and suppressing on the fingerprint alone would sleep through
them. **Never weaken this to the fingerprint on its own.**

So `read_fp` checks each command and **keeps `prev` untouched when it cannot read**. Nothing is lost
by waiting: if an append lands during a failed spell, the next successful read still differs from the
preserved `prev` and fires then. After twelve consecutive failures the loop says so — once, in words
that cannot be mistaken for traffic — rather than letting you sleep through real entries.

**Why a Monitor and not a `run_in_background` Bash task — this is measured, not preference.** On
2026-08-07 twenty-nine background watchers were killed across four sessions of one orchestration,
several in the SAME SECOND in different sessions; every one was a Bash `run_in_background` task,
while a persistent Monitor survived those same instants for 41+ minutes. This shape also removes
the re-arm obligation and the baseline race — the monitor holds `prev` continuously, so nothing
that arrives while you work can fall into a gap.

**If the monitor ever stops** (a `killed`/stopped notification for it), arm a fresh one immediately.

**Never narrow the fingerprint to a text pattern.** It hashes the WHOLE file on purpose. A watcher
that greps for a phrase (`FROM supervisor`, a subject wording) is only as reliable as the writer's
consistency — and on 2026-08-07 a supervisor wrote its headers three different ways, so a
pattern-anchored watcher stayed perfectly healthy and never fired. Any byte that changes is traffic.

**Nothing wakes you except this monitor.** It fires only when your supervisor writes. A long review
is yours to carry to its end within your turn — if you end a turn mid-review expecting to resume by
yourself, you will simply sleep.

**On resume you may see notifications about orphaned background tasks** — they died with their
session. Ignore them and arm your monitor as part of the boot.

Now execute the boot sequence.
