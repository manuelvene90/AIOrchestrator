# The brake that watches work, not the clock — and the advisory that does not arrive

**Date:** 2026-09-11 · **Status:** PROPOSED, nothing built · **Owner:** Nathan
**Revision 3 (same day):** §9 added — §5.1 and §5.2 measured, and the design built on `stage/28`: the silence brake for members, a two-hour member ceiling, loop detection and a progress note. §9 supersedes §4.0's "nothing has to be invented" and §5's hope that a delivered advisory would stop the kills.
**Revision 2 (same day):** §4.0 added — the silence brake ALREADY EXISTS on the stream runner and the two brakes are swapped between the roles, so this is a reuse and not a design. The supervisor argument in §4.1 as first written was a wrong-altitude measurement and is corrected in place.
**Provenance:** measured on the VPS on 2026-09-11 while verifying the night's consumption, as a
follow-on to `2026-09-10-independent-review/REPORT.md`. Not part of the token plan
(`2026-09-08-token-efficiency-design.md`) — it is a **consequence** of it that nobody priced.

**Copies read.** Code: branch source at `ours/integration` HEAD `c14945d`, which is also what the
VPS serves (`/home/orch/AIOrchestrator`) and what the running daemon was published from
(`/opt/aiorchestrator`, DLL built 2026-09-11 09:33, service active since 09:33:17). Live data: VPS
`orch@159.195.254.120`, **read-only, metadata only** — `usage`, timestamps, tool names, field
presence, `orchestrator.log.jsonl`, `turns.jsonl`. Never the text of a message.

Every figure is tagged `[measured]` or `[estimate]`.

---

## 1. What is happening

The state pack made a member's turn roughly eight times longer — from about two model calls to a
median of sixteen [measured]. The turn deadline did not move with it, and a third of member turns
now die on it.

| implementer turns ending in error | share |
|---|---|
| 2026-09-07 (before the pack) | 18.6 % [measured] |
| 2026-09-08 (before the pack) | 19.0 % [measured] |
| 2026-09-09 (the pack lands) | 30.7 % [measured] |
| 2026-09-10 | 35.1 % [measured] |
| the night of 09-10 → 09-11 | 33.3 % [measured] |

The composition changed, not just the rate. Before, most failures were usage-limit refusals; after,
most are the deadline:

| failure | before the cut | after the cut |
|---|---|---|
| HTTP 429, usage limit | 26 | **18** [measured] |
| no status, no result, `exit_code = -1` | 12 | **63** [measured] |

**[measured]** `journalctl -u aiorchestrator --since 2026-09-09` names them: 57 lines
`Turn <orch>/<member>/<n> was killed at the deadline after 30.0 min`, and **57** matching
`Closing turn … ended — success`.

Two things follow immediately. **The brake fails safely** — every killed turn got its salvage
report, 57 out of 57. And **`duration_ms` is absent on these records, not zero**: a first reading of
this data turned the missing field into `0.0` and concluded the turns never started. They run the
full thirty minutes. The record is kept here because the same mistake is available to the next
reader.

---

## 2. The brake is measuring the wrong thing

**The question: were they stuck, or working?** Model calls in the thirty minutes before each kill,
in five-minute blocks, counting the turn's own calls **and its sub-agents'** [measured, 57 kills]:

| window before the kill | model calls |
|---|---|
| −30 to −25 min | 2,705 |
| −25 to −20 min | 1,035 |
| −20 to −15 min | 789 |
| −15 to −10 min | 998 |
| −10 to −5 min | 974 |
| **−5 to 0 min** | **911** |

A stuck turn decays toward zero before the guillotine. This does not decay. **The 57 turns were
working at the moment they were killed.** (The first block is inflated: it catches the turn's
opening burst, and the aggregation is per member, so a neighbouring turn's calls can land in it.
The claim rests on the absence of decay, which no aggregation artefact produces.)

So: in three days the deadline has cut productive work 57 times and caught a genuine hang **zero**
times. There is no case in this data where it did the job it exists for.

**And the threshold is now mis-set against the work.** Successful turns: median 1.7 min, p90 18
min, max 28.5 min [measured]. A distribution whose maximum sits 1.5 minutes under the ceiling is a
censored distribution, not a comfortable one.

