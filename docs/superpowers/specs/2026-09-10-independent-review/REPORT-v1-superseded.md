# Independent review of the token work of 2026-09-08 → 09-10

Written 2026-09-10 by a session that was NOT the one that did the work, after reading the code and
re-measuring production before reading the predecessor's conclusions. It judges that line of work; it
does not continue it.

Companion brief under review:
`git show origin/stage/11-the-proof-in-production:docs/superpowers/specs/2026-09-10-brief-for-a-fresh-agent.md`

---

## 0. How to read this, and how to attack it

Same tagging convention as the brief under review:

- **[MEASURED]** — I ran a command and read its output. The command is given. Re-run it.
- **[VERIFIED-SOURCE]** — I personally read the cited `file:line` in the branch source. Not a
  sub-agent's report; where a sub-agent found it first, I re-read it myself before writing it here.
- **[INFERRED]** — my reasoning over measurements. This is the tag to attack.
- **[UNVERIFIED]** — stated so you do not mistake it for evidence.

**Which copy I read:** the **branch source** at `/Users/nvene/Visual Studio/AIOrchestrator`, branch
`ours/integration`, and the **production data** on the VPS (`orch@159.195.254.120`), read-only,
metadata only. I did **not** read the build output, the installed kit, or the running binary
(decisions 17/18/23) — see §7.

**Reproduction.** Every VPS measurement was produced by piping a script over stdin, leaving nothing
on the box:

```
ssh orch@159.195.254.120 'python3 -' < scripts/<name>.py
```

The four scripts are in `scripts/` beside this file: `hourly.py` (day/hour totals + model mix),
`cost.py` (USD by day and role), `step.py` (arbitrary windows), `step2.py` (implementer context per
call, by hour). They are read-only, stdlib-only, and read `usage`/`timestamp`/`model`/ids only —
never message text.

**Dedupe.** All four dedupe on `(sessionId, requestId)` with `message.id` as the fallback, globally
rather than per file.

---

## 1. Verdict in one paragraph

**The design is sound and one of the five shipped changes carries the whole result. The headline
number does not establish it, and the brief credits the wrong deploy.** The state pack cut an
implementer's cost per model call by **69 %** at constant model and constant role — and the proof is
not a day total but the *shape*: context per call used to grow monotonically inside a work session
and is now flat. The other four changes show **no measurable effect on cost**. Meanwhile the spend
moved to sub-agents, which on 2026-09-10 are the single largest bucket and are governed by nothing.

---

## 2. The re-measurement

### 2.1 The headline reproduces arithmetically

[MEASURED — `scripts/hourly.py`]

| | brief §5 | this reading |
|---|---|---|
| tokens 2026-09-08 | 773.1 M | **777.6 M** |
| tokens 2026-09-10 | 597.0 M | **595.9 M** |
| model calls 09-08 | 3,406 | 3,739 |
| model calls 09-10 | 4,606 | 4,619 |

Within 0.6 % on tokens. The call counts differ by ~330 on 09-08, which I did not chase; the most
likely cause is the brief's narrower session scope (its scripts read one project folder,
`gate_delivery.py:3`, while these read all of `~/.claude/projects`). [INFERRED]

### 2.2 …and it is not a usable number. Three reasons.

**(a) 2026-09-10 was not a whole day.** [MEASURED — `hourly.py`] The last hour bucket with data is
`14Z` (62 calls, clearly truncated); the VPS clock at measurement time read `Thu Sep 10 03:46:01 PM
UTC 2026`. The brief's row is labelled "Tokens, whole day". It is a **partial day compared against a
full one**, and it keeps accruing after the measurement was taken. A measurement that expires is not
a baseline.

**(b) The unit overstates cost by roughly 7×.** [MEASURED] Cache reads are **97.6 %** of the 09-08
sum and **97.3 %** of the 09-10 sum. Fresh input tokens are ~0.01 M per day — essentially nil.

