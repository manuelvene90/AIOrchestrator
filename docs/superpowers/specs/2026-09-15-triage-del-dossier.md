# Triage of the 2026-09-09 dossier, and of the 2026-09-14/15 audit

**Date:** 2026-09-15 · **Status:** TRIAGE — nothing decided, nothing built · **For:** Nathan

**Why this exists.** The dossier at `docs/superpowers/specs/2026-09-09-revisione/` (five documents,
347 KB) sat untracked for six days and was one `git worktree remove` away from being lost. It was
committed on 2026-09-15 (`5810ba4`). Between 2026-09-09 and today there are **333 commits on
`ours/integration` and not one of the dossier's top ten is among them** — verified by grep at
`5810ba4`: no `ITickStage`, no request idempotency, no buffer in `EngineStateSnapshot`, no fence
tracking, no OTEL. Nothing implemented, nothing parked, nothing recorded as rejected. It was never
triaged. This file is the triage, so that the same thing does not happen to it twice.

**What this file is not.** It decides nothing. Every row carries a proposed verdict so there is
something to disagree with, but the verdict column is the owner's to overwrite.

**Provenance of each row** is either `DOSSIER` (2026-09-09, read at a pre-merge HEAD — some rows are
stale for that reason and are marked) or `AUDIT` (2026-09-14/15, this session, re-verified against
`5810ba4` unless the row says otherwise).

---

## A. Already done — closed on 2026-09-15, listed so nobody re-opens them

| # | Item | Commit |
|---|---|---|
| A1 | `subagents/SKILL.md` denied `reviewerModel` / `soloModel`, which the code has had since 2026-09-09 | `85289e1` |
| A2 | Decision 12 described a closed failure class; rewritten to name the live cause (fence-blind parsing) and to record that `Break_SilentDeadlock_Async` does not exist | `be51520` |
| A3 | A machine with no PowerShell reported a failing test instead of a skip | `0bcc47a` |
| A4 | The dossier itself, and `the-brake-that-cannot-be-lifted`, committed before the fork is retired | `5810ba4` |
| A5 | The kit marketplace pointed at a deleted worktree, so the installed plugin froze on 2026-09-07 | machine config, not a commit |
| A6 | `vibe-framework` was enabled machine-wide, making its ~21 k agent prompt the system prompt of every session in every repo | machine config, not a commit |

---

## B. Urgent — a defect that is live, unattended, and costs something real

| # | Item | Provenance | Severity | Cost | Proposed verdict |
|---|---|---|---|---|---|
| B1 | **The owner's Telegram message can be destroyed.** The `getUpdates` offset is persisted before the words are durable; the buffer lives in RAM for ≥6 s and is not in `EngineStateSnapshot`. A host death in that window loses the message silently. | DOSSIER `audit-architecture.md:1011` — ranked #1 by value | **Critical** | Small | **DO FIRST.** In a system whose stated purpose is carrying the owner's words, this is the one defect that contradicts the product. |
| B2 | **An oversized `[n]` poisons a channel for ever.** `int.Parse` on an unbounded `(\d+)` throws after the offset advanced and before `Pending` clears, so it recurs every 2 s and nothing can even append the error report. | DOSSIER `audit-architecture.md:474-484`; **re-verified live** at `ChannelEntry_Parser.cs:26,159,167` | **High** | Small, but **three copies** | Fix by bounding the pattern to `\d{1,9}` in C#, in `channel-grammar.json`, and in the bash transcription — **together**. Bounding one scanner and not the others recreates the duplicate-index defect the tool's own comment warns about. Needs a test pinning all three. |
| B3 | **`DispatchPause_Gate` can latch and has no release.** Account swapped, real usage 70 %, dispatch blocked on a 95 % reading from the previous account for **4 days 14 hours**; two sessions and a reviewer never started; an operator with a shell was required. | `2026-09-11-the-brake-that-cannot-be-lifted.md`, PROPOSED, nothing built | **High** | Medium | Build the release path before wiring anything else into this brake. **This retires the earlier recommendation to connect the token budget to it** — a second trigger on a brake that cannot be lifted doubles the ways to be blocked for days. |
| B4 | **~35 channel-append call sites discard the "did I write?" boolean.** Alerts, nudges and request confirmations are silently dropped under a 1500 ms lock budget. Self-declared HIGH in `ChannelAppender`'s own docstring. | DOSSIER `audit-architecture.md:1012` | **High** | Medium | Decide a policy — retry, log, or escalate — and apply it once at the helper rather than at 35 call sites. |
| B5 | **Request files have no idempotency key.** Execute-then-delete means a host death re-runs `start-orchestration` and produces two orchestrations — the same failure decision 8 blames on `--continue`. | DOSSIER `audit-architecture.md:1017`; **re-verified**: zero hits for `requestId`/`idempot` in `GeneralSupervision/` | **High** | ~50 lines; the dossier records "**no downside**" | Do it. The cheapest high-severity row in the table. |
| B6 | **Fence-blind entry parsing.** An entry quoted inside a fenced block is split into phantom entries with duplicate indices, corrupting the mirror, the index screen, member state and the next-index computation. Likelier true cause of the `option-lab-2` duplicate `[80]` than agent-typed numbers ever were. | DOSSIER `audit-architecture.md:459-472` | **High** | Medium | Track fences in the parse. Note this is now recorded in decision 12, so the documentation no longer misattributes the cause. |

