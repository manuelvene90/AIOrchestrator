# Independent review of the token work of 2026-09-08 → 09-10 — v2

Written 2026-09-10 by a session that was NOT the one that did the work. It judges that line of work;
it does not continue it.

**v2 supersedes v1** (kept as `REPORT-v1-superseded.md`) after a third session's review, filed as
`REPORT-corrections.md`. What that review changed is listed in §9; **it was right on every point it
raised, including three literature citations I had wrong.** v2 also adds one finding neither the
brief, v1, nor the corrections had: **F9 — the fix was never switched on for the most expensive role
left.**

Brief under review:
`git show origin/stage/11-the-proof-in-production:docs/superpowers/specs/2026-09-10-brief-for-a-fresh-agent.md`

---

## 0. How to read this

- **[MEASURED]** — I ran a command and read its output. The command is given. Re-run it.
- **[VERIFIED-SOURCE]** — I personally read the cited `file:line` or fetched the cited primary
  source. Where a sub-agent found it first, I re-read it myself before writing it here. In v1 I
  failed to do this for three literature citations; see §7.
- **[DOCUMENTED]** — Anthropic documentation, quoted.
- **[INFERRED]** — my reasoning over measurements. The tag to attack.
- **[UNVERIFIED]** — stated so it is not mistaken for evidence.

**Which copy I read:** the **branch source** at `/Users/nvene/Visual Studio/AIOrchestrator`, branch
`ours/integration`; the **installed CLI** (`claude --version` → **2.1.267**, the VPS version); and the
**production data** on the VPS, read-only, metadata only. I did **not** read the build output, the
installed kit, or the running binary (decisions 17/18/23) — §7.

**The frame is tokens, not dollars.** [Owner decision, relayed via `REPORT-corrections.md`.] It is
corroborated by the system itself: the app already tracks and alerts on **usage-limit** percentages
(`CLAUDE.md` decision 10, "usage-limit alerts at 90/95/97/98/99/100 %"), so the binding constraint is
the subscription limit, not a bill. Dollar figures are kept as a secondary column because they are
the owner's own lens, and nothing is ranked by them. **One challenge to that frame is open — §6, Q1.**

**Reproduction.** Every VPS measurement was produced by piping a script over stdin, leaving nothing
on the box:

```
ssh orch@159.195.254.120 'python3 -' < scripts/<name>.py
```

Five scripts in `scripts/`: `hourly.py` (day/hour totals + model mix), `cost.py` (by day and role),
`step.py` (arbitrary windows), `step2.py` (implementer context per call, by hour), `unmapped.py`
(the unattributed bucket). Read-only, stdlib-only, reading `usage` / `timestamp` / `model` / ids
only — never message text. All dedupe on `(sessionId, requestId)` globally, `message.id` as fallback.

---

## 1. Verdict in one paragraph

**The design is sound, one of the five shipped changes carries the whole result, the headline number
does not establish it, the brief credits the wrong deploy — and the proven fix was never applied to
the role that now costs the most per call.** The state pack cut an implementer's **context per model
call by 76 %**, and the proof is not a day total but the *shape*: context per call used to grow
monotonically inside a work session and is now flat. The other four changes show no measurable
effect. The spend moved to sub-agents. And supervisors still resume the transcript at **546 k
context per call**, 5.8× an implementer, with the pack code for them already written and switched off.

---

## 2. The re-measurement

### 2.1 The headline reproduces arithmetically, then stops being useful

[MEASURED — `scripts/hourly.py`]

| | brief §5 | this reading |
|---|---|---|
| tokens 2026-09-08 | 773.1 M | **777.6 M** |
| tokens 2026-09-10 | 597.0 M | **595.9 M** |
| model calls 09-08 | 3,406 | 3,739 |
| model calls 09-10 | 4,606 | 4,619 |

Within 0.6 % on tokens. The ~330-call gap on 09-08 I did not chase; most likely the brief's narrower
session scope (`gate_delivery.py:3` reads one project folder; these read all of `~/.claude/projects`).
[INFERRED]

**The day totals are retired from here on.** Three reasons:

**(a) 2026-09-10 was not a whole day.** [MEASURED] Last hour bucket with data is `14Z` (62 calls,
truncated); the VPS clock read `Thu Sep 10 03:46:01 PM UTC 2026`. The brief labels the row "Tokens,
whole day". A partial day compared against a full one, still accruing after the reading was taken.

