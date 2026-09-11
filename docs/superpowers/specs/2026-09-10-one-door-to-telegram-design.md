# One door to Telegram — design

Date: 2026-09-10, night · Branch: `stage/17-one-door-to-telegram`, cut from `ours/integration`
@ `c5deae8` · Companion study (evidence, sources, measurements):
`2026-09-10-telegram-429-study.md`.

**Which copy this design was read against** (CLAUDE.md decision 18): `ours/integration` @
`c5deae8`, in the worktree `../AIOrchestrator-onedoor`. **Not** the main checkout (which sits on
`stage/16` @ `a67a83b`, behind), **not** the build output, **not** the running VPS binary
(`481efc9`).

---

## 1. Where this starts

Three defects were identified in the study. One of them is already fixed:

| | State on `ours/integration` @ `c5deae8` | Running in production? |
|---|---|---|
| The PULSE promotion bypassing its own back-off | **Fixed** — `attemptDue` is now a condition of the promotion (`d22240f`) | No |
| The owner's tap giving up on the first 429 | **Fixed** — `Telegram/RateLimitedRetry_Policy`: Telegram's own `retry_after`, capped 30 s, 3 attempts, off the inbound loop (`d22240f`) | No |
| No shared "we were told to wait" state | **Absent** | — |
| Sends made inline inside the 2 s tick | **Unchanged** | — |
| stage/15's per-message brake sleeping inside the tick | **Unchanged** (`Wait_ForMessageEdit_Async` → `Task.Delay`) | No |

Production runs `481efc9` (service up 2026-09-10 20:56:30). A read-only re-measure at 22:38 gave
**388 HTTP 429 in the preceding hour**, unchanged in shape from the 21:52 measurement. That is the
old code: the two fixes above have never run.

**Therefore this design is gated on a measurement that does not exist yet.** `d22240f`'s own
commit message attributes **357 of 382** 429s to the promotion bug it fixes. If that holds in
production, the storm largely ends at deploy, and what remains is an architectural choice made
calmly rather than an incident response. Building §4 before observing §3 would be designing on an
unmeasured baseline.

## 2. The owner's four decisions (2026-09-10)

Recorded here because they constrain everything below and must not be silently re-litigated:

1. **Priority under scarcity**: the owner's taps and their receipts → the agents' messages (the
   conversation) → the cosmetic surfaces (status lines, topic names, dashboard).
2. **What may be discarded**: only a *superseded cosmetic repaint*. A message of the conversation
   is never lost, even at the cost of delay.
3. **Scope**: all outbound traffic goes through one door.
4. **Delivery**: relief first, cure second — with a measurement between them.

## 3. Step one — deploy what exists, then measure

Not code: an operation, and the owner's to authorise.

**Done when**, in the hour after the deploy of `c5deae8` (or its successor):

- `journalctl -u aiorchestrator --since "-60 min" | grep -c "HTTP 429"` is **under 20** (from 388).
- Zero `Answered-question edit failed` / `Button keyboard removal failed` lines.
- **No tick longer than 5 s.** This is the new risk that arrives with the same deploy: stage/15's
  per-message brake sleeps rather than skips, so a message edited twice inside 30 s now parks the
  whole tick. With the promotion fixed this should be rare — but "should be" is why it is measured
  rather than assumed. There is no tick-duration line in the journal today; §4.1 adds one, and it
  is the one piece of §4 worth doing **before** the measurement rather than after.

If the 429 count does **not** collapse, the cause is not the promotion block, and §4 must be
re-derived from the new breakdown rather than built as designed here.

## 4. What remains to build

### 4.1 `OutboundCooldowns` — the note on the door

**The problem it solves**, and it survives the fix in §1: when Telegram says "wait 30", that answer
is known to exactly one call site — the one that received it. Every other surface keeps ringing the
same bell. Today three of them cannot even back off: the crash-loop, stall and usage-limit alerts
set their "alerted" flag only on a confirmed send, so a failing send re-fires every 2 s with no
back-off of any kind. The promotion fix does nothing for those.

**Shape.** A pure policy object plus a small holder at the single choke point every metered call
already passes through (`TelegramApiClientModel.Post_Async`).

