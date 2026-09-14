# Brief for a fresh agent — working memory between turns, and what it cost

Written 2026-09-10 by the session that did the work of 2026-09-08 → 09-10, for a reader who was not
there. It is a HANDOVER, not a ruling.

---

## 0. How to read this document (read this section or you will inherit my mistakes)

**Nothing here is a conclusion you must keep.** Every substantive line is tagged:

- **[MEASURED]** — a number or behaviour I observed, with the command or log that produced it. You can
  re-run it. If you cannot reproduce it, the tag is wrong and I want to know.
- **[CLAIMED]** — my reasoning, my design judgement, my explanation of *why* something happened. This
  is the tag that has been wrong most often. Treat it as a hypothesis with a name attached.
- **[OPEN]** — a question nobody has answered, including me.

**Your job is not to continue my plan. It is to judge it.** Read the code and the production logs
first, form your own account of the problem, and only then compare it with mine. Where you agree,
say why with your own evidence — not by citing this file. Where you disagree, you are probably
right: I designed and implemented and then reviewed my own work, and on this project that has
produced a real defect **five times out of five** [MEASURED — see §7].

**Say which copy you read.** Every file here exists four times: the branch source, the build output,
the installed kit (`~/.claude/commands`, `~/.claude/hooks`), and the RUNNING binary. They drift by
construction. `CLAUDE.md` decisions 17, 18 and 23 are about exactly this, and five findings in one
evening were all "true of one copy, false of another".

---

## 1. The problem, in one paragraph

A headless Claude Code session (`claude -p`) driven by this app takes one turn per wake-up. Resuming
the transcript makes every turn re-send the whole accumulated conversation, so **cost = context size
× number of turns**, and the context grows monotonically. [MEASURED, 2026-09-06→08 on the VPS]
Carried context was **83 % of all input tokens** overall (supervisor 91 %, implementer 87 %), and the
cache-read share was **97.8 %** — i.e. almost all of it was re-reading, cheaply, an ever-larger
history. The question the owner asked was whether a session can keep the working memory it actually
needs between turns *without* dragging the transcript.

## 2. Where the code is

- Repo: `nathanthegrey/AIOrchestrator`, a fork of `manuelvene90/AIOrchestrator` (upstream, read-only).
- `ours/integration` is the fork's working branch; `stage/<n><letter>-<topic>` branches are packages
  to show upstream, each kept mergeable onto `master` alone. `.claude/rules/git-and-boundaries.md` is
  the authority — read it before any git verb.
- This work is on **`stage/11-the-proof-in-production`** (worktree
  `/Users/nvene/Visual Studio/AIOrchestrator-proof`), four commits: `e647b3b` (docs), `41531e0`,
  `f04362d`, `66a7dea`.
- **The main checkout is not exclusively yours.** [MEASURED 2026-09-10] Two merges appeared in
  `/Users/nvene/Visual Studio/AIOrchestrator` while this session was working (`stage/10` at 14:14,
  `stage/8c` at 14:43) that this session did not make, and the `stage/8c` merge is **committed and
  not pushed**. Do not finish or abort an operation you did not start: measure whose it is, park it,
  report it.
- Production is the VPS (`orch@159.195.254.120`), which pulls `ours/integration`. The mandate for a
  reviewing session there was **read-only**, and it should stay that way: read and aggregate, write
  nothing, and pipe scripts over stdin (`ssh … 'python3 -' < script.py`) rather than leaving files in
  `/tmp`. Transcript files there are **metadata only** — `usage`, timestamps, model, tool names, field
  presence. Never the text of a message.

## 3. What was shipped, and what is actually proven

Five changes went live on the evening of 2026-09-09. For each: what it does, then the evidence.