**(b) The unit lumps three different things together.** [MEASURED] Cache reads are **97.6 %** of the
09-08 sum and **97.3 %** of the 09-10 sum; fresh input is ~0.01 M/day.
[VERIFIED-SOURCE] `gate_delivery.py:11,30` and `gate_turns.py:15` sum
`input + cache_read + cache_creation + output` at equal weight.

In a token frame the fix is not to price-weight them but to **split the classes**:

| Class | Fields | What it is |
|---|---|---|
| **new** | `input_tokens` + `cache_creation_input_tokens` | text processed from scratch this call |
| **re-read** | `cache_read_input_tokens` | context identical to the previous request, passed again |
| **output** | `output_tokens` | answer + thinking |

A cache miss does not change context per call; it moves tokens from *re-read* to *new*. So the three
metrics that matter are **context per model call** (new + re-read), **output per turn**, and **new
tokens on the first call of a Fresh turn** (the cold start the design pays once per turn).

**(c) The baseline is the worst of the five days.** [MEASURED — `scripts/step.py`] Cost per call, all
roles: 09-07 $0.0922, 09-08 **$0.1371**, 09-09 $0.0823, 09-10 $0.0890. Substituting 09-07 for 09-08
moves the answer by an order of magnitude. (That pair also carries a model confound — F4.)

### 2.2 The measurement that holds up

Compare **context per model call, within one role, at constant model**. Implementers run Opus 5 on
both sides, so the model cancels. [MEASURED — `scripts/step.py`, windows B/C/D/E]

| window | implementer calls | **ctx tokens/call** | USD/call *(secondary)* |
|---|---|---|---|
| 09-08, all day | 900 | **391 k** | 0.2519 |
| 09-09, 00–17Z | 1,517 | **98 k** | 0.0777 |
| 09-09, 17–24Z | 1,803 | **105 k** | 0.0782 |
| 09-10, 00–14Z | 1,613 | **95 k** | 0.0715 |

**−76 % context per call, same model, same role.**

### 2.3 The shape, which is the real proof

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

Before, context climbs monotonically inside each work session — the signature of resuming a growing
transcript. After, flat, never trending. **[INFERRED, high confidence] No difference in task mix
converts a rising curve into a flat one.** This is the evidence the brief should have led with.

### 2.4 What I did about the task-mix confound

The brief names it and could not remove it. I did four things, none of which removes it:

1. **Held the role constant** — implementers only.
2. **Held the model constant** — Opus 5 both sides, verified per hour in `step2.py`'s model column.
3. **Normalised by model call**, removing volume.
4. **Used the shape, not the level.** Task mix moves the level of a curve; it does not flatten a
   rising one.

[INFERRED] Residual confound remains on the **level**, not the **trend**. Honest statement: direction
and mechanism are established; exact magnitude is not.

**The only design that removes the confound by construction is a frozen task suite run under both
configurations** — the design used by AgentDiet (arXiv:2509.23586), which excluded its 100
hyperparameter instances and ran "200 instances from the remaining 400" under both arms
[VERIFIED-SOURCE]. Fallbacks: CUPED covariate adjustment, or a paired bootstrap over a shared set. A
plain before/after over two live production periods is not a valid design and better execution does
not make it one.

---

## 3. Findings

### F1 — The brief credits the wrong deploy. [MEASURED + VERIFIED-SOURCE]

Brief §3 says the five changes "went live on the evening of 2026-09-09". The predecessor's own
instrument disagrees with his prose:

[VERIFIED-SOURCE] `tools/token-gate/gate_delivery.py:2`
```python
CUT="2026-09-08T20:34:00"  # round-1 deploy (implementer + reviewer fresh)
```

"implementer + reviewer fresh" **is** the state pack — the config flag that stops resuming
([VERIFIED-SOURCE] `PrintTurnDispatcherModel.cs:1212` `var fresh = roleConfig.Resume ==
ResumeModes.Fresh;`, `:1231` `var resumeTranscript = !fresh && sessionUsed;`, feeding
`PrintTurnCommand_Builder.cs:49` `arguments.Add(resumeTranscript ? "--resume" : "--session-id");`).

[MEASURED] `~/.claude/supervision/config.json.pre-round1-20260908-223404` has mtime
`2026-09-08T20:34:04Z`; the next snapshot forward carries `runners/implementer/resume: transcript →
fresh` and the same for `reviewer`. Implementer work stopped 09-08 `07Z` and resumed 09-09 `04Z`, so
a flag flipped at 09-08 20:34Z first takes effect at 09-09 04:00Z — **exactly where §2.3 shows the
step.**

