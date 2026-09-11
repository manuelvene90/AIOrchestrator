# Telegram 429 — study: what the app does, what everyone else does, and what to change

Date: 2026-09-10, night · Author: a session dispatched on the incident report
`~/Downloads/telegram-troppe-chiamate-429.md` (Italian, owner-facing).

**Which copy was read** (CLAUDE.md decision 18): the **main checkout**
`/Users/nvene/Visual Studio/AIOrchestrator`, branch `stage/16-the-supervisor-never-queues`,
HEAD `a67a83b` — i.e. `ours/integration` + the stage/15 merge, **not** the build output, **not**
the installed copy, and **not** the binary running on the VPS (production is `481efc9`, per the
incident report: stage/15 is merged but NOT deployed). Every `file:line` below is `a67a83b`.
No production system was read for this study — re-measuring on the VPS needs the owner's
approval and was not done.

> **CORRECTION, added the same night, after reading `ours/integration` instead of the stale
> checkout.** This study was written against `stage/16` @ `a67a83b`. That commit is **behind**
> `ours/integration` (`c5deae8`). On the real integration head, **Layer 2 of §5 is already
> implemented and merged** by `d22240f` — the promotion block now passes through `attemptDue`,
> and the owner's tap retries under a new `Telegram/RateLimitedRetry_Policy` (Telegram's own
> `retry_after`, capped at 30 s, three attempts) before falling back to stripping the keyboard.
> §3's "CONFIRMED" therefore describes the **deployed** build, not the branch.
>
> What is still true, and is what the follow-up design addresses:
> - **None of it is running.** Production is `481efc9` (service up 20:56); a re-measure at 22:38
>   gave **388 HTTP 429 in the previous hour**, same shape — that is the old code.
> - **Layer 1 does not exist.** No shared cooldown: every caller still attempts, and the client
>   still honours `retry_after` only up to 2 s for edits.
> - **The per-message brake still sleeps inside the tick** (`Wait_ForMessageEdit_Async` →
>   `Task.Delay`), so deploying stage/15 trades a 429 storm for a possible 30 s stall.
> - **Layer 3 does not exist.**
>
> Design for what remains: `2026-09-10-one-door-to-telegram-design.md`.