---

## C. Structural — worth doing, not on fire

| # | Item | Provenance | Proposed verdict |
|---|---|---|---|
| C1 | **No max-turns / round cap anywhere**; every timer in the system is an *anti-silence* timer that pushes more traffic. The one bound industry guidance asks for — a ceiling that stops — exists in name (`OrchestrationTokenBudget`, doc-commented "Runaway guard") and is **alert-only** (`BridgeEngineModel.cs:3368`). | AUDIT + DOSSIER | Make the budget bite. **After B3**, not before. |
| C2 | **Supervisor context grows monotonically** — 49 k → 400 k → 745 k → 862 k, 91 % dragged — because it is the only node still on `resume: transcript`. This is why cost is quadratic in crew size while the product is linear. | AUDIT | `runners.supervisor.resume = fresh` is a **config key, not code** — reversible in one line. It is step 5 of the 2026-09-08 spec and was never done. Owner's call: it costs continuity of judgement. |
| C3 | **The wake economy.** ~1 M tokens per supervisor wake; 247 of ~400 wakes caused by member traffic; the watcher fires >100×/day, mostly finding nothing. | DOSSIER (verbatim) + AUDIT | Digest to the **deliverable boundary**. Blocked on C4. |
| C4 | The digest ceiling **is** the default (5 min), and is pinned below `IMPLEMENTER_NUDGE_MINUTES` (8) by a test, because holding a report longer than the nudge window makes the app nudge a member for a silence the app itself causes. | AUDIT, verified in `Nudge_Windows.cs` | Raising one is **a change to the pair**. Decide how long a supervisor may be out of touch with its crew, then move both. |
| C5 | **Two contradictory instruction sets in fresh mode**: `FRESH_SESSION_PREAMBLE` says do not re-derive from the channel; `SKILL.md` §Boot says read it top to bottom; the skill arrives second. Nobody has measured which wins. | DOSSIER `audit-protocol.md:183` — ranked #1 protocol finding | **Possibly the largest single lever on spend**, and among the cheapest: select the boot text by regime. The dossier calls it the likeliest cause of the turn-count waste and the re-reading. |
| C6 | **No shared memory between members.** A fact imp-1 learns reaches imp-2 only if the supervisor relays it — and the supervisor briefs lean and has not read the code. A per-orchestration append-only `facts.md` "would be nearly free and is missing". | DOSSIER `audit-protocol.md:104` | Cheap, and it attacks the 32.3 % inter-agent-misalignment failure class directly. |
| C7 | **Channel reads have no acknowledgement or received-sequence check**; the wake is "read from your last entry down", positional and agent-judged. Nothing detects a member that mis-locates itself and skips a brief. | AUDIT + DOSSIER `§5.6` (fix already designed) | The design exists in the dossier. Worth landing. |
| C8 | **The 35-step mirror-tick order IS the specification and is untested.** The code's own comment admits moving a line "compiles, passes every test, and quietly reintroduces exactly the bug". | DOSSIER `audit-architecture.md:361-367` | The dossier calls the `ITickStage` pipeline "the highest-value refactor in this audit and it is not risky". |
| C9 | **`_lastUpdateId` written lockless on one loop, read under `_stateLock` on the other** — "the lock gives the appearance of protection without providing it". | DOSSIER `audit-architecture.md:409-415` | Small, and it duplicates owner messages after a restart. |
| C10 | **Single-poller invariant is per supervision root, not per bot token.** Machine + VPS on one token both long-poll and one steals the other's updates, undetected. 409 is unambiguous and could be fatal-and-named. | DOSSIER `audit-architecture.md:705-709` | Name the 409. Cheap. |
| C11 | **Statusline telemetry is structurally blind on the daemon** — `.usage.json` is written by a status line a headless `-p` session never renders, yet it gates `/cost`, the cards, **and dispatch pausing**. | DOSSIER `audit-protocol.md:289` | This is the mechanism class behind B3. The native fix (`ITurnResult`) already exists in-tree. |

---

## D. Hygiene — real, cheap, no urgency