**Consequence:** the state pack went live on the evening of **09-08**. The 09-09 evening deploy
shipped the *other four* changes. Every attribution to that deploy has to be revised.

### F2 — The other four changes show no measurable effect. [MEASURED]

Across the 09-09 evening deploy ([MEASURED] `~/aiorch-updates/deploy-20260909-3.log` 21:36 CEST,
`-4.log` 22:08 CEST): before **98 k** ctx/call, after **105 k** ctx/call. Zero, within noise. The
digest, the advisory, the closing-spoke reporter and the model split did not move context. This does
not make them wrong — the digest and the closing-spoke fix were never context changes — but it
retires the claim that the headline belongs to them.

### F3 — The spend moved to sub-agents, and the brake that exists is not passed.
[MEASURED + VERIFIED-SOURCE + DOCUMENTED]

| | 09-08 | 09-10 (to 14Z) |
|---|---|---|
| sub-agent calls | 1,720 | 2,197 |
| sub-agent ctx/call | 71 k | **107 k** (+51 %) |
| sub-agent USD *(secondary)* | 86.95 | 157.56 |

The brief flags the doubling (§4.4) and is right. Three token questions it does not ask: **spawns per
turn**, **context loaded cold per spawn**, **tokens returned to the parent**.

- [DOCUMENTED] A non-fork sub-agent starts with a fresh isolated context window — its own system
  prompt, `CLAUDE.md`, git snapshot and task (code.claude.com/docs/en/sub-agents). Issue #74318
  measures ~37 k tokens per spawn, ~97 % identical across same-type sub-agents.
- [DOCUMENTED] Anthropic's own pattern: sub-agents "explore extensively but return condensed
  summaries (typically 1,000–2,000 tokens)".
- [VERIFIED-SOURCE, this machine, CLI 2.1.267] **`--max-turns` exists and is accepted, though it is
  absent from `claude --help`.** Test: `claude -p --definitely-not-a-real-flag …` → `error: unknown
  option`; `claude -p --max-turns 1 …` → passes argument parsing and fails at the login check. It is
  a per-invocation brake the app can set — enforcement at the point of effect (decision 21) rather
  than a hook that advises.
- [VERIFIED-SOURCE] Insertion point is `PrintTurnCommand_Builder.cs:45-95`, and the file already
  records the constraint: any flag must go **before** the reviewer's variadic `--disallowedTools`
  and its `--` terminator.

v1 said sub-agents were "governed by nothing". That was wrong in a useful way: nothing governs them
*today*, but the mechanism to govern them ships in the CLI and the app never passes it.

### F4 — Model routing, not process, explains the 09-07 → 09-08 rise. [MEASURED]

`step2.py`'s model column: implementers on `sonnet-5` through 09-07 12Z, mixed at 13Z, `opus-5` from
17Z. This is why 09-08 is the most expensive baseline and why choosing it flatters the result. Any
future comparison must hold the model constant or say that it does not.

### F5 — `tools/token-gate/` should be rebuilt, not parameterised. [VERIFIED-SOURCE]

Brief §4.5 asks only for a window parameter. Six defects:

1. **The three classes are summed into one** — `gate_delivery.py:11,30`, `gate_turns.py:15`. §2.1(b).
2. **No first-call *new* count per Fresh turn** — the only place cache behaviour surfaces as tokens.
3. **Role attribution is a string prefix with no third bucket** — `gate_turns.py:18`
   `def role(m): return "imp" if m.startswith("imp") else "rev"`. Every non-implementer member is
   silently declared a reviewer. Supervisors come from a hardcoded allowlist of two session ids
   (`gate_delivery.py:34`); a third supervisor is invisible.
4. **Hand-eyeballed divisors** — `gate_delivery.py:70`
   `line(c0,k0,"BASELINE (before cut)",23)` with the code's own comment
   `# fincanva-1..4 lines closed before: 5+22+1+0=28? use fincanva-2+3 = 23`, and `:71` `…,7)`.
   Every per-item figure rests on a number its author was unsure of, in writing.
5. **The cut constant is written in two incompatible formats** — `gate_delivery.py:2`
   `"2026-09-08T20:34:00"` vs `gate_turns.py:2` `"…Z"`, both string-compared against ISO timestamps.
6. **No `cache_creation.{ephemeral_5m,ephemeral_1h}` split**, which the transcripts carry.

### F6 — "No cache-aware logic in the codebase" was true and beside the point. **I was wrong.**
[DOCUMENTED + VERIFIED-SOURCE]