### 3.1 The state pack — the core of the answer
Fresh turns no longer resume the transcript. The bridge assembles the memory a turn needs into a
file (`<member>/pack.md`) and the turn starts clean. It is a FILE and not stdin because the CLI
appends stdin to the positional prompt, which pollutes a slash command's `$ARGUMENTS` [MEASURED
against CLI 2.1.266/267].
Code: `AIOrchestratorCoreLib/Running/StatePack/*`, `PrintTurnExecutorModel`.
**Evidence:** a member's first-call context went from a median of **290 k to 60 k tokens** and member
tokens from 421 M/day to 105 M/day at the same call count [MEASURED — §5].
**What is NOT established:** whether the pack contains the right things. Nobody has measured a turn
that FAILED because something it needed was not in the pack. That is the most valuable unmeasured
thing in this whole story. [OPEN]

### 3.2 The mid-turn advisory (soft boundary)
A `PreToolUse` hook counts a turn's tool calls and, past a threshold (35, `AIORCH_SOFT_BOUNDARY_CALLS`),
injects an advisory into the turn via `hookSpecificOutput.additionalContext`. It advises; it never
blocks (decision 21: hooks advise, the app enforces).
Code: `kit/hooks/soft-boundary-check.sh`. Keyed on `prompt_id` (one turn) with `session_id` fallback.
**Evidence:** **9 sessions** received a real advisory [MEASURED 2026-09-10].
**How to count it, and the trap:** count sessions containing BOTH `hook_additional_context` AND the
hook-only wording `IT HAS READ NO CLOCK`. **Do NOT count the phrase `SOFT BOUNDARY`** — it now lives
in skills that every session loads, so it matches sessions the hook never touched.
**What is NOT established:** whether the advisory changes behaviour. Nine deliveries prove arrival,
not effect. Nobody has compared turn length or tool-call count before and after a delivered advisory.
[OPEN — and it is cheap to measure]

### 3.3 The member digest
Member reports are held up to `printRunner.memberDigestMinutes` (default 5, 0 = off) so several
arrive in one supervisor turn. Escalations are never held: `BLOCKED ON OWNER`, `QUESTION:`, an
owner message, and a member's first entry all go immediately.
Code: `WakeUp_Policy`, `PrintTurnDispatcherModel.Resolve_DigestHold`.
**Evidence [MEASURED 2026-09-10, `orchestrator.log.jsonl`]:** 12 releases, **3 of them carrying two
entries** = 3 supervisor turns not taken. A supervisor turn on that orchestration averaged **607 k
tokens of carried context** (mean of 171 turns that day). Nine releases carried one entry and saved
nothing while costing 5 minutes of latency each.
**[CLAIMED]** that coalescence rises with workload density — the three multi-entry releases clustered
in the busiest stretch. Three data points. Do not treat this as a trend; re-measure.
**The ceiling is a coupling, not a preference:** `IMPLEMENTER_NUDGE_MINUTES = 8` (now
`Status/Nudge_Windows.cs`) is when the app nudges a supervisor for an unanswered report, and the
clock runs from the member's report — so a digest longer than that window makes the app nudge for a
report it is itself holding. Pinned by a test since `f04362d`.
**[OPEN]** The better variant, designed and deliberately not built: release when **no member has a
turn in flight**. In today's workload it would release immediately — i.e. the digest would switch
itself off when it cannot help. Whether that is better than a fixed window is a real question and it
is the owner's call.

### 3.4 The closing-spoke report — read this one carefully, it is the cautionary tale
Before a member is closed, a line says which of its entries the supervisor was never handed (closing
stops the spoke being a source, and the cursor is kept, so those entries reach nobody).
Code: `AIOrchestratorCoreLib/Bridge/UndeliveredSpokeTraffic_Reporter.cs`.
**It fired for the first time in production on 2026-09-10 and it was FALSE** [MEASURED]:

```
14:06:13  imp-3 files entry [72] "Done. F1 closed, origin/dev merged, all gates re-run"
14:11:15  the digest releases it; supervisor turn sup/136 starts, carrying [72]
14:12:43  the reporter: "imp-3 is being closed with 1 entry its supervisor was never handed: [72]"
14:12:58  sup/136 ends — success. The entry had been delivered.
```

