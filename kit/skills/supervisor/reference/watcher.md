## The watcher — ONE persistent Monitor, armed at boot (definition of done)

Arm it ONCE, at the end of your boot sequence, with the **Monitor** tool and `persistent: true`:

```
Monitor(
  description: "channel traffic on orchestration $ARGUMENTS",
  persistent: true,
  command: <the script below>
)
```

```bash
sup="${AIORCH_SUPERVISION_ROOT:-$HOME/.claude/supervision}/$ARGUMENTS"
shopt -s nullglob

# Sets FP, or returns non-zero with FP_ERR naming the command that failed. Each command is run per
# file and its status checked — never `cat … | md5sum` — so that a failed read is visible; see below.
#
# ONE LINE PER CHANNEL, "<path>|<size> <hash>", and the SIZE is not decoration: the self-write record
# below is expressed in sizes, so a fingerprint of hashes alone could not tell your own append from
# anybody else's.
read_fp() {
  FP=""; FP_ERR=""
  local files file size hash out=""
  files=( "$sup"/imp-*/channel.md "$sup"/rev-*/channel.md "$sup/owner-channel.md" )
  for file in "${files[@]}"; do
    if ! size="$(wc -c < "$file" 2>/dev/null)" || [ -z "$size" ]; then FP_ERR="wc -c on $file"; return 1; fi
    if ! hash="$(md5sum "$file" 2>/dev/null)"  || [ -z "$hash" ]; then FP_ERR="md5sum on $file"; return 1; fi
    # Trimmed with parameter expansion, never a pipe into tr: a pipe would hand the `if !` above the
    # exit status of tr, and a failed read would start reporting itself as a successful one.
    size="${size// /}"
    out="$out$file|$size ${hash%% *}"$'\n'
  done
  FP="$out"
}

# Did anything OTHER THAN YOUR OWN APPENDS change? You write to every channel here, so on the busiest
# watcher in the system most changes were your own — this monitor's own note records it firing
# "upwards of a hundred times in a day" with the large majority finding nothing new, and each of
# those is a full context reload.
#
# Per channel, because one spoke's traffic must never be excused by another's. A channel counts as
# yours only when channel-append.sh's record for it says BOTH that the fingerprint is the one your
# write left AND that your unbroken run of writes started at or before the size you last saw. The
# second half is what stops an implementer's report being swallowed: if they append and then you
# append, the file carries exactly your fingerprint, and a hash-only check would sleep through them.
#
# ANY doubt fires: a channel you have never seen, a missing record, a record that does not match.
# Returns 0 (fire) as soon as one changed channel is not provably yours.
foreign_change() {
  local line file now before record start after
  while IFS= read -r line; do
    [ -n "$line" ] || continue
    file="${line%%|*}"; now="${line#*|}"

    before="$(printf '%s' "$prev" | grep -F -m1 "$file|")" || before=""
    before="${before#*|}"

    [ "$now" = "$before" ] && continue
    [ -n "$before" ] || return 0

    record="$file.self-write.supervisor"
    [ -f "$record" ] || return 0
    start="$(grep -m1 '^start=' "$record" 2>/dev/null | cut -d= -f2- | tr -d ' ')"
    after="$(grep -m1 '^after=' "$record" 2>/dev/null | cut -d= -f2-)"
    [ -n "$start" ] && [ -n "$after" ] || return 0
    [ "$after" = "$now" ] || return 0
    [ "$start" -le "${before%% *}" ] 2>/dev/null || return 0
  done <<< "$FP"

  return 1
}

# The watcher drops a FACT; the APP writes the record. Never write the log file from here.
mark_unreadable() {
  [ -d "$sup" ] || return 0
  printf '%s\n%s\n%s\n%s\n%s\n%s\n' "watcher" "the channel fingerprints" "$1 failed" \
    "supervisor" "" "took the fingerprint as unknown rather than as a change" \
    > "$sup/.guard-not-in-force" 2>/dev/null
  return 0
}

prev=""; fails=0
if read_fp; then prev="$FP"; else fails=1; mark_unreadable "$FP_ERR"; fi
while true; do
  sleep 5
  # MEETING: the owner is at your terminal (/pc). Stay armed, say NOTHING — and do NOT advance
  # `prev`, which is the load-bearing half: the first tick after the flag is gone sees the whole
  # difference and fires exactly ONCE, so the meeting costs you no notifications and loses no wake.
  # It sits ABOVE the read for the same reason: a failed read during a meeting must not spend a
  # strike or raise the blind alarm into the terminal the owner is talking in.
  if [ -f "$sup/.meeting" ]; then
    continue
  fi
  if read_fp; then
    fails=0
    if [ -n "$prev" ] && [ "$FP" != "$prev" ] && foreign_change; then
      echo "CHANNELS CHANGED on $ARGUMENTS — read every channel from your last entry down, act on it, append your entries."
    fi
    prev="$FP"
  else
    fails=$((fails + 1))
    if [ "$fails" -eq 1 ]; then mark_unreadable "$FP_ERR"; fi
    if [ "$fails" -eq 12 ]; then
      echo "WATCHER BLIND — the channels have been unreadable for about a minute ($FP_ERR failing). This is NOT a change notification: read them yourself, and expect the machine to be out of memory or disk."
    fi
  fi
done
```

**THE RULES THIS SCRIPT ENCODES — every one is load-bearing, none is a preference. The accounts
below are why; read the relevant one BEFORE you propose changing, relaxing or "improving" any of
them, because every one of these looks like ceremony from the outside and that is exactly how each
came to be broken the first time.**