v1 reported `grep -rn "cache_control\|CacheControl\|promptCach" AIOrchestratorCoreLib/ → 0 lines` and
called it the largest untouched lever. The grep result stands; the conclusion does not. **The
orchestrator never calls the API.** It drives the `claude` CLI as a subprocess, and Claude Code
places every `cache_control` marker and orders the request itself. A C# grep was always going to
return 0. Credit where it is due: [VERIFIED-SOURCE] `StatePack_Builder.cs:16-22` already states
`ORDER: stable material first, the trigger last` — that *is* the ProjectDiscovery change (dynamic
content moved to the tail), which measured a 7 % → 84 % cache-hit swing. **The pack already does the
thing v1 said was missing.**

What the orchestrator does control, in tokens: the order of what it injects (done); the **cold start
of a Fresh turn** — [DOCUMENTED] sequential sessions share a cached prefix only when the startup git
snapshot matches, so after every commit a fresh turn re-processes system prompt + `CLAUDE.md` from
scratch, which is measurable now as *new* tokens on a Fresh turn's first call; and the cache **TTL**
(`promptCacheTtl` / `subagentPromptCacheTtl`), which should be set from the measured turn-to-turn gap
— #74318 measures a blanket 1 h on sub-agents as **+8.6 % worse**, because the win there is
placement, not lifetime.

### F7 — The advisory is a check, not a risk. **Downgraded.** [DOCUMENTED + INFERRED]

v1 raised the soft-boundary advisory as a possible cache-invalidator. [DOCUMENTED] Claude Code
"appends system context mid-conversation … and marks that block for caching"; skills and plan mode
are cache-safe because they "append their instructions as conversation messages, so the cached prefix
stays intact". [VERIFIED-SOURCE] The hook's own header (`kit/hooks/soft-boundary-check.sh:21`)
records the advisory arriving as `hook_additional_context` rendered as a `<system-reminder>` after
the tool call — conversation layer, at the tail. [INFERRED, high confidence] It cannot sit ahead of
stable content. Token effect: the advisory's own tokens, once per turn. Threshold is
`SOFT_BOUNDARY_CALLS` default **35** at `:140`. **The check is one number on data already on disk:**
*new* tokens on the call after the advisory should not jump.

### F8 — The pack has no global ceiling. [VERIFIED-SOURCE]

`StatePack_Builder.cs:26-31` caps six sections (8 000 / 6 000 / 6 000 / 24 000 / 3 000 / 8 000
characters). The pending-entries block is deliberately uncapped, stated at `:16-22`:

> "EXCEPT the pending entries, which are the reason the turn exists and are never cut."

The reasoning is sound; the consequence is that a burst of member traffic produces an unbounded pack,
which is the failure mode the pack exists to end. No measured incident. [INFERRED]

### F9 — **NEW. The proven fix was never switched on for the most expensive role left.**
[MEASURED + VERIFIED-SOURCE]

v1 left a bucket unexplained: on 09-10 the unattributed spend ran at 4.6× the implementer rate. It is
the supervisors, and the reason is that they never got the state pack.

[MEASURED — `scripts/unmapped.py`] 2026-09-10 unattributed main-session calls:

```
calls   ctx/call(k)  sessions  project
  287       546.6         3    -home-orch-repos-Fincanva      ← the supervisors
   13        49.1         2    -home-orch--claude-supervision-general
    1        22.9         1    -
```

[MEASURED] The live `~/.claude/supervision/config.json` resume mode per role:

```
communicator    transcript
general         fresh
implementer     fresh
reviewer        fresh
solo            transcript
supervisor      transcript      ← never changed, in any snapshot
```

So: **546 k context per call, 5.8× an implementer's 95 k, on Opus, still resuming the transcript.**
287 calls — 6 % of the day's calls — carrying the largest per-call context in the system.

And the code for them is already written and switched off: [VERIFIED-SOURCE]
`StatePack_Locator.cs` routes `SessionRoles.Supervisor` to `<orch>/.supervisor.pack.md`, and
`StatePackInputs_Reader.cs:34,42,44` gives supervisors and solo the whole `PLAN.md` and the
owner-channel tail via `ownsTheEndeavour`. **The supervisor pack has a shape. Nothing writes it,
because the role is not in Fresh mode.**

[UNVERIFIED] Whether leaving supervisors on `transcript` was deliberate. It plausibly was — a
supervisor is the owner's phone line and losing conversational continuity there is a different risk
from losing it in a worker. **Find out before flipping it.** But it is now the largest single
remaining lever in the system, and it costs a config flag.

### F9-bis — **CORRECTION to F9: it was not an oversight. It is a scheduled step, and its gate has
not been shown to work.** [VERIFIED-SOURCE]