- `Note_RateLimited(target, retryAfterSeconds, now)` — records `notBeforeUtc`, clamped by the
  existing `MAXIMUM_HONOURED_RETRY_AFTER_SECONDS`.
- `Is_Held(target, band, now)` — true when the door is shut for this target *and* this band.
- `target` is the exact thing Telegram refused: the **message** for an edit, the **chat** for a
  send. A 429 also shuts the chat for the band that caused it **and every band below it**.

**The band exception, stated as a deliberate bet.** A band *above* the one that caused the
cooldown may still make **one** attempt while it holds — no retries. This is what makes decision
2.1 real: the owner's tap goes out while a status line is in punishment. It is **[unconfirmed]**
whether attempting during a flood wait extends it; no primary source says either way. One
high-value call is a risk worth taking, thirty low-value ones a minute is not. Revisit if the
measurement in §3/§6 shows cooldowns lengthening.

**Held is not failed.** A held call must not travel the same path as a refusal, or every one of the
~40 catches logs a warning per tick and a 429 storm becomes a log storm. Two rules:

- The choke point returns a distinct outcome (`TelegramHeldException` carrying `NotBeforeUtc`), so
  a caller can tell "not now" from "no".
- **The log line belongs to the cooldown, not to the caller**: one line when a window opens
  (`held until X, from retry_after=N`), one when it closes, nothing in between. 388 lines an hour
  become two per window.

**Skip, do not sleep.** `Wait_ForMessageEdit_Async` currently awaits up to 30 s *inside the tick*.
For tick-driven traffic it must return "held" instead. Sleeping trades a 429 storm for a stalled
bridge — §3's third criterion exists to catch exactly this.

**Also in this slice**: a tick-duration log line, and a warning above 5 s. It is three lines of
code and it is the only way §3's third criterion can be checked at all.

### 4.2 The door — `OutboundQueue` + `OutboundPump`

**The structural cause, unchanged by `d22240f`:** every Telegram call is made inside the 2 s tick,
which is a sequential list of ~20 steps. Telegram asks for 20–34 s; the tick cannot wait; so the
client caps how long it may honour that answer (2 s for edits, 10 s for sends) and throws to "the
caller, where the per-channel backoff already lives". There is no per-channel backoff — there are
~40 call sites, each with its own policy, three of them with none. `TokenBucket_Gate`'s own header
asks for the limit to live "in ONE place"; it cannot, while the one place is forbidden to wait.

**The shape.** The tick stops calling Telegram. It **publishes intents**; one pump sends them.

- **`OutboundIntent`** — band, kind, target, the call to make, and what to do with the outcome.
- **Two kinds, and the whole design turns on the difference:**
  - **Job** — must be delivered, in order. The conversation, alerts, receipts.
  - **Slot** — latest-state-wins, keyed (`statusline:<orchId>`, `topicname:<orchId>`, `dashboard`).
    Publishing overwrites any pending intent with the same key. Painting a superseded state is
    always waste; this is decision 2.2 expressed as a data structure rather than as a rule someone
    must remember.
- **`OutboundQueue`** — per chat: three bands; jobs FIFO by `OrderKey` (the channel file path),
  slots a dictionary. No clock of its own.
- **`OutboundPump`** — the only caller of `ITelegramApiClient`. One pump; **each chat serialised,
  chats concurrent** (Telegram's limits are per chat). Signalled by `System.Threading.Channels`
  (repo precedent: `Running/StreamTurn/StreamSessionProcess.cs`). It honours the existing token
  buckets, the §4.1 cooldowns, and `retry_after` **in full** — the 2 s and 10 s caps exist only
  because a tick was being held, and nothing is held any more.

**Policy stays pure.** The band/kind/order/coalescing decision is a pure function over descriptors
(`OutboundQueue_Planner.Decide_Next(state, now)`), never touching the call delegate it carries.
This is the repo's established pattern and the only way any of it is testable, since
`BridgeEngineModel` is `internal sealed`.

### 4.3 The two invariants that a careless version would break

**Crash safety, obtained by adding nothing.** A conversation message is not lost today because the
tailer's offset advances only when the send is confirmed (`Mirror_Append_Async` →
`Settle_MirrorAttempt_Async`). The settle must therefore be driven by the **pump's outcome**, not
by a successful enqueue. Keep that and the consequence is free: if the process dies with a full
queue, no offset advanced, and the restart re-emits exactly what was not delivered.