Labels used throughout: **DOCUMENTED** (primary source), **MEASURED** (someone's numbers,
ours or a third party's), **FOLKLORE** (community consensus with no primary source),
**[unconfirmed]**.

---

## 1. The headline: the incident report is right about the mechanism, and understates the cause

The report's diagnosis is confirmed on the file (§3 below). But two things it says are worth
correcting, and one thing it does not say is the most important finding.

**Correction 1 — the 382 429s are not 382 violations.** The sequence quoted in the report
(`retry after 34 → 31 → 29 → 27 → 25 → 22 → 20 → 20 → 19 → 17 → 15 → 13 → 10`) decrements
in lockstep with wall-clock time at ~2 s per step. That is **one cooldown window counting
down**, being re-reported once per mirror tick — not a fresh violation each time. So the
382/hour figure measures *how often the app asked during a window it had already been told
about*, and the count will collapse the moment the app stops asking, without the underlying
rate necessarily changing at all. **[unconfirmed]** whether asking during a window also
*extends* it — community reports suggest repeated hits can escalate a flood wait, but no
primary source says so. Treat "stop asking" as the fix either way.

**Correction 2 — do not size the control bucket from the observed `retry_after`** (the
report's §5.2, inherited from commit `00bb154`). `retry_after` is a *duration*; a bucket is
a *rate*. The two are not convertible, and `TokenBucket_Gate.cs` L92-98 already argues,
correctly, that a group-wide allowance cannot express a per-message limit: two topics editing
once a minute is two calls a minute, which no group-level bucket would ever hold back. The
right lever is §5's Layer 1, not a bucket resize. If the control bucket is touched at all,
the defensible change is a different one — see §5.4.

**The finding the report does not make: this is a priority inversion, and that is the bug the
owner actually feels.** The owner's tap is a single, high-value, human-initiated event: it is
handled **one-shot, best-effort**, and gives up on the first 429
(`Record_AnsweredQuestion_BestEffort_Async` → log + `Remove_Buttons_BestEffort_Async`, itself
one-shot; `BridgeEngineModel.cs:11886-11921`, `:10282-10295`). PULSE is a machine repaint worth
nothing when the content has not moved: it retries **every 2 s, forever**, because of the
bypass in §3. So the surface with no value holds the allowance open, and the surface the owner
is looking at gets whatever is left of it — which during a cooldown window is nothing. Whatever
else is done, **value must decide who spends the allowance, not arrival order.** A fix that
lowers the 429 count but leaves the tap one-shot has not fixed what the owner reported
("the button did nothing").

---

## 2. Root cause: every Telegram call is made inside the 2-second tick

This is the structural fact from which all three defects follow, and it explains why the
design's own stated intent has not held.

`TokenBucket_Gate.cs` L3-26 sets out the right principle — the rate limit "in ONE place",
because "handling the limit per feature is how it was handled" and "a burst from any one of
them spends the allowance of all the others, invisibly, and the feature that gets the 429 is
whichever happened to go last."

But the one place **cannot hold the wait**. Telegram answers 20–34 s; the mirror tick is 2 s and
holds a channel-write allowance while it runs. So the client caps how long it may honour
Telegram's own answer — `MAXIMUM_CONTROL_RETRY_WAIT = 2 s` for edits/deletes/callbacks,
`MAXIMUM_INLINE_RETRY_WAIT = 10 s` for sends (`TokenBucket_Gate.cs` L104-176) — and past the cap
throws to the caller, "where the per-channel backoff already lives" (L169-172).

That last clause is the leak. There is no per-channel backoff: there are **~40 individual
call sites, each with its own catch and its own policy** — some back off (status line, topic
name), some are one-shot (tap, button removal, narration), some have **no backoff at all** and
retry every 2 s until they succeed (crash-loop alert, stall alert, usage-limit alert: the
"alerted" flag is set only on a confirmed send, so a failure re-fires next tick —
`BridgeEngineModel.cs:1955-1975`, `:2095-2115`, `:3195-3215`, `:3325-3345`). The comment says
"one place"; the code has forty, and the reason is that the one place is not allowed to wait.

**Therefore: the 429 policy cannot be centralised while sends happen inline in the tick.** Any
fix that keeps calling Telegram from inside the tick will keep re-deriving per-feature policy,
which is the fourth or fifth time this repo has done that (`git log` on the Telegram folder:
`6f44f74` "the three gates", `dfb3368` "a second bucket", `269861f`/`9e73dd0` "the long poll
pays its way", `ab2d22d` + `1cd3af7` on PULSE, plus stage/15's per-message brake).

---

## 3. The reported bug, verified line by line

**CONFIRMED, read on `a67a83b`.** `Refresh_TopicStatusLines_Async`,
`BridgeEngineModel.cs:9520-9526`:

```csharp
if (action == Telegram.TopicStatusActions.None
    && session.StatusLineMessageId != null
    && lastText != null
    && renderKey != lastText)
{
    action = Telegram.TopicStatusActions.Edit;
}
```

This runs *after* `TopicStatusLine_Planner.Plan(...)` (L9491-9508) has already applied its
back-off check (`Is_AttemptDue` against `_statusLineFailedAtByOrchId`, `MirrorRetryBackoffSeconds`
= 30 s), and it consults **neither**. And the failure catch (L9679-9696) deliberately does not
update `lastText` — its own comment explains why, and names the exact failure it is trying to
prevent:

```
// The remembered text is deliberately NOT updated, so the next tick retries — but BACKED
// OFF, because a 429 answered at the tick rate inverts the cadence from once a minute
// to thirty times a minute per topic and sustains the throttling that caused it.
```

So after any failed edit, `renderKey != lastText` is true forever, the planner's `None` is
promoted to `Edit` on the very next tick, and the app does precisely the thing this comment
exists to prevent. **The back-off is not weak — it is bypassed.**

Two consequences the report reaches but is worth stating flatly:

- **The tick can be held ~30 s.** `Edit_MessageTextWithButtonRows_Async` awaits
  `_budget.Wait_ForMessageEdit_Async` *before* the HTTP call, and that gate stamps
  `_lastEditUtcByMessageId[messageId]` on **every attempt, not on success** (verified,
  `TelegramSendBudgetModel.cs` L55-80 — pass-through path stamps `now`, wait path stamps
  `lastEdit + gap`). `Refresh_TopicStatusLines_Async` is a sequential `foreach` over sessions
  (L9447) inside the tick (L1573), and everything after it in the tick is queued behind it.
  So with stage/15 deployed, a topic stuck in the promotion loop can **sleep the mirror for up
  to 30 s per stuck message**. Fixing the promotion block is what keeps stage/15 from turning a
  429 storm into a stalled bridge.
- **No test can reach it, and no test can produce a real 429 at engine level.**
  `BridgeEngineModel` is `internal sealed` with no `InternalsVisibleTo`, and the engine-level
  fake (`ScriptedInbound_Fake.Record`) throws a **plain `Exception`**, never a
  `TelegramApiException`. Every classifier in the engine pattern-matches on
  `TelegramApiException.Is_Retryable`, so a scripted failure is always read as a hard refusal,
  never as a retryable 429. The suite admits the gap in its own words
  (`TheBridgeNeverLiesAboutDeliveryTests.cs` L603-611: *"THE REPAINT IS DELIBERATELY NOT
  ASSERTED HERE … not producible from this seam"*). **The missing test seam is one change:
  the fake must be able to throw `TelegramApiException(429, …, retryAfterSeconds)`.** That one
  change is what makes every back-off in this document pinnable.

**A hypothesis that was checked and is FALSE:** that the General dashboard hammers an edit
every tick. It does not — `Push_GeneralDashboard_Async` checks its own
`_generalDashboardFailedAtUtc` back-off **before** the call (L3388-3389) and goes through
`TopicStatusLine_Decider.Decide`, which returns `None` when the text has not moved. It is the
model citizen: the correct shape already exists in this file, ~6000 lines above the broken one.

---

## 4. What everyone else does (evidence base)

### 4.1 What Telegram actually documents

**DOCUMENTED** (`core.telegram.org/bots/faq`): ~1 message/second per chat; ~20 messages/minute
per group; ~30 messages/second global on the free tier. `retry_after` arrives **in the JSON
body** (`parameters.retry_after`), not as an HTTP `Retry-After` header — which is what this app
already parses.

**Nothing is documented about editing.** No per-message limit, no per-chat edit limit, nothing
in the API docs or the changelog. python-telegram-bot's own wiki says it outright: *"Telegram
does not document the precise limits, neither for sending messages nor for other kinds of API
requests."* grammY's flood page is blunter: *"They are unspecified. Deal with it."*

**FOLKLORE**, worth knowing but not evidence: ~20 message **edits** per minute per group
(grammY, sourced from a Telegram support-group thread); one low-confidence content-farm figure
of "~5 edits per message per minute (undocumented, empiric)". **[unconfirmed]** whether a no-op
edit (`message is not modified`) is charged against the quota.

**MEASURED by a third party, and it is our exact bug:** `agno-agi/agno` issue #7360 — an
agent bot doing streaming `editMessageText` per token chunk, ignoring `retry_after` of 32–33 s
and re-firing ~once a second, producing a flood of repeated 429s. Their fix is the one
recommended in §5.1: parse `retry_after`, store a `_rate_limited_until` timestamp, and **skip**
attempts until it passes — not retry-and-fail. Also `tdlib/telegram-bot-api` #318: the cost of
an edit appears to **scale with chat size** (20 edits at 1–2 s intervals in a 100+ member
channel degraded badly; the same pattern in a ~10-member group was fine).

### 4.2 What the serious libraries do

The consistent split across all of them: **proactive shaping** (a queue that paces traffic
before it is sent) **plus reactive retry** (honour `retry_after` when a 429 happens anyway).
Nobody retries inline inside their event loop, and **nobody has a per-message bucket** —
every limiter surveyed buckets by global / per-chat only.

| Library | Proactive shaping | Reactive |
|---|---|---|
| python-telegram-bot | `AIORateLimiter`: nested leaky buckets (`aiolimiter`), 30/s global + 20/60 s per group, `async with group: async with overall:` | opt-in (`max_retries` defaults to **0**); on `RetryAfter` it **halts all requests** for `retry_after + 0.1 s` — a global pause |
| grammY | `transformer-throttler` (Bottleneck): global `reservoir 30 / 1000 ms`; group `maxConcurrent 1, minTime 1000 ms, reservoir 20 / 60 000 ms` | separate `auto-retry` plugin: waits the exact `retry_after`, `maxRetryAttempts` + `maxDelaySeconds` caps |
| aiogram | none shipped; opt-in middleware | `TelegramRetryAfter` is a first-class exception carrying `retry_after` |
| Telegram.Bot (C#) | none found **[unconfirmed]** that this is deliberate | community pattern: wrap the client in **Polly v8** — rate limiter (over `System.Threading.RateLimiting`) → retry honouring `Retry-After` → circuit breaker → timeout; `RateLimiterRejectedException.RetryAfter` is exposed precisely so callers respect it |

Two details transfer directly:

- **PTB halts *all* requests on a 429, not just the failing one.** That is the same insight as
  `TokenBucket_Gate`'s header (one feature's burst spends everyone's allowance) — but applied
  as a *global brake at the moment of the 429*, which is what this app lacks.
- **grammY separates the two concerns into two plugins** and recommends running the reactive
  one even when you do not expect to hit the proactive one's limits. This app has merged them
  into one inline retry with a 2 s cap, which is why neither job is done well.

### 4.3 The platforms that publish more than Telegram

- **Slack** (**DOCUMENTED**): *"Apps may post no more than one message per second per channel"*
  — and `chat.update` sits in the same special tier as `chat.postMessage`. So Slack's own
  answer to "how fast may I repaint a live status message" is **1 edit/second, per channel,
  through a per-channel queue**.
- **Discord** (**DOCUMENTED**): per-route buckets, advertised in response headers, with an
  explicit instruction: *"Rate limits should not be hard coded into your app … your app should
  parse response headers."*

Telegram publishes neither a number nor a mechanism for edits. **Therefore `retry_after` is the
only authoritative signal this app will ever get, and throwing it away above 2 s is discarding
the only ground truth available.** `TokenBucket_Gate.cs` L163-172 says exactly this — *"THEIR
NUMBER BEATS OURS, always … ignoring it in favour of a local guess is how a client keeps
hitting the same wall"* — and then the 2 s ceiling ignores it, for the structural reason in §2.

### 4.4 The named failure mode

Retrying faster than the server's stated cooldown is a **retry storm**, and the universal
guidance (Polly, grammY, PTB, the dev.to Telegram-in-PHP writeup) is: *treat `Retry-After` as
authoritative, do not invent your own backoff, and floor it at ~1 s if the server says 0.*
For a high-frequency UI-style update the companion pattern is **coalescing**: hold the latest
desired state and repaint at most once per window, discarding intermediate states — never queue
every intermediate render.

---

## 5. Recommendation — three layers, in this order

### Layer 1 — make a 429 a *state* the whole app can see, and SKIP while it holds

One shared, in-memory cooldown map at the single choke point that every call already passes
through (`TelegramApiClientModel.Post_Async` / `ITelegramSendBudget`), keyed by **chat/topic**
and by **message id**, written from `retry_after` on every 429 — clamped, as
`Read_RetryAfter` already does.

While a cooldown holds, the choke point **refuses immediately** ("not now", with the deadline)
instead of attempting. It does not sleep, so the tick is never held. Callers therefore stop
firing *without any of the ~40 call sites changing its own policy* — which is the only way this
gets centralised while §2's structure stands.

Why this first: it is the smallest change that kills the storm regardless of which feature is
at fault, it is exactly the fix the agno bug adopted, and it makes the 429 count in the journal
a *measurement of the underlying rate* instead of a measurement of our own asking.

Also in this layer: the per-message gate must **skip, not sleep**, for tick-driven traffic.
Sleeping up to 30 s inside the tick (§3) trades a 429 storm for a stalled bridge.

### Layer 2 — fix the reported bug, and build the seam that can pin it

1. The promotion block (`:9520-9526`) must go through the same `Is_AttemptDue` the planner
   uses — a button-only change is a legitimate reason to edit, but never a reason to ignore a
   cooldown.
2. The owner's tap must **retry honouring `retry_after`** instead of giving up one-shot, and it
   must outrank a PULSE repaint when both want the allowance (§1, the priority inversion).
3. **`ScriptedInbound_Fake` must be able to throw `TelegramApiException(429, …, retryAfter)`.**
   Without it nothing in this document is testable at engine level; with it, all of it is.
   This is the highest-leverage line in the whole study.

### Layer 3 — decide whether to decouple sending from the tick

The structural answer, and what every library in §4.2 actually ships: the tick stops calling
Telegram. It **publishes the desired state** — for a status line, into a *slot it overwrites*,
not a queue it appends to — and a single writer per chat drains, paced proactively, honouring
`retry_after` in full because nothing is holding the tick.

What that buys, in the app's own terms: coalescing becomes free and automatic (PULSE only ever
paints the newest text); priority becomes expressible (a tap outranks a repaint); Telegram's own
number can finally be honoured at 34 s instead of 2 s; and the ~40 catches collapse into one
policy, which is what `TokenBucket_Gate`'s header asked for in the first place.

This is a real piece of work and it should be a decision, not a drive-by. Layers 1 and 2 stand
on their own and do not block it.

### 5.4 If the control bucket is touched at all

Not from `retry_after` (§1, correction 2). The only defensible re-derivation available is the
**FOLKLORE** figure that edits mirror the group ceiling — ~20 edits/minute per group — which
would make the control bucket `20/min, capacity 10`, the same arithmetic as
`DEFAULT_CAPACITY`, rather than today's `60/min, capacity 30`, which the comment itself calls
a guess (L66-73). Do this only *after* Layer 1 is measured: with the storm gone, the steady
rate may already be far under any ceiling, and tightening a bucket that is not the binding
constraint just adds latency.

---

## 6. Acceptance criteria

Keep the report's §6 measurement, and add the two things a 429 count alone cannot show:

- **Done when**, over one hour of production with ≥2 live topics: zero
  `Answered-question edit failed` / `Button keyboard removal failed` lines; PULSE 429s under
  5/hour; **and** no mirror tick longer than 5 s (Layer 1's skip-not-sleep, which the current
  journal cannot show and should be logged); **and** for every 429 observed, at most **one**
  attempt against that cooldown window (the storm is what we are removing, not the 429 itself).
- **In the suite**: an engine-level test that drives a real 429 through the fake and asserts
  the next attempt comes no sooner than `retry_after` — impossible today (§3), which is why
  Layer 2.3 comes with Layer 2.1.

## 7. Sources

1. `core.telegram.org/bots/faq` — the only primary Telegram source with numbers; none of them
   about edits.
2. `github.com/python-telegram-bot/python-telegram-bot/wiki/Avoiding-flood-limits` — the
   clearest statement that Telegram documents nothing precisely.
3. `docs.python-telegram-bot.org` → `telegram.ext.AIORateLimiter` — dual-bucket proactive
   limiter; halts all requests for `retry_after + 0.1 s`.
4. `grammy.dev/plugins/transformer-throttler` and `/plugins/auto-retry`, `grammy.dev/advanced/flood`
   — exact bucket configs, and the documented/folklore split.
5. `github.com/agno-agi/agno/issues/7360` — our exact symptom in another codebase, with the
   skip-until-deadline fix.
6. `github.com/tdlib/telegram-bot-api/issues/318` — edit cost scaling with chat size.
7. `docs.slack.dev/apis/web-api/rate-limits` — 1 message/second per channel, `chat.update`
   included: the published answer for a live-updating message.
8. `docs.discord.com/developers/topics/rate-limits` — parse the buckets, never hard-code them.
9. `pollydocs.org/strategies/rate-limiter.html` + `App-vNext/Polly` wiki — the .NET-native
   `Retry-After`-aware pipeline, if this is ever rebuilt on standard parts.