F9 asks whether leaving supervisors on `transcript` was deliberate. It was, it is recorded in three
places, and it is **the owner's own decision**:

- `docs/superpowers/specs/2026-09-08-token-efficiency-design.md:331` — risk register:
  *"Supervisor judgement thins without its transcript … supervisor goes fresh **last** (step 4),
  owner reads verdicts — medium — **the one risk to watch with eyes**."*
- `:129` — *"Scheduled BEFORE the supervisor goes fresh … halving the wake-ups is roughly a 2× on the
  most expensive role **without touching its memory** — the one lever here with no continuity risk.
  The supervisor's own transcript stays until the pack (C2) exists to replace it."*
- `:344` — *"**decided 2026-09-09: no.** No shortcuts; the supervisor takes the full road
  (C2 → C4 → fresh). The 48 % it costs meanwhile is accepted."*
- `docs/superpowers/specs/2026-09-09-token-work-handoff.md:47` — scheduled:
  **"11 Sep afternoon | Supervisor fresh with the pack (the pack for it is already built and inert)
  | VPS `config.json` + verification; no new code expected."**

[VERIFIED-SOURCE] The role protocol is ready too: `kit/skills/supervisor/SKILL.md` already carries
*"Fresh start? Look for your PACK first … the greeting in step 2 is skipped."*
[VERIFIED-SOURCE] And the code default was never the question:
`RoleRunnerConfig_Factory.cs:20` returns Fresh only for `SessionRoles.General`; every other role
defaults to Transcript by design, and implementer/reviewer run Fresh only because the VPS
`config.json` says so.

**So F9 is not a discovery.** What this review contributes is the number the plan never had:
**546 k context per call, 5.8× an implementer, 6 % of calls, on Opus** (§F9).

**But the sequencing gate has not been shown to hold.** The plan makes C4 — the member digest —
the prerequisite that must land *before* the supervisor goes fresh, on the stated grounds that it
would roughly halve supervisor wake-ups ("supervisor wake-ups per day −40 % `[estimate]`",
`2026-09-08-token-efficiency-design.md:184`). The digest shipped on the 09-09 evening deploy and
**F2 measures no effect on context per call.** Wake-up *count* is a different quantity from context
per call and I did **not** measure it — `orchestrator.log.jsonl` carries only `ts/orch/level/message`,
so counting supervisor turns from it is a free-text exercise I did not perform. [UNVERIFIED]

### F10 — **MEASURED: the digest's prerequisite delivered about a quarter of its estimate.**

[MEASURED — `scripts/supwakes.py`] Supervisor turn-starts parsed from `orchestrator.log.jsonl`'s own
line `"<stream|print> turn <orch>/sup/<n> started — attempt <a>, entries: <src> [i], …"`, first
attempt only (retries are not wake-ups), 652 of 700 starts:

| window | wakes | owner | member | entries | multi-entry | active h | wakes/active h | **entries/turn** |
|---|---|---|---|---|---|---|---|---|
| 2026-09-07 | 207 | 79 | 128 | 231 | 22 | 6 | 34.5 | 1.12 |
| 2026-09-08 | 165 | 52 | 113 | 173 | 8 | 8 | 20.6 | **1.05** |
| 09-09 pre-cut | 140 | 58 | 82 | 145 | 5 | 13 | 10.8 | **1.04** |
| 09-09 post-cut | 36 | 5 | 31 | 43 | 6 | 6 | 6.0 | **1.19** |
| 09-10 (to 14Z) | 100 | 34 | 66 | 110 | 7 | 11 | 9.1 | **1.10** |

Coalescence is the digest's mechanism and it **is** present: entries per turn moved from 1.04–1.05
before to 1.10–1.19 after. Wake-ups saved = 1 − turns/entries: **3–5 % before, 9–17 % after.**

The estimate in the plan was **−40 %** (`2026-09-08-token-efficiency-design.md:184`) on the argument
that halving wake-ups is "roughly a 2× on the most expensive role". Measured: **≈ 9 % on 09-10**,
roughly a quarter of that.

[MEASURED] And, as with context per call (F1), the large fall predates the digest: wakes per active
hour went 34.5 → 20.6 → 10.8 **before** the cut, and 6.0 → 9.1 after.

**Caveats, stated with the number:** "active hours" is my own crude denominator (distinct clock hours
containing at least one supervisor turn); the post-cut window opens at 17Z while the deploy landed
at 19:36Z, so it carries ~2.5 h of pre-deploy traffic; and 36 turns is a small sample whose 16.7 %
multi-entry rate should not be read as a rate. The 09-10 row (100 turns, 11 h) is the one to trust.