- **A failed read is NOT a change.** `read_fp` checks each command's status and leaves `prev`
  untouched when it cannot read.
- **Your own append is not traffic.** `foreign_change` suppresses only when EVERY changed channel is
  provably yours, per channel and never per tick. Any doubt fires.
- **Never narrow the fingerprint to a text pattern.**
- **Use a Monitor, never a `run_in_background` Bash task**, and never re-introduce re-arming or a
  turn-start baseline.
- **The `.meeting` check stays in that exact shape** — above the read, `continue` without advancing
  `prev`. The APP writes and removes that file.
- When it wakes you: read ALL channels, act, write your entries, end your turn. The monitor keeps
  running — you do not touch it again.

**A failed read is not a change, and the old `cat` pipeline could not tell you which had happened.**
It discarded every exit status — the pipeline's status is `cut`'s, which succeeds on anything — so a
read that could not run fired anyway. **One failed read produced exactly two phantom wakes**, one
going into the failure and one coming out. Measured on 2026-08-14: this monitor fired on changed
channels upwards of a hundred times in a day and the large majority found nothing new, while an
implementer's channel that had not been touched for 27 minutes woke it four times.

**YOUR OWN APPEND IS NOT TRAFFIC, and on this watcher that is most of it.** You write to every
channel you watch, so the majority of the changes you woke for were your own briefs — and a wake is
a full context reload. `channel-append.sh` records, from inside the lock, the size a channel had when
your unbroken run of writes to it started and the fingerprint your write left; `foreign_change` fires
unless EVERY changed channel is provably yours. **Per channel, never per tick:** if imp-1 reports
while you are briefing imp-2, judging the tick as a whole would call the whole change yours and lose
the report. Any doubt fires — a channel you have never seen, a missing record, a record that does not
match. The cost of the per-file fingerprint is two commands per channel instead of one `md5sum` for
all of them; that is the price of being able to tell whose write it was, and a failed read is still
handled exactly as below.

**Your shape was the worse of the two, and this is why `cat` is gone.** `cat … | md5sum` hashes
whatever reached the pipe, so a `cat` that fails entirely hashes EMPTY INPUT and yields
`d41d8cd98f00b204e9800998ecf8427e` — a perfectly valid-looking digest, with exit status 0. There is
no output you could inspect to tell that apart from a real fingerprint, which is why the fix is not a
retry or an emptiness check but a change of instrument: the commands are run PER FILE and each
status is checked, so a file that could not be read is visible as a failure rather than as a change,
and nothing has to buffer every channel's bytes on a machine that is short of memory. (This sentence
used to describe one `md5sum` given all the files at once; that stopped being true when the
fingerprint went per-file to tell your own appends from everyone else's.) `nullglob` is what keeps an
orchestration with no implementers yet from looking permanently unreadable.

`read_fp` keeps `prev` untouched when it cannot read, so an append that lands during a failed spell
still fires on the next successful read — nothing is lost by waiting. After twelve consecutive
failures the loop says it is blind, once, in words that cannot be mistaken for traffic.

**The `.meeting` check is not optional and not decoration.** Without it every append during a meeting
prints a wake notification into the terminal the owner is trying to talk in — which is the complaint
that created this mode. Without the `continue`-without-advancing shape, silence would swallow the
resumption wake instead of deferring it, and you would sit deaf until some unrelated append arrived.
The APP writes and removes that file; you never create it, and you never delete it to get your
notifications back.


**Why a Monitor and not a `run_in_background` Bash task — this is measured, not preference.** On
2026-08-07 twenty-nine background watchers were killed across four sessions of one orchestration,
several of them in the SAME SECOND in different sessions (12:02:35: supervisor ×2, communicator;
12:08:31–33: supervisor, imp-1, communicator). Every one of them was a Bash `run_in_background`
task. **In the same orchestration, on the same machine, across those same instants, a persistent
Monitor survived 41+ minutes and delivered five times without a single kill.** Something outside
the app and outside the sessions reaps background Bash tasks; it does not touch Monitors.

**Two failure modes disappear with this shape, so do not "improve" it back:**

- **No re-arming.** The old watcher had to be re-armed at every turn end, which is a step that runs
  on memory alone — miss it once and the orchestration stalls silently.
- **No baseline race.** The old watcher captured a baseline that had to be taken at turn START, and
  taking it at arm time made everything that arrived mid-turn invisible forever (an implementer
  once sat 35 minutes on a brief that was already in its file). A persistent monitor holds its own
  `prev` continuously, so there is no window in which a change can be missed.

When it wakes you: read ALL channels (there may be several new entries), act, write your entries,
end your turn. The monitor keeps running — you do not touch it again.

**Never narrow the fingerprint to a text pattern.** It hashes the WHOLE file on purpose. A watcher
that greps for a phrase (`FROM supervisor`, a subject wording) is only as reliable as the writer's
consistency — and on 2026-08-07 a supervisor wrote its headers three different ways, so a
pattern-anchored watcher stayed perfectly healthy and never fired. Any byte that changes is traffic.

**If you ever see it stop** (a `killed`/stopped notification for it), arm a fresh one immediately;
that is the one case where re-arming is your job.

**Nothing wakes you except this monitor and the owner.** A monitor fires only when SOMEONE ELSE
writes. If you end a turn with your OWN work unfinished and nobody is going to write to you, you
will sleep until spoken to — so never end a turn mid-task expecting to continue by yourself.
Finish the step, or hand it to an implementer, or say in your entry that you are waiting.

**On resume you may see notifications about orphaned/stopped background tasks from a previous
session** — those died with that session. Expected; ignore them and arm your monitor as part of
the boot.

Now execute the boot sequence.