**Raising the ceiling is not free, and the code already says why.**
`ShutdownGrace_Rule.DEFAULT_TURN_TIMEOUT` is 30 minutes, `printRunner.turnTimeoutMinutes` overrides
it, and `Describe_Mismatch_OrNull` refuses silence: the host's `TimeoutStopSec` must cover
`turn + closing turn + margins`, or a service stop during a long turn cuts the drain. Raising the
number therefore touches the systemd unit too — and only moves the wall. The work grew once; it will
grow again.

---

## 3. The other half: the gentle brake does not arrive

The system already has a work-aware brake. `kit/hooks/soft-boundary-check.sh` counts a turn's tool
calls and, past `SOFT_BOUNDARY_CALLS` (default **35**, `:140`), injects one advisory through
`hookSpecificOutput.additionalContext` — reach a stable point, save, report. It advises, never
blocks, which is decision 21 correctly applied.

**[measured]** The turns that later die on the deadline make a **median of 74 tool calls** in their
final thirty minutes (min 20, max 132) — twice the threshold. And **only 25 of the 57 received the
advisory at all.**

That is not a threshold to tune. Fewer than half the turns that most need the gentle brake never
hear it, while the blunt one kills them. The two brakes are not connected.

**Why it misses is not diagnosed.** Candidates, none verified: the counter directory under
`${TMPDIR:-/tmp}` differing inside the session's systemd scope; the once-per-turn latch keyed on
`prompt_id` with a `session_id` fallback; the daily sweep of stale turn directories; sub-agent calls
counting toward the total while only a parent call can carry the delivery. **Diagnose before
changing the number** — a threshold moved under an undiagnosed delivery fault buys nothing.

---

## 4. The design

> Not "has it been quiet for N minutes", but **"is anything alive under this turn, and is it waiting
> for something the system itself told it to wait for"**.

### 4.0 CORRECTION, 2026-09-11 — the mechanism already exists, on the other runner

**This section is a correction to §4.1 as first written, raised by the owner's question "does this
take the supervisor out of the queue?" and it changes the size of the whole proposal.**

The system already has a silence brake. `StreamTurnExecutorModel` kills a stream turn that has
produced no bytes for `limits.streamSilenceSeconds` and says so in a line that names the limit and
the config key that sets it. In production that key is **600 seconds** [measured, VPS
`config.json`], and it has killed **17** supervisor turns since 2026-09-09 [measured, journal].

So the two brakes are **swapped against what the roles need**:

| role | runner | the brake it has | kills since 09-09 |
|---|---|---|---|
| supervisor | stream | **silence**, 600 s | 17 [measured] |
| member (imp / rev) | print | **elapsed time**, 30 min | 57 [measured] |

The role that is idle by design is watched for idleness; the role that works continuously is
watched by a clock. **Nothing has to be invented.** The proposal is to give the print runner the
brake the stream runner already has — a mechanism that is built, tested, and running in production —
not to design a new one. That is a far smaller change than §4.2–§4.4 imply, and better evidenced.

**And the argument in §4.1 below was wrong as argued.** It reads a supervisor's 412-minute median
silence as proof that an inactivity brake would kill every supervisor. That compares the wrong
things: those are gaps inside a long-lived *session*, which for a supervisor span the idle time
*between* turns, while the brake acts *within* a turn. The supervisor already lives under a
10-minute within-turn silence limit and mostly survives it. The table is kept below because the
member rows are still the right numbers for §4.2, and because the mistake is instructive: a
measurement taken at the wrong altitude supports a confident wrong conclusion.

**What this does not settle:** whether those 17 supervisor kills were right. 600 seconds of total
process silence could be a genuine hang or a long think. Nobody has looked. See §8.

### 4.1 Scope: members only, and what the numbers below do and do not show

The longest silence inside one session, own calls plus sub-agents' [measured, all sessions since
09-07]. **Read the member rows; the supervisor row is a session-level figure and is not evidence
about within-turn behaviour — see §4.0:**

| role | sessions | median | p90 | p95 | p99 | max |
|---|---|---|---|---|---|---|
| implementer | 232 | 1.4 min | 3.4 | **10.0** | 419.6 | 419.8 |
| reviewer | 128 | 0.5 min | 2.1 | 3.2 | 227.4 | 418.6 |
| **supervisor** | 7 | **412.5 min** | 909.3 | 909.3 | 909.3 | **909.3** |