**The channel files already are the durable queue.** So the queue is **not persisted** — no second
record that can disagree with the first, no growth in `.bridge-state.json`. Intents not derived
from a file are either cosmetic (rebuilt from state next tick) or alerts (whose flag is already set
only on a confirmed send, which re-fires next tick — the existing pattern, preserved).

**Order within a conversation.** Jobs are FIFO per `OrderKey`; a retrying head blocks its own
channel and no other.

### 4.4 The one exception, and the one non-change

**`answerCallbackQuery` stays inline and unqueued.** Telegram invalidates a callback query after
about ten seconds; queueing it would guarantee lateness. It is one tiny call and the existing 2 s
Control retry is right for it. Written here as a choice with its reason, not left to happen.

**DND is untouched, deliberately.** Deferred works today because under mute the tick does not tail
the channels, so no conversation intent is produced at all, while the two silent surfaces keep
updating. The pump therefore needs no knowledge of mute. Putting a mute check inside it would feel
natural and would break the catch-up burst.

### 4.5 What this buys, in the terms the owner asked for

- **Efficiency**: at most one pending repaint per surface, ever; superseded ones never travel.
- **Scalability**: today N topics produce N × ~30 attempts a minute against a per-chat wall. After:
  the rate is the pump's, independent of N. The honest limit that remains is how many distinct
  messages are kept fresh — and the designed behaviour is that **with many topics the cosmetic
  surfaces lag; the conversation does not**. Never a 429, never a loss, just slower paint.
- **Correctness**: one policy in one place, which is what the header of `TokenBucket_Gate` asked
  for and what six previous per-feature attempts could not deliver (`6f44f74`, `dfb3368`,
  `269861f`, `9e73dd0`, `ab2d22d`, `1cd3af7`).

## 5. Testing

**The seam that unlocks everything.** Engine-level tests drive a fake Telegram client whose
scripted failures throw a plain `Exception`. Every classifier in the engine matches on
`TelegramApiException.Is_Retryable`, so **no engine test can produce a real 429** — a scripted
failure always reads as a hard refusal. The suite says so itself
(`TheBridgeNeverLiesAboutDeliveryTests.cs`: *"not producible from this seam"*). Teaching the fake
to throw `TelegramApiException(429, …, retryAfterSeconds)` is small and is a precondition for every
behavioural test below.

**Pure** (fast, exhaustive): cooldown open/close/band-exception arithmetic; queue ordering;
slot overwriting; band selection; `retry_after` clamping.

**Wire** (real client, fake transport): a **Control-class** 429 — today only the Message class is
pinned, and Control is both the tighter ceiling and the one every edit uses.

**Behavioural, at engine level** — the five that matter:
1. A 429 on a status line produces no second attempt before the deadline.
2. A tap goes out while a status line's cooldown holds (the band exception, 4.1).
3. A mirror append whose send fails does not advance the offset, and is re-delivered.
4. Two appends on one channel arrive in order when the first one retried.
5. No tick exceeds 5 s while a cooldown is open.

## 6. Acceptance criteria

- **§3 (deploy)**: 429s under 20/hour; zero owner-facing edit failures; no tick over 5 s.
- **§4.1**: for every cooldown window, **at most one** attempt per band against it — the storm is
  what is being removed, not the 429 itself; and at most two log lines per window.
- **§4.2**: the five behavioural tests above green; the full suite green (0 red, 9 skipped —
  compare the names, not the count); trial merge into `ours/integration` clean.

## 7. Risks

- **The band exception may extend a flood wait.** `[unconfirmed]`, deliberate, revisit on measurement (4.1).
- **The measurement may invalidate §4.2's premise.** If the 429s do not collapse after the deploy,
  the breakdown must be re-read before the door is built. §4.1 stands either way — the three alert
  sites have no back-off at all, and no fix in `d22240f` touches them.
- **Moving ~40 call sites is the largest edit in this repo's history of this subsystem.** It is
  mechanical but wide; it is the reason §4.2 is a separate slice from §4.1, and the reason the
  invariants in §4.3 are written before any of it is moved.