**What this means for the 11 Sep step. [INFERRED]** The gate was never "C4 must succeed or fresh is
unsafe" — the judgement risk of going fresh is independent of the digest. The gate was
*sequencing*: do the risk-free 2× first. **That 2× did not arrive.** So the supervisor is still at
546 k context per call, the free lever is spent, and the argument for delaying further is gone. The
continuity risk is unchanged and remains what the owner called "the one risk to watch with eyes" —
so the step should go ahead **with the before/after verdict reading already written into the plan**,
not on the strength of these numbers alone.

---

## 4. Verdict per shipped change

| Change | Verdict | Confidence | Evidence |
|---|---|---|---|
| **State pack** (brief §3.1) | **KEEP** | High | §2.2 −76 % ctx/call at constant model; §2.3 growth→flat. I agree with the predecessor, on different and stronger evidence than his. |
| **Mid-turn advisory** (§3.2) | **KEEP, verify once** | Low | F7: documented as tail-appended and cache-safe; its effect on behaviour remains unmeasured, as the brief admits. |
| **Member digest** (§3.3) | **KEEP, unproven** | Low | F2: no context effect. Its case is latency- and turn-count-shaped, not token-shaped. |
| **Closing-spoke report** (§3.4) | **KEEP** | Not assessed | Correctness fix. I did not review it; check its adversarial review first, as the brief says. |
| **Per-role model split** (§3.5) | **KEEP** | Medium | F4: model choice dominates per-call context cost, so routing it per role is the right control surface. Nothing here isolates it. |
| **The −23 % headline** | **DISCARD as stated** | High | §2.1 (a) partial day, (b) three classes summed as one, (c) baseline-dependent. The underlying win is real and larger. |

---

## 5. Literature — corrected

Fetched from primary sources 2026-09-10. **Three citations in v1 were wrong; all three came from a
research sub-agent's briefing that I forwarded without fetching the papers.** §7.

| v1 said | The source says | [VERIFIED-SOURCE] |
|---|---|---|
| "Summarisation raises confidence while lowering accuracy … (ACON, arXiv:2510.00615)" | ACON = *Optimizing Context Compression for Long-horizon LLM Agents*. It reports compression **preserving** accuracy: AppWorld gpt-4.1 56.0 % → **56.5 %** at −26 % peak tokens; 8-objective QA 0.366 → **0.373** EM at −54.5 %; OfficeBench 76.84 % → 74.74 % at ~−30 % (a small drop). The word *confidence* **does not appear in the paper**. | **ACON is evidence FOR careful compression, not against it.** The confidence claim is withdrawn entirely. ACON's descriptive list of what agent context carries — "causal relations …, evolving states …, preconditions …, task-relevant decision cues" — is a useful checklist for what a digest must keep. |
| "a frozen task suite (as in arXiv:2509.23586)" | 2509.23586 = **AgentDiet**, trajectory reduction removing useless/redundant/expired content *inside* a run: **−39.9 % to −59.7 % input tokens** with task success **−1.0 % to +2.0 %** (SWE-bench Verified, Multi-SWE-bench Flash, Claude 4 Sonnet / Gemini 2.5 Pro). It does use a fixed 200-instance set under both arms. | Both halves true, but v1 filed the **biggest published token lever in its own bibliography** as a methodology footnote. Promoted to a lever below. |
| "Zep's audit of Mem0 found full-context (~73 %) beating graph memory (~68 %)" | Those are **Mem0's own Table 2** (arXiv:2504.19413): full-context 72.90 %, Mem0ᵍ 68.44 %. Zep quotes them; Zep's own disputed figures are a different pair. | Attribution corrected. Keep "contested". |

**Levers, ranked by measured token effect:**

1. **Trajectory reduction inside a turn** (AgentDiet) — −39.9 % to −59.7 % input tokens, success
   flat. The largest published effect available, and conceptually what the pack does *between*
   turns applied *within* one.
2. **Placement of dynamic content** — ProjectDiscovery 7 % → 84 % cache hit rate. **Already done by
   the pack** (F6).
3. **Sub-agent prefix sharing** — issue #74318, 1,777 sub-agents, 6.8 B tokens: ~97 % of cold-start
   content static, only 16 % served from cache; a blanket 1 h TTL measured **+8.6 % worse**.