All 57 deadline kills to date were members and none was a supervisor [measured] — because the two
runners already carry different brakes (§4.0), not because anyone chose it. The member rows are the
ones that set the threshold in §4.2.

### 4.2 Threshold: the members' own numbers

**95 % of member turns never go quiet for more than 10 minutes** [measured]. A silence threshold in
the 15-minute region leaves essentially every healthy turn alone, against a clock that today kills a
third of them. 30 of 367 sessions have a silence of 5 minutes or more (8.2 %), 24 of them members.

The absolute ceiling stays as a backstop, well above today's 30 minutes, for a turn that emits calls
forever without converging.

### 4.3 Liveness: what counts as "alive"

In order of strength:

1. **A child process of the turn is running.** A build or a test suite emits no model call for
   minutes while being entirely healthy. The p95 of 10 minutes for implementers is consistent with
   exactly this [estimate — the gaps were not attributed to a cause].
2. **A model call by the turn OR by one of its sub-agents.** The parent blocks while a sub-agent
   works; issue #74318 measures a median parent-blocked gap of about nine minutes on comparable
   traffic. Counting only the parent would read a working turn as dead. Every table in this spec
   already counts both, and must.

### 4.4 The three legitimate waits — the app already knows all three

The owner's objection, in his words: *how do you tell "doing nothing" from "waiting for me while I
sleep"?* Measured answer: **members do not wait for the owner — supervisors do**, and the system
already represents every one of these states.

| the turn is waiting for | what the app already holds |
|---|---|
| the owner to answer a question | the `.awaiting-answer` flag, app-managed, self-expiring, already enforced by `supervisor-awaiting-answer-check.sh` |
| a usage limit to reset | the retry appointment (`RetryNotBeforeUtc`), already a dispatcher gate |
| its own long tool call | the child process, observable |

The clock **pauses** on the first two and does not run at all while the third holds. Nothing new has
to be invented or inferred; three facts the system already maintains have to be consulted by one
more reader.

This also explains the p99 of about seven hours in the member rows above: those are usage-limit
waits, not stalls, and a brake that does not consult the appointment would kill exactly the turns
that were correctly parked.

---

## 5. Order of work, and why this order

1. **Diagnose the advisory's 25-of-57 delivery.** Read-only; no production change. It comes first
   because if the gentle brake reaches every long turn, the hard one stops firing on its own and §2
   becomes a much smaller change.
2. **Look at the 17 supervisor silence-kills** before moving that mechanism anywhere. It is the one
   being proposed for reuse, and whether it is currently killing the right turns is unmeasured
   (§4.0, §8). Reusing a brake nobody has checked is how this spec's subject came about.
3. **Give the print runner the stream runner's silence brake**, members only, with the three pauses
   of §4.4 — reusing the existing mechanism rather than writing a second one.
4. **Only then** consider the ceiling, which by that point is a backstop and not a policy.

---

## 6. Done when

- No turn is killed while a call of its own or of a sub-agent landed within the silence threshold —
  measured on the same window shape as §2, not asserted.
- A deliberately hung member turn is still killed. **A brake that has never been shown to catch the
  thing it exists for is the situation this spec is written about**; the new one must be shown, with
  a hang staged on purpose.
- No supervisor is ever killed by it.
- A turn parked on a usage-limit appointment, or on an unanswered owner question, survives a window
  longer than the threshold.
- Implementer turns ending in error return toward the pre-pack 19 % [measured baseline], with the
  429 share unchanged — the deadline share is what must fall.

---

## 7. What NOT to do

- **Raise the 30 minutes and stop there.** It moves the wall, touches the systemd unit
  (`Describe_Mismatch_OrNull` will say so), and the work will grow into it again.
- **Add a brake to sub-agents.** They are the largest bucket — 3,913 calls on the night against
  2,894 for implementers [measured] — and that argues for a return contract in the role prompts,
  not a third brake. One mis-set brake is the current problem; two would be worse. This retracts a
  proposal made in the independent review's §6 in that form.
- **Move the advisory threshold before §5.1.** Tuning a number whose delivery is broken changes
  nothing and hides the fault.
- **Write a second silence brake.** One exists (§4.0). A second implementation of the same rule is
  how a formatter ends up with two copies and one of them without the guard — the failure this
  repository already has a decision about.

