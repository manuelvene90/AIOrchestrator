## The monitor — ONE persistent Monitor, armed at boot

```
Monitor(
  description: "owner traffic on $ARGUMENTS",
  persistent: true,
  command: <the script below>
)
```

```bash
ch="${AIORCH_SUPERVISION_ROOT:-$HOME/.claude/supervision}/$ARGUMENTS/owner-channel.md"

# Sets FP, or returns non-zero with FP_ERR naming the command that failed. A read that FAILED is
# not a read that saw something different — see below.
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
# behind. BOTH must match: the fingerprint alone would swallow an owner message that landed just
# before our own entry, because the file would still carry exactly the fingerprint our write left.
# Anything foreign in between moves `start` past the last size we saw, and we fire.
self_write_suppresses() {
  local record start after
  record="$ch.self-write.solo"
  [ -f "$record" ] || return 1
  start="$(grep -m1 '^start=' "$record" 2>/dev/null | cut -d= -f2- | tr -d ' ')"
  after="$(grep -m1 '^after=' "$record" 2>/dev/null | cut -d= -f2-)"
  [ -n "$start" ] && [ -n "$after" ] || return 1
  [ "$after" = "$FP" ] || return 1
  [ "$start" -le "${prev%% *}" ] 2>/dev/null
}

# The watcher drops a FACT; the APP writes the record. Never write the log file from here.
mark_unreadable() {
  local orch="${AIORCH_SUPERVISION_ROOT:-$HOME/.claude/supervision}/$ARGUMENTS"
  [ -d "$orch" ] || return 0
  printf '%s\n%s\n%s\n%s\n%s\n%s\n' "watcher" "the owner channel fingerprint" "$1 failed" \
    "solo" "" "took the fingerprint as unknown rather than as a change" \
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
      echo "OWNER WROTE — read from your last entry down, act on it, reply."
    fi
    prev="$FP"
  else
    fails=$((fails + 1))
    if [ "$fails" -eq 1 ]; then mark_unreadable "$FP_ERR"; fi
    if [ "$fails" -eq 12 ]; then
      echo "WATCHER BLIND — the owner channel has been unreadable for about a minute ($FP_ERR failing). This is NOT a message: read the file yourself, and expect the machine to be out of memory or disk."
    fi
  fi
done
```

**A failed read is not a change.** The old loop discarded both commands' exit statuses and always
returned success, so a `wc` or `md5sum` that could not run produced an empty fingerprint, compared
unequal to the real one, and fired — **one failed read, two phantom wakes**, with nothing recording
that a read had failed. `read_fp` now keeps `prev` untouched when it cannot read, so a message that
lands during a failed spell still fires on the next successful read; and after twelve consecutive
failures it says plainly that it is blind rather than letting you sleep through the owner.

**YOUR OWN APPEND IS NOT TRAFFIC.** A fingerprint cannot tell whose write changed the file, so every
entry you wrote used to wake you — and a wake is a full context reload, bought to learn that you had
written something you already knew you had written. About half of every wake on this machine was
that. `channel-append.sh` now records, from inside the lock, the size the channel had when your
current unbroken run of writes started and the fingerprint it left; the watcher fires unless BOTH
say the change was yours alone. The two-fact rule is the load-bearing part: if the owner writes at
23:14 and you append at 23:15, the file still carries exactly the fingerprint your write left, and
suppressing on that alone would sleep through them. **Never weaken this to the fingerprint on its
own** — the saving is small and what it costs is the owner's message.

**Use a Monitor, never a `run_in_background` Bash task.** On 2026-08-07 twenty-nine background
watchers were reaped across four sessions in one day, several in the same second; every one was a
Bash background task and no Monitor was ever touched. It also removes the re-arm step and the
baseline race entirely — the monitor holds `prev` continuously, so nothing that arrives while you
work can fall into a gap. Never narrow the fingerprint to a text pattern.

**Nothing wakes you except this monitor and the owner.** It fires only when THEY write. If you end a
turn with your own work unfinished, you sleep until spoken to — so finish the step, or say
explicitly what you are waiting for.

**`GO AHEAD — resume`** entries mean the owner sent `/resume` (usually a usage-limit reset): pick up
exactly where you left off, redo the step the limit cut short, and if you were genuinely finished
say so in one line rather than inventing work.

Now execute the boot sequence.