The cursor advances when a turn COMPLETES, so throughout a turn's run the entries it is delivering
still read as pending. `41531e0` fixes it: the dispatcher records what each in-flight turn launched
with, and the reporter splits "nothing is carrying this" (warning) from "a turn is carrying this"
(info naming the condition — because a turn that FAILS leaves them pending, so suppressing the line
would trade a false alarm for a silent drop).
**Reviewed adversarially, 2026-09-10, nothing CRITICAL.** The two ways it could have been an inert
no-op were both REFUTED by a probe driving the real dispatcher: `SUPERVISOR_MEMBER_ID` is `"sup"` and
matches the registration path, and the two sides compute the digest through the same
`ChannelHistory_Cache.Read_Entries` → `Select_Pending` pair, so the identity sets do intersect. That
probe is now a kept test (`InFlightDeliveryIdentitiesTests`) and it carries its own control: with an
empty set the false alarm of 14:12:43 reproduces on demand.
The review's other findings were fixed in the same pass — an `orchId` differing only in case or
whitespace used to miss every lookup here while the close itself succeeded (the session store
resolves through a file path, case-insensitive on Windows), which handed the reporter an empty set and
resurrected the false alarm; the three session-keyed dictionaries are now case-insensitive and the key
is trimmed. And `Log_BeforeClosing` — the method that picks WARNING vs INFO — had no test at all: the
level could be inverted with all seven cases green.
**And it settled an owner question with a measurement:** whether closing a member with unread traffic
should be *prevented* rather than logged. It should not — prevention would have refused exactly that
close, of the member that had just filed its final report.

### 3.5 The per-role model split, and three smaller ones
Models per role are `supervisorModel` / `implementerModel` / `communicatorModel` /
`generalSupervisorModel` in the bridge config (there is no `reviewerModel` or `soloModel` — those
ride the implementer default). The VPS installer's step-7 defaults were changed from four `haiku` to
`opus/opus/sonnet/sonnet`. Also shipped: the drain (`ShutdownGrace_Rule`), model-name validation
(`SpawnCommand_Builder.Validate_Model` — the model was the only unquoted interpolation into a
PowerShell script), and a plan-backend config that fails loudly instead of silently.

## 4. The open items, as questions rather than tasks

1. **Does the pack contain what a turn needs?** (§3.1) Nobody has looked for a turn that failed for
   lack of context. This is the load-bearing assumption of the whole design.
2. **Does the advisory change anything?** (§3.2) Arrival is proven, effect is not.
3. **Is a fixed digest window the right shape at all?** (§3.3) Or should it release on crew silence?
4. **Where the cost went.** [MEASURED] Sub-agent tokens nearly doubled (123 M → 237 M) and a member's
   calls-per-turn went from a median of 1 to 12. The expensive thing did not disappear, it moved
   inside long turns and into fan-out — which is where the advisory now watches and nothing brakes.
   Whether anything SHOULD brake there is [OPEN] and is the biggest design question left.
5. **`tools/token-gate/` has the cut date and two session ids hardcoded**, so it cannot measure two
   arbitrary days without being rewritten. Making it take a window is small and unblocks every future
   reading.
6. **The denominator does not exist.** "Tokens per delivered item" is not computable: there is no
   ledger-close event in `orchestrator.log.jsonl`, and the orchestrations' `PLAN.md` is not under git,
   so there is no history to difference. Either the app should emit such an event, or the question
   should be reframed. [OPEN — and worth deciding before anyone promises that number again]
7. **[OPEN, and named by the review] Nothing ever resolves the in-flight conditional.** The reporter's
   INFO line says "if that turn fails these reach nobody" — and if the turn does fail, that line is
   the only record that will ever exist. After `Close_Member`, `TurnSources_Resolver` skips the member
   for good: the entries can never be pending again, the archived-undelivered warning iterates only
   RESOLVED sources, and no later close re-examines them. Better than a WARNING with a false claim,
   but not the same as catching the drop. **The fix the reviewer proposed:** one warning on the turn's
   FAILURE path, which already holds the pending set and can ask the store which members are closed.
   Left undone deliberately — a new warning on the failure route, untested against real failure modes,
   at the end of a long session, is how the previous batch earned five review findings.
8. **[OPEN, LOW] The reporter is not archive-aware.** It reads the live channel file only, so if
   `Channel_Compactor` moves an undelivered entry to the archive between the two reads the line goes
   SILENT rather than wrong. Pre-dates this work; `ChannelHistory_Counter` exists for exactly this
   (decision 13) and is not used here.
