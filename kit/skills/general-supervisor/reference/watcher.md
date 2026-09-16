Arm the one your `runners.general.wake` names; if you do not know, arm the watcher-mode script.

## Watcher mode — when you decide

Arm it ONCE, at the end of your boot sequence, with the **Monitor** tool and `persistent: true`:

```
Monitor(
  description: "general channel traffic",
  persistent: true,
  command: <the script below>
)
```

```bash
gc="${AIORCH_SUPERVISION_ROOT:-$HOME/.claude/supervision}/general/channel.md"   # = ./channel.md in your working directory

# Sets FP, or returns non-zero with FP_ERR naming the command that failed. A read that FAILED is
# not a read that saw something different — see below.
read_fp() {
  FP=""; FP_ERR=""
  local size hash
  if ! size="$(wc -c < "$gc" 2>/dev/null)" || [ -z "$size" ]; then FP_ERR="wc -c"; return 1; fi
  if ! hash="$(md5sum "$gc" 2>/dev/null)"  || [ -z "$hash" ]; then FP_ERR="md5sum"; return 1; fi
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
  record="$gc.self-write.supervisor"
  [ -f "$record" ] || return 1
  start="$(grep -m1 '^start=' "$record" 2>/dev/null | cut -d= -f2- | tr -d ' ')"
  after="$(grep -m1 '^after=' "$record" 2>/dev/null | cut -d= -f2-)"
  [ -n "$start" ] && [ -n "$after" ] || return 1
  [ "$after" = "$FP" ] || return 1
  [ "$start" -le "${prev%% *}" ] 2>/dev/null
}

# The watcher drops a FACT; the APP writes the record. Never write the log file from here.
mark_unreadable() {
  local orch="${AIORCH_SUPERVISION_ROOT:-$HOME/.claude/supervision}/general"
  [ -d "$orch" ] || return 0
  printf '%s\n%s\n%s\n%s\n%s\n%s\n' "watcher" "the general channel fingerprint" "$1 failed" \
    "general supervisor" "" "took the fingerprint as unknown rather than as a change" \
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
      echo "GENERAL CHANNEL CHANGED — read from your last entry down, act, reply."
    fi
    prev="$FP"
  else
    fails=$((fails + 1))
    if [ "$fails" -eq 1 ]; then mark_unreadable "$FP_ERR"; fi
    if [ "$fails" -eq 12 ]; then
      echo "WATCHER BLIND — the general channel has been unreadable for about a minute ($FP_ERR failing). This is NOT a change notification: read the file yourself, and expect the machine to be out of memory or disk."
    fi
  fi
done
```

**A failed read is not a change.** The old loop discarded `md5sum`'s exit status and always returned
success, so a read that could not run produced an empty fingerprint, compared unequal to the real
one, and fired — **one failed read, two phantom wakes**, with nothing recording that a read had
failed. `read_fp` now keeps `prev` untouched when it cannot read, so an append that lands during a
failed spell still fires on the next successful read; and after twelve consecutive failures it says
plainly that it is blind rather than going quiet on you. The owner's traffic is what is at stake
here, so the silent half matters more than the noisy one.

**YOUR OWN APPEND IS NOT TRAFFIC.** A fingerprint cannot tell whose write changed the file, so every
entry you wrote used to wake you — and a wake is a full context reload, spent to learn that you had
written something you already knew you had written. `channel-append.sh` now records, from inside the
lock, the size the channel had when your current unbroken run of writes started and the fingerprint
it left; the watcher fires unless BOTH say the change was yours alone. The two-fact rule is the
load-bearing part: if the owner writes and you append a minute later, the file still carries exactly
the fingerprint your write left, and suppressing on the fingerprint alone would sleep through them.
**Never weaken this to the fingerprint on its own** — the saving is small and what it costs is the
owner's message.

**Why a Monitor and not a `run_in_background` Bash task — this is measured, not preference.** On
2026-08-07 twenty-nine background watchers were killed across four sessions of one orchestration,
several in the SAME SECOND in different sessions; every one was a Bash `run_in_background` task,
while a persistent Monitor survived those same instants for 41+ minutes. This shape also removes
the re-arm obligation and the baseline race — the monitor holds `prev` continuously, so anything
arriving while you work cannot fall into a gap. Channels are APPEND-ONLY — never `Write` one.

**If the monitor ever stops** (a `killed`/stopped notification for it), arm a fresh one immediately.

**On resume you may see notifications about orphaned/stopped background tasks from the previous
session** — those died with that session. Expected; ignore them, never investigate them, just arm
your monitor as part of the boot.

## Ticket mode — when the app decides

Arm it the SAME way — ONE persistent Monitor, `persistent: true` — only the command changes:

```
Monitor(
  description: "wake ticket, general",
  persistent: true,
  command: <the script below>
)
```

```bash
# TICKET MODE. The app decides whether you should take a turn, with the same policy it uses for a
# headless session, and says so by writing this file. You carry the message; you do not decide.
#
# Everything the watcher-mode script above does — fingerprinting the channel, proving a change was
# not your own write, the blind-alarm strike counter — is GONE here, because none of it was ever your
# question. It was a second implementation of a decision the app already makes, with no cursor, no
# digest, and no way to tell your own append from the owner's message.
#
# The ticket is read as RAW TEXT, never parsed as JSON — no jq, not guaranteed on every machine. Any
# change in its bytes is a new ticket; a missing file is silence, not an error.
ticket="${AIORCH_SUPERVISION_ROOT:-$HOME/.claude/supervision}/general/.wake"   # = ./.wake in your working directory
last=""
while true; do
  sleep 2
  now="$(cat "$ticket" 2>/dev/null)" || continue
  [ -z "$now" ] && continue
  [ "$now" = "$last" ] && continue
  last="$now"
  echo "WAKE — $now. Read the state pack this ticket names in statePackFile FIRST (when it names one): it holds your last entry and the entries that woke you, assembled by the app. Then read the general channel only for what the pack lacks, act, and reply."
done
```

**No `.meeting` check here, on purpose.** Watcher mode never had one for this role either — checked
2026-09-15, only the supervisor's watcher.md carries the `.meeting` rule today. Ticket mode conserves
what the file it is added to already did; it invents nothing.

**No `$ARGUMENTS` substitution needed.** This role's own `$ARGUMENTS` is empty (`/general-supervisor`
takes none) — the general supervisor's folder is the one fixed `general` folder, exactly as
watcher mode above already hardcodes it. The config key for this role is `general`, not
`general-supervisor` — checked 2026-09-15 against `SessionRole_Names.Get_ConfigKey` — which is why
the line above this section reads `runners.general.wake`.

Now execute the boot sequence.