| # | Item | Provenance |
|---|---|---|
| D1 | `kit/` duplication, measured: five byte-identical `print-runner.md`; `solo` is 35 % near-duplicate of `supervisor`; the supervisor skill is **49 % incident narrative**; the platform-code table is hard-coded three times in a "portable kit". Fix is a rule/why split with **zero rules deleted**. | DOSSIER `audit-protocol.md:138-179` |
| D2 | `solo/SKILL.md` tells the session to read the implementer's whole 24,951-byte skill to get one section — an ~8 k-token read for ~1 k of rules. | DOSSIER `audit-protocol.md:188` |
| D3 | COMMUNICATOR is declared retired in one skill and still shipped in six places. The audit also finds the communicator **arc** itself unnecessary: a whole session that tells the owner "the supervisor is busy", which the app knows because it dispatches the turn. | DOSSIER `:186` + AUDIT |
| D4 | `reviewer-readonly-check.sh` is 40,881 B / 870 lines of quote-aware bash whose own header records four failed matcher generations — policing a Bash tool the reviewer could simply not have. "The single most expensive artifact in the kit per unit of guarantee." | DOSSIER `audit-protocol.md:211` |
| D5 | The advisory hook layer is **measurably not running**: 52 hook executions exited 127. | DOSSIER `audit-protocol.md:115` |
| D6 | Windows-only owner commands (`/screens`, `/show`, `/organize`) ship enabled on the Linux VPS and silently no-op. | DOSSIER `audit-architecture.md:1020` |
| D7 | **154 compile-time tuning constants**, several admitting "THIS NUMBER IS A GUESS", against ~15 config keys. The owner can change the model from a phone and not the nudge interval. | DOSSIER `audit-architecture.md:666-682` |
| D8 | **No metrics at all, only a log** — no tick-duration histogram, no send/failure/contention counters, for a system with a 2-second control loop. | DOSSIER `audit-architecture.md:548` |
| D9 | `.claude/rules/git-and-boundaries.md` describes a fork/upstream split the repo has outgrown, and carries **no `paths:` trigger**, so it loads on every call. Its content is stale as of the 2026-09-15 consolidation. | AUDIT |
| D10 | `feat/fork-merge-and-profiles-spec` **does not exist** in any local branch, on `origin`, or on `upstream` — yet CLAUDE.md decision 26 cites it as the authority on the fork merge. Either it was never pushed, or it is lost. | AUDIT, verified |

---

## E. Rejected or retired — recorded so they are not proposed again

| # | Item | Why it is closed |
|---|---|---|
| E1 | "Put the sub-agent report on a file and return three lines." | Already the case, and already to spec: a crew report measured **1,929 tokens returned against 158,862 burned internally**, with the full text at `tasks/<id>.output`. Anthropic's own guidance is 1,000–2,000. Nothing to do. |
| E2 | "Enable tool search to cut the tool schemas." | Deferral is **already active**: ~250 deferred tool names carry no schema. The 46,695 tokens that remain are 16 built-in plus 27 host MCP tools, **none of them deferrable**. The saving was illusory. |
| E3 | "The Stop hook re-wakes every turn in a loop." | Refuted with live evidence: one wake per session per distinct announcement, guarded at `keeper.js:285` and `notice-state.js:465`; five of five session records showed one announce and zero nudges. Working as designed. |
| E4 | "Crew reports are what bloated the 450 k session." | Measured wrong the first time by counting JSON bytes. Real figure: **34,671 tokens over 11 reports, 8 %** of that session. The session is simply long — 1,160,320 characters of conversation at ~3.44 chars/token accounts for it with nothing missing. |
| E5 | "Wire the per-orchestration token budget into `DispatchPause_Gate`." | Retired by B3: the brake has a documented latch with no release. Build the release first. |
| E6 | "Trim CLAUDE.md to the recommended 1,000–3,000 tokens." | Not as stated. The narrative is load-bearing — it is what stops incidents repeating, and the ACE literature names the failure mode (brevity bias, context collapse). The move is **tiered loading**, not trimming: an always-loaded imperative core plus one `paths:`-triggered file per decision. Model-discretion retrieval is not an option here — measured at 0 pp improvement, never invoked in 56 % of cases; deterministic `paths:` triggers are. Two of three rule files already do this correctly. |

---

## What I would do first, if it were mine

1. **B1** — the owner's message. It is small and it contradicts the product.
2. **B5** — request idempotency. ~50 lines, no downside, prevents duplicate orchestrations.
3. **C5** — the fresh-mode contradiction. Cheap, and plausibly the biggest single lever on spend.
4. **B3** — the brake's release path, *before* anything else is wired into it.

And one process change worth more than any row above: **this file, or its successor, gets looked at.**
The dossier's real defect was not any of its findings — it was that 333 commits went by without one
of them being accepted, parked, or rejected on the record.
