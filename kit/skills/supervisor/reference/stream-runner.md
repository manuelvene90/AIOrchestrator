# `AIORCH_RUNNER=stream` — the bridge holds one process open and talks to it

Your boot command printed `AIORCH_RUNNER`. This file applies when it says `stream`. You have no
terminal and no window: the bridge started ONE `claude` process for you and wakes you by writing on
its stdin. Everything below replaces the corresponding rule in the main protocol.

## THE SHAPE OF YOUR FINAL MESSAGE — read this before anything else

The bridge takes your final message and writes it into the channel AS THE ENTRY. So:

```
supervisor online — probe — /tmp/probe-repo        <- line 1 IS the subject
                                                   <- blank
Ready. Text me what you need.                      <- the body
```

**NO preamble, ever.** Not "Responding to the owner's greeting:", not "Here is my update:", not a
heading. Whatever occupies line 1 BECOMES the subject the owner reads in their topic list — measured
2026-09-06, on the first live stream turn against this file: the session wrote a lead-in sentence
above its own subject, and that sentence is what would have been filed as the entry's subject.

The same applies to anything a machine prints before you: a `SessionStart` banner lands in your
first message, and a session that repeats it makes the banner its subject.

**And never write that final message while a background sub-agent is still running** — wait for every
agent to return first, because a late return re-opens the turn and a later message replaces the
entry. (The bridge files the superseded one for you and tells you it did; the entry the owner reads
is still the LAST thing you said.)

## 1. The environment is resolved by a command, first, always

This is not different for stream, it is the same rule the main protocol opens with, and it is
repeated here because it is the one whose absence is silent:

```bash
echo "ROOT=${AIORCH_SUPERVISION_ROOT:-$HOME/.claude/supervision}"; env | grep '^AIORCH_' | sort
```

The Read tool does not expand variables. A path typed from memory is the DEFAULT root, which under a
bridge started with `--root` does not exist. In the stage-1b live run the first version of a test
role protocol omitted this line and a member lost its first brief.

## 2. NO Monitor, no watcher, no `sleep`

Do not arm anything and do not poll. You are woken by the bridge writing to your stdin. The whole
"definition of done" of `reference/watcher.md` does not apply in this mode — that file is for a
session that owns its own process.

## 3. Your FINAL MESSAGE is your entry — and you never write your channel yourself

First line is the SUBJECT, then a blank line, then the body. The bridge writes the entry from that
message.

**Do NOT also append it with `channel-append.sh`.** In the stage-1b live run a member that had not
been told this wrote its report TWICE — once with Bash, once from the bridge.

**The first line is the subject and nothing else.** Any `SessionStart` banner the machine prints
lands in your first message, and a session that repeats it makes the banner the subject of its own
entry.

## 4. A question ENDS the turn, exactly as an answer does

Ask, and stop. There is no tool-denial hook holding you: the turn simply finishes, and the bridge
does not send you another line until the owner has answered. "One question closes the turn" is a
property of the transport here, not a rule you have to remember to obey.

## 5. YOU ARE WOKEN BY EVERY CHANNEL YOU LISTEN TO — the owner's, and each member's spoke

The bridge resolves your channels from the roster on every tick: `owner-channel.md` plus the spoke of
every OPEN member. **A member writing in its spoke starts a turn for you**, with the owner having said
nothing at all.

**Do NOT `cat` your spokes at the end of a turn.** That was the patch for a single-source trigger,
and the trigger is not single-source any more: doing it now re-reads files the bridge has already
quoted to you and files the same report a second time.

The prompt names the channel each message came from:

```
--- from imp-1 (channel.md) ---

## [4] FROM implementer — 2026-09-06 12:30 — REPORT — parser
…
```

**One turn can carry several channels at once.** Entries that arrive together ride together, oldest
first, with the owner ahead of a spoke when the two arrive at the same moment. It is not one turn per
channel, and nothing you were shown is repeated on a later one.

## 6. ADDRESS EACH PART OF YOUR ANSWER — `TO: <channel>`

Your final message is your channel entr**ies**, plural. A line reading `TO: <channel>` on its own opens
a block that runs to the next such line:

```
TO: owner
Status — imp-1 is done

The parser is merged. Briefing imp-1 on the writer next.

TO: imp-1
VERDICT — accepted

Good. Next: the writer, same shape.
```

- Inside a block, §3 still holds: **first line the subject, a blank line, then the body.**
- The prompt lists the channels you may address in that turn. It is the roster as it stands, so a
  member added since your last turn is already in it.
- Text before the first `TO:` goes to the OWNER — and it becomes an entry OF ITS OWN, not a preamble
  folded into the block after it. So address every part, or write no preamble.
- A block addressed to a channel you do not have is written to the owner's channel with a note saying
  where you meant it to go. Nothing you write is dropped.

**ANSWER A MEMBER IN ITS OWN CHANNEL.** A verdict written anywhere else does not reach the session it
is about: the app decides "has this member been answered" from the last `FROM supervisor` entry in
THAT member's channel, so a member left without one stays *awaiting review* for ever and puts a nudge
on the owner's phone every few minutes.

**And you never write a channel file yourself, in either direction.** Briefing a member is a `TO:`
block, not `channel-append.sh`. §3 says you do not write your own channel; this says the same about
theirs.

## 7. YOUR FIRST TURN MAY CARRY NOTHING AT ALL — that is the BOOT TURN

If you are the supervisor of an orchestration, the bridge starts you ONCE with your role command and
nothing else, before anybody has said anything to you. It has to: the owner has no topic to type into
until you have written your greeting, and the greeting is what creates the topic — so if the bridge
waited for an entry, nothing would ever arrive and you would never run. Measured 2026-09-06, live from
the phone: a full crew sat complete and unreachable for six minutes because of exactly that.

So on that turn: follow the boot sequence, file the greeting, END. Do not go looking for work that has
not arrived — there is none, and an empty channel is not a problem to investigate.

**If a task was already waiting, it arrives in the SAME turn as a second message**, after the role
command, in the ordinary prompt shape of §5. So a first turn is either "role command, nothing else" or
"role command, then traffic": never a third thing.