[VERIFIED-SOURCE] `tools/token-gate/gate_delivery.py:11` and `:30`, and `gate_turns.py:15`, all sum
`input + cache_read + cache_creation + output` at equal weight. Anthropic prices a cache read at
**0.1×** base input and a 5-minute cache write at **1.25×**
(https://platform.claude.com/docs/en/build-with-claude/prompt-caching). So the reported figure is
dominated by the cheapest token class in the system.

Priced properly [MEASURED — `scripts/cost.py`, at Opus 5 $5/$25, Sonnet 5 $2/$10, Haiku 4.5 $1/$5
per MTok]:

| day | model calls | USD |
|---|---|---|
| 2026-09-07 | 5,934 | 547.08 |
| 2026-09-08 | 3,739 | **512.56** |
| 2026-09-09 | 9,585 | 788.97 |
| 2026-09-10 (to 14Z) | 4,619 | **410.96** |

**(c) The baseline is the worst of the five days.** [MEASURED — `scripts/step.py`] Cost per model
call, all roles: 09-07 **$0.0922**, 09-08 **$0.1371**, 09-09 **$0.0823**, 09-10 **$0.0890**. On
matched 00–14Z windows, 09-07 is **$0.0743/call** and 09-10 is **$0.0867/call** — the "after" day is
*more* expensive per call than that "before" day. Substituting 09-07 for 09-08 as the baseline moves
the answer by an order of magnitude, which is the definition of a non-robust result. (That
particular pair also carries a model confound — see §3, F4.)

### 2.3 The measurement that does hold up

Do not compare days. Compare **cost per model call, within one role, at constant model**. On both
sides of the cut the implementers run Opus 5, so the model cancels.

[MEASURED — `scripts/step.py`, windows B/C/D/E]

| window | implementer calls | USD | **USD/call** | **ctx tokens/call** |
|---|---|---|---|---|
| 09-08, all day | 900 | 226.69 | **0.2519** | 391 k |
| 09-09, 00–17Z | 1,517 | 117.94 | **0.0777** | 98 k |
| 09-09, 17–24Z | 1,803 | 140.96 | **0.0782** | 105 k |
| 09-10, 00–14Z | 1,613 | 115.25 | **0.0715** | 95 k |

**−69 % per call, at the same model, in the same role.**

### 2.4 The shape, which is the real proof

[MEASURED — `scripts/step2.py`] Mean context tokens per model call, implementer members only, by UTC
hour (hours with ≥10 calls):

```
2026-09-07  10Z 142k → 11Z 285k → 12Z 467k → 13Z 564k → 14Z 587k     GROWS
2026-09-08  00Z 239k → 01Z 299k → 02Z 487k → 03Z 628k                GROWS
            ── 21 hours with no implementer work ──
2026-09-09  04Z  78k   05Z 107k   06Z 110k   11Z  72k   12Z  89k     FLAT
            13Z  84k   14Z  77k   15Z 102k   16Z 126k   18Z 114k     FLAT
            19Z 103k   20Z  98k   21Z 100k   23Z  99k                FLAT
2026-09-10  00Z 111k   01Z  81k   02Z  89k   03Z  65k   10Z  88k     FLAT
            11Z 132k   12Z  78k   13Z  78k   14Z  86k                FLAT
```

Before, context per call climbs monotonically inside each work session — the signature of resuming a
growing transcript. After, it is flat and never trends. **[INFERRED, high confidence] No difference
in task mix produces that change of shape.** This is a far stronger causal fingerprint than any day
total, and it is the evidence the brief should have led with.

### 2.5 What I did about the task-mix confound

The brief names it and could not remove it. I did four things, none of which removes it, and I do
not claim it removed:

1. **Held the role constant** — implementers only, so a day with more reviewer work cannot move the
   number.
2. **Held the model constant** — Opus 5 on both sides, verified per hour in `step2.py`'s model
   column, so the sonnet→opus switch of 09-07 cannot leak in.
3. **Normalised by model call** rather than by day, removing volume.
4. **Used the shape, not the level.** Monotonic-growth → flat is a structural property. Task mix can
   move the level of a curve; it does not convert a rising curve into a flat one.

[INFERRED] Residual confound remains on the *level* (95 k vs 391 k could in principle owe some part
to smaller tasks) but not on the *trend*. The honest statement is: the direction and the mechanism
are established; the exact magnitude is not.

**The only design that removes the confound by construction is a frozen task suite run under both
configurations.** Statistical correction (CUPED-style covariate adjustment on a per-task cost proxy)
is the fallback when that is impractical. A plain before/after over two live production periods is
not a valid design and cannot be made one by measuring it more carefully. See §5.

---

## 3. Findings

### F1 — The brief credits the wrong deploy. [MEASURED + VERIFIED-SOURCE]

The brief §3 states the five changes "went live on the evening of 2026-09-09". The predecessor's own
instrument disagrees with his prose:

[VERIFIED-SOURCE] `tools/token-gate/gate_delivery.py:2`
```python
CUT="2026-09-08T20:34:00"  # round-1 deploy (implementer + reviewer fresh)
```

"implementer + reviewer fresh" **is** the state pack — it is the config flag that stops resuming the
transcript ([VERIFIED-SOURCE] `PrintTurnDispatcherModel.cs:1212` `var fresh = roleConfig.Resume ==
ResumeModes.Fresh;` and `:1231` `var resumeTranscript = !fresh && sessionUsed;`, feeding
`PrintTurnCommand_Builder.cs:49` `arguments.Add(resumeTranscript ? "--resume" : "--session-id");`).

[MEASURED] The VPS config backups corroborate: `~/.claude/supervision/config.json.pre-round1-20260908-223404`
has mtime `2026-09-08T20:34:04Z` and the next snapshot forward carries
`runners/implementer/resume: transcript → fresh` and the same for `reviewer`.

[MEASURED] Implementer work stopped on 09-08 at `07Z` and resumed on 09-09 at `04Z`. A flag flipped
at 09-08 20:34Z therefore first takes effect on 09-09 04:00Z — **exactly where §2.4 shows the step.**

**Consequence:** the state pack went live on the evening of **09-08**, one day earlier than the brief
says. The five changes of the 09-09 evening deploy are the *other four*. Everything the brief
attributes to that deploy has to be re-attributed.

### F2 — The other four changes show no measurable cost effect. [MEASURED]

Across the 09-09 evening deploy ([MEASURED] `~/aiorch-updates/deploy-20260909-3.log` at 21:36 CEST
and `-4.log` at 22:08 CEST), the implementer numbers are:

- before: **$0.0777**/call, **98 k** ctx/call
- after: **$0.0782**/call, **105 k** ctx/call

That is zero, within noise. The member digest, the soft-boundary advisory, the closing-spoke
reporter and the per-role model split did not move cost. This does not make them wrong — the digest
and the closing-spoke fix were never cost changes — but it retires the claim that the −23 % belongs
to them.

### F3 — The spend moved to sub-agents, and nothing watches there. [MEASURED — `scripts/cost.py`]

| | 09-08 | 09-10 (to 14Z) |
|---|---|---|
| sub-agent calls | 1,720 | 2,197 |
| sub-agent USD | 86.95 | **157.56** |
| sub-agent USD/call | 0.0505 | **0.0717** (+42 %) |
| implementer USD | 226.69 | **115.25** |

On 09-10 **sub-agents cost more than the implementers that spawn them**. The brief flags the token
doubling (§4.4) and is right; what it does not say is that this bucket has become the largest single
line of spend in the system. [VERIFIED-SOURCE] The soft-boundary hook, the only brake in the design,
explicitly excludes sub-agents from receiving the advisory (`kit/hooks/soft-boundary-check.sh`, the
`agent_id`-present skip), though their calls still count toward the parent's total.

### F4 — Model routing, not process, explains the 09-07 → 09-08 rise. [MEASURED]

`scripts/step2.py`'s model column shows implementers on `sonnet-5` through 09-07 12Z, mixed at 13Z,
and `opus-5` from 09-07 17Z onward. Opus is 2.5× Sonnet on input. This is why 09-08 is the most
expensive day per call and why choosing it as the baseline flatters the result. Any future
comparison must hold the model constant or state that it does not.

### F5 — `tools/token-gate/` cannot produce a trustworthy number even after parameterisation.
[VERIFIED-SOURCE]

The brief's §4.5 asks only for a window parameter. Four further defects, each of which changes the
answer:

1. **Cache reads at full weight** — `gate_delivery.py:11,30`, `gate_turns.py:15`. §2.2(b).
2. **Role attribution is a string prefix with no third bucket** — `gate_turns.py:18`
   `def role(m): return "imp" if m.startswith("imp") else "rev"`. Every member that is not an
   implementer is silently declared a reviewer. Supervisors are handled separately by a hardcoded
   allowlist of two session ids (`gate_delivery.py:34`); a third supervisor session is invisible.
3. **Hand-eyeballed divisors** — `gate_delivery.py:70` `line(c0,k0,"BASELINE (before cut)",23)` with
   the code's own comment `# fincanva-1..4 lines closed before: 5+22+1+0=28? use fincanva-2+3 = 23`,
   and `:71` `line(c1,k1,"ROUND1 (since cut)",7)`. Every per-item figure the tool prints rests on a
   number its author was unsure of, in writing.
4. **The cut constant is written in two incompatible formats** — `gate_delivery.py:2`
   `"2026-09-08T20:34:00"` (no `Z`) vs `gate_turns.py:2` `"2026-09-08T20:34:00Z"`. Both are compared
   as strings against ISO transcript timestamps.

### F6 — No prompt-cache-aware logic exists anywhere in the codebase. [VERIFIED-SOURCE]

```
grep -rn "cache_control\|CacheControl\|promptCach\|prompt_cach" AIOrchestratorCoreLib/   →  0 lines
```

Given that ~97 % of every token this system spends is a cache read (§2.2), this is the largest
untouched lever in the design. See §5 for what the measured literature says it is worth.

### F7 — The soft-boundary advisory is an unaudited cache risk. [VERIFIED-SOURCE + UNVERIFIED]

[VERIFIED-SOURCE] The hook injects mid-turn via `hookSpecificOutput.additionalContext`
(`kit/hooks/soft-boundary-check.sh:21`, threshold `SOFT_BOUNDARY_CALLS` default **35** at `:140`,
overridable by `AIORCH_SOFT_BOUNDARY_CALLS`).

[UNVERIFIED] Where that injected text lands in the rendered prompt. Prompt caching is prefix-match:
any byte change ahead of a breakpoint invalidates everything after it. If the advisory lands ahead of
otherwise-stable content, it silently taxes every subsequent cache read for the rest of the session —
i.e. a feature shipped to reduce cost could be raising it. **This is cheap to settle and nobody has.**

### F8 — The pack has no global ceiling. [VERIFIED-SOURCE]

`StatePack_Builder.cs:26-31` caps six sections (8 000 / 6 000 / 6 000 / 24 000 / 3 000 / 8 000
characters). The pending-entries block is deliberately uncapped, stated at `:16-22`:

> "EXCEPT the pending entries, which are the reason the turn exists and are never cut."

The reasoning is sound — those entries are why the turn exists — but the consequence is that a burst
of member traffic produces an unbounded pack, which is the failure mode the pack was built to end.
No measured incident; a structural hole. [INFERRED]

---

## 4. Verdict per shipped change

| Change | Verdict | Confidence | Evidence |
|---|---|---|---|
| **State pack** (§3.1 of the brief) | **KEEP** | High | §2.3 −69 %/call at constant model; §2.4 growth→flat shape. I agree with the predecessor, on different and stronger evidence than his. |
| **Mid-turn advisory** (§3.2) | **AUDIT before keeping** | Low | Arrival was proven, effect never was — I confirm neither. F7 raises a mechanism by which it could cost money. |
| **Member digest** (§3.3) | **KEEP, unproven** | Low | F2: no cost effect. Its value was always latency-shaped, not cost-shaped; the brief's own three data points do not support the density story it tells. |
| **Closing-spoke report** (§3.4) | **KEEP** | Not assessed | Correctness fix, not a cost change. I did not review the fix; the brief itself says to check its adversarial review first. |
| **Per-role model split** (§3.5) | **KEEP** | Medium | F4: model choice demonstrably dominates per-call cost, so routing it per role is the right control surface. No before/after here isolates it. |
| **The −23 % headline** | **DISCARD as stated** | High | §2.2 (a) partial day, (b) wrong unit, (c) baseline-dependent. The underlying win is real and larger; the number as published does not establish it. |

---

## 5. What the literature changes about the plan

Sources fetched 2026-09-10. Marked measured vs asserted.

**Prompt-cache-aware ordering is the best-evidenced lever in this whole space, and F6 says we do
none of it.**
- ProjectDiscovery moved dynamic fields out of the early prompt: cache hit rate **7 % → 84 %**,
  **−59 % LLM cost**. Measured, single production system.
  https://projectdiscovery.io/blog/how-we-cut-llm-cost-with-prompt-caching
- claude-code issue **#74318** — 95 sessions, ~1,800 sub-agents, 6.8 B input tokens over two weeks.
  Finds sub-agents run on the 5-minute cache TTL while the main loop gets 1 hour, across a median
  9-minute parent-blocked gap; ~97 % of sub-agent cold-start tokens are static yet only ~16 % are
  served from cache. Three fixes measured at **−13.6 % sub-agent prompt cost, −7.8 % overall**.
  Measured, reproducible script published. https://github.com/anthropics/claude-code/issues/74318
  — **this is our F3 bucket, described by someone else, with a fix already quantified.**

**Levers Anthropic prescribes that this design does not have.** Tool-result *clearing* (not
summarising) is called "the safest, lightest-touch form of compaction"; Claude Code itself caps tool
responses at **25,000 tokens**; concise structured tool output measured at ~3× token reduction.
https://www.anthropic.com/engineering/effective-context-engineering-for-ai-agents ·
https://www.anthropic.com/engineering/writing-tools-for-agents

**The brief's biggest open question (§4.1, "does the pack contain what a turn needs?") cannot be
answered the way it proposes.** It proposes looking for a turn that failed for lack of context. The
MAST taxonomy (1,600+ annotated multi-agent traces, human-validated κ=0.88, arXiv:2503.13657) puts
"loss of conversation history" at **2.80 %** of failures and "information withholding" at **0.85 %** —
missing context is rarely a *visible* error. The characteristic manifestation is a confident,
coherent, wrong answer (arXiv:2606.14589). **Detection needs an abstention/precision metric, not an
inspection of outcomes.** LongMemEval (arXiv:2410.10813) is the published instrument that scores
abstention explicitly. [INFERRED] Chasing failed turns will find nothing and will be reported as
reassurance.

**Two findings that argue against parts of the design.**
- Curation buys efficiency, not accuracy, *while the transcript still fits the window*. Zep's audit
  of Mem0 found plain full-context (~73 %) matching or beating curated graph memory (~68 %) on
  LoCoMo. Contested benchmark; treat as a caution, not a result.
- Summarisation raises confidence while lowering accuracy, discarding rejected approaches and
  implicit constraints (ACON, arXiv:2510.00615). This is aimed squarely at the member digest.
- Counterweight in our favour: measured context degradation well inside the window across 18 models,
  with curated ~300-token prompts beating full ~113 K prompts on LongMemEval.
  https://www.trychroma.com/research/context-rot

**Methodology.** The only technique that removes a task-mix confound *by construction* is a frozen
task suite run under both configurations (as in arXiv:2509.23586). Otherwise: CUPED covariate
adjustment (Deng et al., ~50 % variance reduction at Bing) or a paired bootstrap over a shared task
set. And the denominator should be **cost per success**, not cost per token (Cost-of-Pass,
arXiv:2504.13359) — which is the brief's own §4.6 admitting the denominator does not exist.

---

## 6. Proposal

Ordered by (evidence strength × money at stake). Not the brief's §4.1–§4.4.

**P1 — Audit where the soft-boundary advisory lands in the prompt.** (F7)
Cheapest item here and it can invert the sign of a shipped feature. Done when: the injection point
is located relative to the cache breakpoint, with the measurement that shows it.

**P2 — Make sub-agents cache-correctly.** (F3, F6, issue #74318)
Largest bucket, best-measured external fix. Done when: sub-agent `cache_read_input_tokens` share is
measured before and after on the same frozen task set, and the change is stated in dollars.

**P3 — Rebuild `tools/token-gate/` rather than parameterise it.** (F5)
Window parameters plus: price-weighted totals, a real role map (from `turns.jsonl`, not a string
prefix), no magic divisors, one timestamp format. Done when: it reproduces §2.3 and §2.4 from a
command line with no constants edited.

**P4 — Emit a ledger-close event into `orchestrator.log.jsonl`.** (§5, Cost-of-Pass)
Without it, "cost per delivered item" stays uncomputable and the next headline will be another −23 %.

**P5 — Give the pack a global ceiling.** (F8)

**Rejected, and why:**
- *Re-running the brief's day-vs-day comparison with better scripts.* That design is invalid for a
  task-mix confound; better execution of an invalid design produces a more confident wrong number.
  §2.5.
- *Hunting for a turn that failed for lack of context* (brief §4.1). §5 — it will find nothing and
  the nothing will be read as proof.
- *Removing or re-tuning the digest on these numbers.* F2 says it did not move cost; that is not
  evidence it should change. Its case is about latency and supervisor turn count, and belongs to
  whoever owns that question.

---

## 7. What I did NOT verify. Explicitly.

- **The running binary.** I read the branch source and the VPS's deployed git checkout and config
  files. I did **not** check what process is actually executing (decision 23, the fourth copy). Every
  code claim here is about the branch source and is labelled as such. **A reviewer should assume the
  VPS may be running something else.**
- **The test suite.** Not run. No claim in this document depends on it.
- **Message text.** Never read, on the VPS or anywhere. No `~/.claude/projects` on the local Mac was
  read at all.
- **The supervisor bucket.** My role map is built from `session_id` in
  `~/.claude/supervision/*/*/turns.jsonl`, which covers members only. Supervisors fall into
  `unmapped`, which is **$146.89 on 09-08 (29 %)** and **$97.38 on 09-10 (24 %)**. This is the same
  hole the brief admits (its 16 %), differently shaped. **No statement here about supervisor cost.**
- **The closing-spoke fix** (`41531e0`) — not reviewed. Its adversarial review outcome is still the
  thing to check.
- **Whether the pack contains the right things.** Untouched. §5 explains why the proposed method
  would not have answered it either.
- **The 330-call discrepancy** in §2.1 — hypothesised, not chased.
- **Config-backup mtimes** are a weak instrument; I used them only to corroborate F1, whose primary
  evidence is the transcript data and the source comment.
- **No adversarial review of this document.** Nobody certifies their own work. This is a claim set,
  not a clearance.

---

## 8. Where I am most likely wrong — attack these first

1. **F1 is the load-bearing claim** and it rests on a code comment plus a 21-hour work gap. If the
   fresh-mode flag was in fact flipped at some other moment, the attribution changes. Test it against
   the app's own log rather than config mtimes.
2. **The shape argument (§2.4) is mine and it is an inference.** I claim no task mix converts a
   rising curve into a flat one. Find a counter-example: a pre-cut work session that stayed flat, or
   a post-cut one that grew.
3. **I never established the magnitude, only the direction and mechanism.** The −69 % is per-call
   within one role and still carries a level-confound. Do not quote it as "the state pack saves 69 %"
   without the qualifier.
4. **F3's "+42 % per sub-agent call" spans the same two confounded days** as everything else here.
   It is the finding of mine I trust least on magnitude, while trusting its direction.
5. **I read no message text by mandate**, which means I cannot distinguish "cheaper because the pack
   works" from "cheaper because the work was smaller". §2.5 is my defence and it is partial.
6. **On 09-10 the `unmapped` bucket costs $0.3279/call — 4.6× the implementer rate.** I did not
   explain it. It may be the supervisors, or it may be something that undoes part of this analysis.

