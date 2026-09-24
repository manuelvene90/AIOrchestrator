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

# ---- THE SIBLING HALF (a linked solo only) — every outbox listed in "$orch/.siblings" ----------
# Independent of the owner half: its own fingerprint set, its own failure count, its own alarm.
# There is NO self-write logic here, and none may be added: your own outbox is never listed in your
# own .siblings (the app derives the file from the OTHER members), and every sibling signs `FROM
# solo`, so a suppression rule here would read a sibling's record and sleep through that sibling.
orch="${AIORCH_SUPERVISION_ROOT:-$HOME/.claude/supervision}/$ARGUMENTS"
sibs="$orch/.siblings"   # derived by the app; re-read EVERY iteration, so a sibling born mid-loop joins

# Prints the state recorded for outbox $2 in fingerprint set $1 ("<size> <hash>", or "absent"), or
# returns non-zero when the set has no line for it — a path never seen.
sib_line_of() {
  local line
  while IFS= read -r line; do
    if [ -n "$line" ] && [ "${line%%|*}" = "$2" ]; then printf '%s' "${line#*|}"; return 0; fi
  done <<< "$1"
  return 1
}

# Sets SIB_LIST (the lines of .siblings), SIB_FP (one "<path>|<size> <hash>" line per outbox) and
# SIB_ERR (what failed, or empty). An outbox that cannot be read keeps its PREVIOUS line: unknown,
# never a change. One that does not exist yet is "absent", so its first entry IS a change.
read_sib_fp() {
  SIB_LIST=""; SIB_FP=""; SIB_ERR=""
  [ -f "$sibs" ] || return 0
  if ! SIB_LIST="$(cat "$sibs" 2>/dev/null)"; then
    SIB_ERR="reading .siblings"; SIB_LIST=""; SIB_FP="$sib_prev"; return 0
  fi
  local id path rest size hash kept
  while IFS=$'\t' read -r id path rest; do
    [ -n "$id" ] && [ -n "$path" ] || continue
    if [ ! -e "$path" ]; then SIB_FP="$SIB_FP$path|absent"$'\n'; continue; fi
    # Hashed from stdin, not from a file argument: md5sum escapes its whole output line when the
    # path holds a backslash, which every Windows path in .siblings does.
    if ! size="$(wc -c < "$path" 2>/dev/null)" || [ -z "$size" ] \
       || ! hash="$(md5sum < "$path" 2>/dev/null || md5 -q < "$path" 2>/dev/null)" || [ -z "$hash" ]; then
      SIB_ERR="reading the outbox of $id"
      if kept="$(sib_line_of "$sib_prev" "$path")"; then SIB_FP="$SIB_FP$path|$kept"$'\n'; fi
      continue
    fi
    size="${size// /}"
    SIB_FP="$SIB_FP$path|$size ${hash%% *}"$'\n'
  done <<< "$SIB_LIST"
}

# Did outbox $1 change since the last read? Never for a path seen for the FIRST time (a sibling born
# mid-loop is baseline), never when this read could not see it. NO self-write test, on purpose.
sib_changed() {
  local before now
  before="$(sib_line_of "$sib_prev" "$1")" || return 1
  now="$(sib_line_of "$SIB_FP" "$1")" || return 1
  [ "$now" != "$before" ]
}

prev=""; fails=0
if read_fp; then prev="$FP"; else fails=1; mark_unreadable "$FP_ERR"; fi
sib_prev=""; sib_fails=0
read_sib_fp; sib_prev="$SIB_FP"; [ -z "$SIB_ERR" ] || sib_fails=1
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
  # The sibling half. While you are PAUSED it neither fires nor moves its baseline, so what a
  # sibling wrote during the pause fires once, on the first read after the owner wakes you.
  if [ ! -e "$orch/.paused" ]; then
    read_sib_fp
    if [ -n "$SIB_ERR" ]; then
      sib_fails=$((sib_fails + 1))
      if [ "$sib_fails" -eq 12 ]; then
        echo "WATCHER BLIND — a sibling outbox has been unreadable for about a minute ($SIB_ERR failing). This is NOT a message: read the outboxes listed in .siblings yourself."
      fi
    else
      sib_fails=0
    fi
    while IFS=$'\t' read -r sib_id sib_path sib_rest; do
      [ -n "$sib_id" ] && [ -n "$sib_path" ] || continue
      if sib_changed "$sib_path"; then
        echo "SIBLING $sib_id WROTE — read its outbox from the last entry you saw, act if it asks you something."
      fi
    done <<< "$SIB_LIST"
    sib_prev="$SIB_FP"
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

**A sibling's append is traffic, and your own outbox is never listed.** The sibling half (only when
`$orch/.siblings` exists — you are linked, see `reference/siblings.md`) watches each other sibling's
outbox and says `SIBLING <id> WROTE`. It has no self-write rule because it needs none: the app lists
only the OTHER members, so nothing you write can change a file this half watches. Adding one would be
the defect, not the fix — every sibling signs `FROM solo`, so it would read a sibling's
`.self-write.solo` record and sleep through that sibling. A sibling seen for the first time (born
while you were running) is recorded as baseline, never announced. A failed read is handled exactly as
"A failed read is not a change" says above, per outbox, with its own blind alarm. **A wake that finds
no new entry means nothing:** the app compacts a long outbox, which changes its size and hash — read
from your last-seen index, find nothing new, and carry on (if the first live index is past your
last-seen + 1, the gap is in `sibling-outbox.archive.md`).

**Paused, you do not hear siblings; they wait for the owner.** While `$orch/.paused` exists the half
neither fires nor moves its baseline, so everything a sibling wrote during the pause fires once, on
the first read after the owner wakes you — the same "not a change until it is seen" discipline as a
failed read, applied to a pause.

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