4. **Tool-output capping** — [DOCUMENTED] Claude Code's default cap on tool responses is 25,000
   tokens; the costs page's own example takes a test run "from tens of thousands of tokens to
   hundreds" via PreToolUse output filtering. The "~3×" figure is one example in
   *writing-tools-for-agents* (206 → 72 tokens), not a general result. **Tool-result clearing is
   already done by the runtime** (`/usage` counts "expected rebuild (compaction or tool-result
   clearing)") — the missing piece is a cap of the design's own, e.g. `bashOutputMaxChars`.
5. **Effort per role** — [DOCUMENTED] thinking is billed as output; default effort is `high` on every
   current model. `--effort` is confirmed present in `claude --help` on 2.1.267.

**On detecting whether the pack is sufficient (brief §4.1).** The brief proposes looking for a turn
that failed for lack of context. It will not find one. MAST (1,642 annotated multi-agent traces,
κ=0.88) puts "loss of conversation history" at **2.80 %** of failures and "information withholding"
at **0.85 %**; the characteristic manifestation is a confident, coherent, *wrong* answer
(arXiv:2606.14589). Detection needs an **abstention/precision metric** — LongMemEval
(arXiv:2410.10813) is the published instrument that scores abstention explicitly. Counterweight in
the design's favour: measured context degradation well inside the window across 18 models, with
curated ~300-token prompts beating full ~113 K prompts (trychroma.com/research/context-rot).

---

## 6. Proposal

Ranked by tokens at stake × evidence. Done-when uses the §2.1(b) metrics on a **frozen task suite**.

- **P0 — DONE (F10).** Supervisor wake-ups measured on both sides: the digest delivered ≈ 9 % of
  wake-ups saved against a −40 % estimate. The 11 Sep step is unblocked by this reading and its free
  alternative is spent. **Remaining P0 work:** measure supervisor context per call before and after
  the flip (expected 546 k → implementer-shaped), and read verdicts before/after as the plan
  requires.
- **P1 — Advisory check.** (F7) One number on data already on disk: *new* tokens on the call after
  an advisory should not jump. Ten minutes.
- **P2 — Sub-agent discipline.** (F3) (i) `--max-turns` per role in `PrintTurnCommand_Builder.cs`,
  before the `--` terminator; (ii) a return contract in the role prompts — one summary, ≤ 2 k tokens;
  (iii) measure spawns/turn and context/spawn. Done when both fall on the same suite.
- **P3 — Rebuild `tools/token-gate/`.** (F5) Three classes split, first-call *new* per Fresh turn,
  real role map, one timestamp format, no magic divisors. Done when it reproduces §2.2 and §2.3 with
  no constants edited.
- **P4 — Ledger-close event** in `orchestrator.log.jsonl`, so tokens-per-delivered-item becomes
  computable. Without it the next headline will be another −23 %.
- **P5 — Pack global ceiling.** (F8)
- **P6 — Effort per role at launch.** `--effort` is the only lever that moves output tokens, which
  nothing else here touches. Caveat: parallel members sharing a prefix in one directory should share
  a level. Done when output per turn falls with task success flat.
- **P7 — Tool-output cap.** `bashOutputMaxChars` + output-filtering PreToolUse hooks for the noisiest
  commands (tests, builds).
- **Later — trajectory reduction** (AgentDiet). Biggest published effect; needs its own design.

**Open question back to the owner and to the corrections:**

- **Q1 — Is model routing really "price only"?** `REPORT-corrections.md` removes sub-agent model
  routing from the analysis on the grounds that it changes price, not tokens. That holds for a raw
  token count. It does **not** obviously hold for the constraint that actually binds here: the app
  alerts on **usage-limit** percentage (`CLAUDE.md` decision 10), and a subscription limit is
  normally metered with a per-model weight — so an Opus token and a Sonnet token are not
  interchangeable units of the constrained resource. [UNVERIFIED] how this account's limit is
  metered. Settleable from data the app already collects (`statusline.ps1` → `.usage.json`): if the
  limit percentage advances faster per Opus token than per Sonnet token, routing is a first-class
  lever in this frame and should come back into the proposal.
- **Q2 — `--max-turns` is a brake with an unmeasured downside.** A cap that truncates a turn
  mid-work causes rework, and rework costs more tokens than the cap saved. No source I found measures
  the cost of an over-aggressive brake. Set it from the measured distribution of tool calls per turn
  (the brief reports the member median moving 1 → 12), not from a guess, and make it fail toward
  completion.

**Rejected, and why:**
- *Re-running the day-vs-day comparison with better scripts.* Invalid design for a task-mix confound;
  better execution produces a more confident wrong number. §2.4.