9. **Parked, with a destination:** the `QUESTION:` literal appears in several spellings and the
   entry author is not role-bound. Delivered to another line (E3, commit `ff24382`), which starts
   after its brief C reaches production. Do not re-solve it.

## 5. The measurement, and how to redo it

[MEASURED 2026-09-10, deduped by `(sessionId, requestId)`, metadata only]

| | 2026-09-08 (before) | 2026-09-10 (after) |
|---|---|---|
| Tokens, whole day | 773.1 M | **597.0 M** (−23 %) |
| Model calls | 3,406 | 4,606 (+35 %) |
| Member first-call context, median | 290 k | **60 k** (−79 %) |
| Member tokens | 421.4 M | **105.3 M** (−75 %, same call count) |
| Supervisor tokens | 228.1 M | 157.1 M (−31 %) |
| Sub-agent tokens | 123.5 M | **237.0 M** (+92 %) |
| Member calls per turn, median | 1.0 | 12.0 |

**Caveats that belong with those numbers, not after them:** different orchestrations were active on
the two days, so part of the difference is task mix and not process; 97.6 M tokens of 2026-09-10
(16 % of the day) could not be attributed to a role without reading message text, which is out of
mandate; and the gate scripts were adapted rather than run as shipped (§4.5). A fresh reading should
fix the scripts first and then re-measure, rather than trusting this table.

## 6. Traps this session actually fell into (each cost real time)

- **macOS toolchain.** `export PATH="$HOME/.dotnet:$PATH" DOTNET_ROOT="$HOME/.dotnet"`. There is no
  GNU `timeout`. Test the PROJECT (`AIOrchestratorCoreLib.Tests/AIOrchestratorCoreLib.Tests.csproj`),
  never the `.slnx` — the WPF app is `net10.0-windows` and fails with `NETSDK1100`. I reported three
  "suite runs" that had never run because of this.
- **One suite at a time on this machine.** Five different tests went red once each across a night of
  three concurrent suites, and **not one was a defect** — the harness polls wall-clock deadlines while
  spawning processes. Re-run a red alone with `--filter` before believing it. My first diagnosis of
  those reds was wrong and is recorded in `.claude/rules/code-conventions.md` on purpose.
- **On a file another process is still writing, absence is not a measurement.** I twice declared a
  detector broken because a field was missing from a transcript that was still being appended to; it
  appeared minutes later, both times. Presence is a valid conclusion, absence is not.
- **"Done" and "delivered" are not the same thing.** One fix sat uncommitted in a worktree while I
  had already told the owner it was "closed and pinned by a test". It surfaced only because the owner
  noticed an unmanaged branch.
- **A green suite and a clean `git log` say nothing about what is running.** Decision 23.

## 7. Where I am most likely wrong — attack these first

1. **I designed, implemented, and reviewed my own work.** Five adversarial reviews on this line found
   real defects in 5/5 changes *after* I had read the diff and seen green — including a critical
   regression introduced by one of the fixes. The base rate says there is at least one more.
2. **The digest's value rests on three data points** and on my story about workload density (§3.3).
   That story is the kind of explanation that feels right and predicts nothing.
3. **I have no evidence the pack is sufficient** (§3.1) and I shipped it anyway.
4. **I have no evidence the advisory does anything** (§3.2) and I shipped that too, then called it
   "proven" — meaning proven to ARRIVE. Watch me for that word.
5. **The −23 % is a two-day comparison across different orchestrations.** It is the headline number of
   this whole effort and it has a task-mix confound I could not remove.
6. **My own instruments produced two false alarms** about my own features (a wrong grep, a premature
   read). Do not trust a watcher I wrote without reading what it greps for.

## 8. What to do first, if you want a recommendation

Not the code. **Reproduce §5 with fixed scripts** (§4.5) — if the −23 % survives an honest window and
an honest role split, the design is sound and the remaining work is §4.1–§4.4. If it does not
survive, everything above is a story about a number that was not there, and that is much more
important than any of the fixes.