---

## 8. What is NOT measured, explicitly

- **Why the advisory misses.** §3 lists candidates; none is verified. This is the first task, not a
  finding.
- **What the 10-minute silences are.** The build/test hypothesis in §4.3 is an estimate; the gaps
  were never attributed to a cause.
- **Whether killed turns lose work.** The closing turn succeeds 57 times out of 57 [measured], so a
  report is always produced. Whether that report carries what the killed turn had learned is
  unexamined, and it is the difference between a nuisance and a real loss.
- **The cost of the kills.** 57 turns at no less than the 253 k median context of a successful turn
  is about 14 M tokens over three days [estimate, and a floor — a killed turn ran far longer than
  the median], plus 57 closing turns. It was not measured directly because the killed turns'
  records carry no usage at all.
- **Whether the 17 supervisor silence-kills were legitimate.** 600 seconds of silence could be a
  hang or a long think; the activity profile of §2 was never run against them. This matters more
  than it looks: §4.0 proposes reusing that mechanism.
- **Nobody has reviewed this spec.** On this line of work, six consecutive changes were found
  defective by review after their author had read the diff and seen green. Including this author's.

---

## 9. Revision 3 — measured, then built (2026-09-11, solo of ai-orch-2)

**Copies read.** Live data on the VPS, read-only, metadata only (journal, `print-session.json`,
transcript structure, `/tmp/aiorch-soft-boundary` counters). Code: `stage/28-the-print-brake-watches-life`,
forked from `ours/integration` at `9394971`.

### 9.1 §5.1 — the advisory is delivered; it does not stop the kills
- **58 deadline kills** since 09-09 [measured]. **29 came before the hook was deployed at all**: the
  service ran unrestarted from 09-09 20:08Z to 09-10 09:43Z and the hook was merged 09-10 09:39Z
  [measured]. "25 of 57" was a measurement window, not a delivery fault.
- With the hook installed, **26 of 29** killed turns received it [measured]. The 3 misses were each
  inside one long call (a sub-agent 17 min, a Bash 29 min, a Skill 5 min): a PreToolUse advisory needs
  a next call to ride on.
- It lands a median **7.1 min** into the turn and the turn then runs a median **22.9 min** more to the
  kill [measured]. Delivery is not the problem; the turns keep working.
- Two defects found on the way, parked: the hook counts **0 of 2,574** sub-agent calls, contrary to
  its header [measured]; and a `resume = transcript` print turn (solo, communicator) loses every
  skill-frontmatter hook after turn 1, Stop hooks included [measured on the solo's own session].

### 9.2 §5.2 — the 17 supervisor silence-kills were all false, from a bug already fixed
- **17 of 17** fired about 5 s after a new prompt, on an idle supervisor, with the "silence" equal to
  the time since the previous turn [measured]; fixed by `b5f7607` (the clock starts at the prompt) and
  **none since** the 09-09 13:10Z restart [measured]. Neither brake had ever caught a real hang.

### 9.3 What §4.0 missed
The print runner read the child's output only at exit, and the stream brake counts bytes only — a
running build or a parent waiting on a sub-agent writes none. So the stream brake could not be
"given" to members as it was: liveness had to come from more than bytes.

### 9.4 Built on stage/28 — the owner's choices of 16:19 and 16:31: "implement the best practice"
| piece | what it does |
|---|---|
| silence brake (members) | an implementer/reviewer print turn is killed only after `printRunner.memberSilenceMinutes` (15) with no output, no write to its transcript or a sub-agent's, and no process running below it; "cannot tell" counts as alive |
| member ceiling | `printRunner.memberTurnTimeoutMinutes` (120) while the brake is on; everyone else keeps 30; shutdown, host and the systemd unit (`TimeoutStopSec=7800`) are sized for the longest |
| loop detection | OpenHands' stuck-detector rules on the turn's own transcript: same call + same result ×4, same call failing ×3, A/B alternating ×3 |
| progress note | members append one line per verified step to `<orch>/<member>/progress.md`; the next pack hands it back |
| closing turn | every brake kill gets the deadline kill's closing turn, and the records say which brake fired |

**Not measured yet:** how long the cut turns actually needed; whether the loop thresholds fit our
turns; the Windows process-tree reader (no Windows machine). The §6 "done when" error-rate target can
only be read after a deploy.