- *Hunting for a turn that failed for lack of context* (brief §4.1). §5 — it will find nothing, and
  the nothing will be read as reassurance.
- *Removing or re-tuning the digest on these numbers.* F2 says it did not move context; that is not
  evidence it should change. Its case belongs to whoever owns latency and supervisor turn count.

---

## 7. What I did NOT verify. Explicitly.

- **The running binary.** I read branch source, the VPS's deployed git checkout, and its live config.
  I did **not** check what process is executing (decision 23, the fourth copy). **Assume the VPS may
  be running something else.**
- **The test suite.** Not run. No claim here depends on it.
- **Message text.** Never read, anywhere. No `~/.claude/projects` on the local Mac was read at all.
- **My own literature citations, in v1.** Three of them were wrong (§5) because I forwarded a
  research sub-agent's briefing without fetching the papers — the exact failure my own v1 §7 warned
  about and the reason the corrections session exists. In v2 every paper cited was fetched. **This is
  the sixth consecutive defect found by review on this line of work.**
- **The closing-spoke fix** (`41531e0`) — not reviewed.
- **Whether the pack contains the right things.** Untouched. §5 says why the proposed method would
  not have answered it either.
- **Supervisor wake-up count.** F9-bis names it as the gating number and I did not measure it;
  `orchestrator.log.jsonl` carries only `ts/orch/level/message`.
- **F9's premise, in v2 as first written.** I framed the supervisor setting as a possible oversight
  and had to correct it within the hour: it is documented, scheduled and owner-decided (F9-bis). The
  measurement stands; the framing was wrong, and I found that only because I went looking after the
  owner said he was not sure.
- **`--max-turns` semantics** — confirmed to exist and parse (F3); I did not confirm what it counts
  (agentic turns vs tool calls) or how it terminates.
- **The 330-call discrepancy** in §2.1 — hypothesised, not chased.
- **No adversarial review of v2.** v1 got one and needed it. This document is a claim set, not a
  clearance.

---

## 8. Where I am most likely wrong — attack these first

1. **F1 is load-bearing** and rests on a code comment plus a 21-hour work gap. Test it against the
   app's own log rather than config mtimes.
2. **F9's magnitude is an extrapolation, and the design doc says so first.** I measured that
   supervisors carry 546 k/call on `transcript`. I did **not** measure what they would carry on
   `fresh`. The project's own risk register got there before me — "supervisor judgement thins
   without its transcript … the one risk to watch with eyes" — and it is about quality, not tokens.
   Do not read F9's number as an argument to move the 11 Sep date earlier.
3. **The shape argument (§2.3) is an inference.** Find a counter-example: a pre-cut work session that
   stayed flat, or a post-cut one that grew.
4. **I established direction and mechanism, never magnitude.** Do not quote −76 % without the
   constant-model, constant-role, level-confounded qualifier.
5. **F3's +51 % per sub-agent call spans the same two confounded days** as everything else.
6. **I read no message text by mandate**, so I cannot separate "cheaper because the pack works" from
   "cheaper because the work was smaller". §2.4 is my defence and it is partial.

---

## 9. What `REPORT-corrections.md` changed

Every point it raised was upheld. Recorded so the next reader can see the shape of the error:

| Correction | Outcome |
|---|---|
| Frame is tokens, not dollars; split the three classes rather than price-weighting them | **Accepted.** §0, §2.1(b). Dollars kept as a secondary column. One challenge back: Q1. |
| F6 "no cache-aware logic" is beside the point — the orchestrator never calls the API | **Accepted, v1 was wrong.** F6 rewritten; the pack credited for the ordering it already does. |
| F7 downgrade from risk to check — mid-turn context is documented as append-only | **Accepted.** F7 rewritten. |
| Three literature citations misused (ACON, 2509.23586, Mem0/Zep) | **Accepted, all three confirmed against the primary sources.** §5, §7. |
| `--max-turns` is the brake that exists and is not passed | **Accepted and independently verified** on CLI 2.1.267 (F3), including that it is absent from `--help`. Challenge back: Q2. |
| Add effort-per-role (P6) and tool-output cap (P7); promote AgentDiet | **Accepted.** §5, §6. |
| Remove sub-agent model routing and TTL pricing as price-only | **Accepted for TTL. Challenged for model routing** — Q1. |

**What the corrections did not have, and this version adds:** F9. They were written without VPS
access (their own §4.1), so they improved the frame and the citations and added no measurement. F9
came from re-measuring the one bucket v1 had left unexplained.
