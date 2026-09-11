# Token efficiency — stage-scoped sessions, app-assembled state, honest accounting

**Date:** 2026-09-08 · **Status:** round 1 LIVE since 2026-09-08 22:34 CEST; plan revised 23:00 the same evening by the independent review (owner directive: "take command; tomorrow we continue with you") · **Owner:** Nathan
**Revision 2 (23:00):** C5 (skill diet) PARKED by owner decision; C2 enriched with the memory the bridge already holds; `STATE:` shrunk and kept out of the channel; C4 moved before the supervisor goes fresh; verification split into production / orientation / rework; C7 attribution by session id; a net for the roles still on `transcript`. The review that motivated it: measured 2026-09-08 on the VPS (see §5c).
**Provenance:** two independent audits (A, B) merged into `2026-09-08-piano-consumo-token.md`, then an adversarial challenge that re-measured every load-bearing figure. This spec supersedes the Italian working document; where the two disagree, this one carries the corrected numbers.

**Copies read.** Code: `ours/integration` HEAD `1fafaba` (Mac source; VPS clone at the same HEAD; the running daemon binary at `/opt/aiorchestrator`, built 2026-09-08 04:17, verified at HEAD by symbol). Installed kit identical to `kit/`. Live data: VPS `orch@159.195.254.120`, read-only — Claude Code transcripts under `~/.claude/projects`, registries under `~/.claude/supervision`, `orchestrator.log.jsonl`, `config.json`; window 6 → 8 Sep 2026. CLI: installed binary 2.1.263 (help text and embedded strings).

Every figure is tagged `[measured]` or `[estimate]`. Estimates are built on measured ratios and say so.

---

## 1. The problem

Cost is the sum of the context of every model call. Today a session's context is its whole conversation since it started, and it never resets. Every gesture — one `Read`, one `Bash` — resends it.

### Corrected totals `[measured]`

The transcripts write one line per content block (`thinking`, `text`, `tool_use`) with the **same** `message.id`, `requestId` and `usage`. Both audits summed lines. Deduplicated by `message.id`:

| | audit A | corrected |
|---|---|---|
| API calls | 18 480 | **9 726** |
| total tokens | 3.76 B | **2.04 B** (cache read 1 985 M, cache write 48 M, output 6.6 M) |
| average context per call | 197 k | 209 k |
| cache-read share | 97 % | 97.3 % |
| main sessions / sub-agents / daemon one-shots | 3 168 / 478 / 13 M | **1 760 / 264 / 13.5 M** |

Ratios survive; every absolute number in the working document is ×1.85.

### Where the context comes from `[measured]`

Main-session context, 1 756 M, split per call into boot (system prompt + role skill, capped at 36 k), context **carried from earlier turns**, and growth **inside the current turn**:

| role | calls | Σ context | carried | inside turn | fresh-per-turn Σ | ratio |
|---|---|---|---|---|---|---|
| supervisor | 1 018 | 406 M | 91 % | 2 M | 37 M | 11× |
| implementer | 2 547 | 1 134 M | 87 % | 72 M | 161 M | 7× |
| reviewer | 568 | 126 M | 75 % | 13 M | 33 M | 3.9× |
| general (already fresh) | 283 | 11 M | 0 % | 2 M | 12 M | 1× |
| **all** | 5 262 | 1 756 M | **83 %** | 8 % | **318 M** | **5.5×** |

The last two columns are what the same calls would cost if each turn started from a fresh session with the same boot: an **upper bound**, ignoring the state the fresh session must be handed.

### Interrupted work — the 28 % / 66 % divergence, closed `[measured]`

Method: segment each transcript into attempts (every `[bridge turn <id>]` prompt or role command), sum the deduplicated calls per attempt, match to a registry result of the same session with identical `cache_read` and `output` sums, classify the rest with log lines within ±4 min.

| class | tokens | share of main | evidence |
|---|---|---|---|
| completed, counted exactly by the registry | 1 053 M | 59.8 % | 704 exact matches |
| **completed, under-reported by the registry** | 246 M | 14.0 % | log `ended — success`; the CLI's `result.usage` resets at every `<task-notification>` (7/7 examples carry one, 3/704 exact matches do) |
| killed by the 30-min timeout | ~220 M | ~12 % | `attempt N timeout` lines |
| **killed by a daemon restart** | 112 M | 6.4 % | 17 attempts ending within 4 min of `Daemon stopping`; 21 daemon starts in the window |
| unexplained residue | ~85 M | ~5 % | |
| boot turns, API errors | 44 M | 2.5 % | |

**Interrupted work is 19–27 % of main-session tokens (17–24 % of the total).** Audit B (28 %) was close. Audit A's 66 % divided a deduplicated registry (1 069 M) by double-counted transcripts (3 168 M) and treated the CLI's under-reporting as dead work.

A killed attempt is retried with `--resume` of the **same transcript** (log and `PrintTurnDispatcherModel` agree), so the model sees its partial work; the waste is the re-read, not the loss.

### What the counter cannot see `[measured]`

The app's registry (`turns.jsonl`, `result.usage`) misses: the killed attempts (no result), 14 % of completed turns (resets at background-task notifications), and every sub-agent call (264 M — exact main-only matches prove `result.usage` excludes sidechains). It sees roughly 60 % of consumption.

### Other findings that bind the design `[measured]`

- Prompt cache TTL is **1 hour** (`ephemeral_1h_input_tokens` 13.9 M, `ephemeral_5m` 0). A fresh session's boot is a cache read when the prefix is identical (general supervisor: first call 32 k, cache write 0).
- The CLI reports `contextWindow = 1 000 000` for opus-5 and sonnet-5 in every result. `--autocompact`, `autoCompactWindow`, `CLAUDE_CODE_AUTO_COMPACT_WINDOW` (takes precedence), `CLAUDE_CODE_DISABLE_1M_CONTEXT` and **`--max-budget-usd`** exist in 2.1.263.
- A fresh turn today (general supervisor) spends **9.2 calls, 7.8 of them reading channel and `PLAN.md` files** — the model rediscovering state the app already holds. Once sessions are fresh, this is the dominant cost.
- The app writes ~1 300 bookkeeping entries into the channels (`turn_ended` 1 274, `STATUS` 232, ledger nags 320, nudges 140). Zero turns were triggered *only* by app entries, but every session reads them at boot.
- The supervisor wakes ~400 times (one every 6 min), 2.5 calls per wake-up, mean context 398 k; 15 % of its turns are acknowledgement-sized (< 600 output tokens); 247 wake-ups come from member traffic, 150 from the owner.
- Every role runs on opus (`config.json`), the implementers included.
- The weekly limit rose 15 points while the VPS read 69 M; the owner's Mac made 1 109 M tokens on the same days (vs 2 040 M on the VPS). Whether the limit weights cache reads like fresh input could not be determined (integer steps, second consumer).
- The `run-to-the-end` Stop hook blocked 3 times in 435; 52 hook executions exited 127 (command not found).

---

## 2. Principle

**Durable state lives on disk; a session's conversation is disposable. The app hands a session its state; the session does not go looking for it.**

Corollary: a restart, a rotation, a model change and a stage boundary become the same operation, and they are free.

## 3. Goals and non-goals

Goals: 3–5× fewer tokens per delivered item `[estimate]`; interrupted work under 5 %; an accounting that sees 100 % of consumption; the supervisor chooses each implementer's model; no loss of the owner-facing guarantees (work runs to the end of the endeavour, the owner talks to the supervisor as today).

Non-goals: replacing the supervisor's judgement with app logic; disabling the 1 M window; touching sub-agent architecture before the stage regime is measured; solving the account-sharing question in code.

---

## 4. Changes

### C1 — Stage-scoped sessions

A **stage** is one process with a **new session** — never a resume of the previous one — whose prompt is the role command plus the state pack (C2). It closes on a **stable, verifiable state**: for writers, tests green and a commit; for reviewers and investigations, the verdict or findings filed in the channel. The final message ends with a `STATE:` block (C1.2). Continuation does not pass through the supervisor: the final message declares `continue | done | waiting | blocked`; on `continue` the bridge opens the next stage at once. Work runs to the end of the endeavour as today.

**C1.1 Mechanism.** `runners.<role>.resume: fresh` for every role (the mode exists; the general supervisor runs it). The supervisor keeps the stream runner for owner latency; only its `--resume` becomes a new session per wake-up.

**C1.2 `STATE:` block** — protocol; the bridge parses it defensively and never refuses a turn over it. Fields (five fixed keys): goal of the stage; branch and commit; the command that proves the state; next step with file and line; **dead ends** (conclusions, not pointers — the one thing git and the channel cannot reconstruct). **≤ 2 KB (~500 tokens), truncated, never rejected.** The bridge **strips the block from the channel entry** before appending it — the final message IS the entry and the entry is mirrored to the owner's phone, so a block left in place would bloat exactly the file a fresh session reads and reach the owner as noise (decisions 14–15) — and stores it as `<member>/state.md` for the next state pack. Two keys are checked where checkable (`git cat-file -e <commit>`; the `file:line` exists) and marked `[unverified]` in the pack when they fail; the verifying command is declared, never run by the bridge. A Stop hook may **advise** when work happened without a block (kit, decision 21: hooks advise, the app enforces at the point of effect — here the pack's fallback). If the block is absent, the pack carries the member's last own entry (C2) — never "the last three entries", which do not guarantee the brief. Git holds the state; the block holds the intention.

**C1.3 A killed stage gets one resume, then a fresh start.** A killed attempt never wrote its block. The bridge opens **one** turn on the killed transcript (now small) with the prompt "write `STATE:` and stop"; the next stage is fresh from that block. This replaces the working document's absolute "an interrupted attempt is never replayed", which would restart from a stale block and lose the interrupted stage's reasoning.

**C1.4 Stage cap.** Twelve consecutive `continue` stages on one brief flag the supervisor (rework signal). The app flags; the supervisor decides.

### C2 — App-assembled state pack

The first message of every stage carries, assembled by the bridge from what it already holds — **deterministic, so it cannot lie or go stale the way a model-written sheet can**:
- the new channel entries (verbatim, as today — shipped as the first slice, `3537d57`);
- **the member's brief**: the oldest `FROM supervisor` entry without a reply from the member, looked up across the live file **and** `channel.archive.md` through `ChannelHistory_Counter` (decision 13) — measured 2026-09-08: 3 of 25 member channels are compacted, the live file keeps 45 entries ≈ 29 turns, and the archive was opened by a session twice in the whole window, so a long-lived member's brief is otherwise lost;
- **the member's last own entry** (its report is its state) and the `STATE:` block if one exists;
- `git status --short` and `git log -3 --oneline` of the member's worktree (the code state, which never goes stale);
- the `PLAN.md` lines that name the member; for the supervisor the whole `PLAN.md` **and the last 5 owner-channel entries** (the owner's messages refer back to earlier exchanges; pending entries alone are not enough to answer them);
- the member roster with status; the source contract.

The role command's boot sequence stops saying "read your channels" — the channels are in the message. Target: from 9 calls to ≤ 2 per acknowledgement-sized turn `[estimate on 7.8 measured file-reading calls]`. Size budget for the whole pack: ≤ 6 k tokens `[estimate]`; PLAN.md today is 8–15 KB (2–4 k), a channel entry ~600 tokens, 1.56 pending entries per turn (median 1).

Layout is **cache-first** (1 h TTL, prefix match): stable material first and always in the same order — system prompt, role skill, `PLAN.md` — volatile material last — entries, state, timestamps. A test pins the order.

### C3 — Channels carry words of people and agents only

App bookkeeping (`turn_ended`, `STATUS`, ledger advisories, orphan/respawn notes, contract violations) moves to `<member>/status.jsonl`, shown to a session only inside the state pack when relevant, and to the owner only where decision 15 already allows. The channel stays the human-readable record and the wake-up mechanism. Nudges that fire while a stage is running are dropped (a stage cannot read them).

### C4 — Wake-up policy

Owner messages wake the supervisor immediately, as today. Member entries are **digested**: delivered when the member's stage ends, or every N minutes (default 5) if it runs longer. Deterministic acknowledgements — a turn ended, a state updated, a ledger line moved — are handled by the app and never wake the supervisor. The supervisor is woken to decide, not to take note. Judgement stays with the supervisor: this is not the "externalised brain" the working document rejected.

**Scheduled BEFORE the supervisor goes fresh (step 3, revision 2).** Today a wake-up costs ~1 M tokens (2.5 calls × ~400 k) and 247 of ~400 wake-ups come from member traffic: halving the wake-ups is roughly a 2× on the most expensive role **without touching its memory** — the one lever here with no continuity risk. The supervisor's own transcript stays until the pack (C2) exists to replace it.

### C5 — Role instruction diet — PARKED (owner decision 2026-09-08 23:00)

Removed from the plan. The premise ("boot is about half of the remaining bill") counted the whole first-call context as skill; measured 2026-09-08 from first-call contexts (implementer 33.9–46.6 k, reviewer 33.9–44.5 k, supervisor 55.9–56.9 k, general 29.7–32 k) and the supervisor−implementer delta (22 k for 62 KB more skill ≈ 2.8–3.2 bytes/token), the skill is ~8 k of a member's 34–47 k boot: the rest is Claude Code's own system prompt, tool definitions and the `ianus` MCP tools. A diet buys a member little and the supervisor 27–30 k → it is not the lever now. **Reopen only if** a measured empty `-p` turn (with and without the skill) shows the skill above 40 % of the supervisor's boot. No `- [ ]` line, no acceptance criterion: parked means outside the denominator (decision 22).

### C6 — The supervisor chooses the model per task (owner directive)

The brief carries `MODEL: sonnet | opus` for the implementer, and the reviewer's brief likewise. The bridge reads the line and starts that member's next stage with `--model` accordingly; a stage is a new process, so the switch is free and per-task. Default in the supervisor protocol: sonnet for well-specified, mechanical work (ports, tests, guided refactors); opus for design, ambiguity, unexplained failures, review. The configuration model becomes the fallback when the brief says nothing. Sub-agents follow `aiorch:subagents`: read-only fan-out on a small model unless the brief says otherwise.

### C7 — First-class accounting

The registry is built from the **transcripts**: deduplicated by `message.id`, sidechains included, killed attempts included, attributed to `(orchestration, member, stage)` by the **session id of the stage** — not by the `[bridge turn]` prompt, which a fresh print turn carries only on stdin and a boot turn not at all. The dispatcher already logs the id at every start (`PrintTurnDispatcherModel.cs`, "fresh session <id>"); `executed_turns` in `print-session.json` must record it too (today: turn number, request id, entry indices, outcome, cost — no session id). `result.usage` is no longer a source (it resets at background-task notifications). One reader (decision 10) feeds `/tokens`, `/cost`, the limit alerts and the new owner-visible metric: **tokens per delivered item**, where a delivered item is a ledger line closed with a reviewer verdict and a merged commit.

### C8 — Drain before restart

On `SIGTERM` the daemon stops opening turns, waits for running turns (up to the turn timeout), then exits. Deploys stop killing work (112 M in the window).

### C9 — Budget per stage (last)

Three parts, each existing already or cheap:
- **Sensor:** the CLI appends every call to the session's transcript; the bridge tails it (it tails files already) and knows each call's context for every role, print or stream.
- **Advice:** a `PreToolUse` hook reads its own transcript and, past the soft threshold, tells the model to reach a stable state and write `STATE:`. `[unverified]` that `PreToolUse` can inject text into context in 2.1.263; fallback is advice at the next stage.
- **Net:** `--max-budget-usd` per process. When it trips, the turn ends with an error result; the bridge runs the C1.3 closing turn, then a fresh stage.

Calibration on **calls and inside-turn growth**, not on today's token distribution (which measures the disease): today p50 = 2 calls, p80 = 9, p90 = 18, max 59. The ceiling is set where 80 % of today's turns pass whole. No adaptive budget: the app proposes a new ceiling with the distribution in hand; the owner decides.

### C10 — A net for the roles still on `transcript` — DROPPED (owner decision 2026-09-09 10:15: "no shortcuts; if it costs more now, so be it — do it properly")

Not applied, and the one-off rotation of the two supervisor sessions not done either. The supervisor reaches the new regime by the full road — C2 pack, C4 digest, then fresh (step 5) — and pays the transcript price until then. **Expected uncontrolled event:** the fincanva-2 supervisor transcript (`fee5320d`) sits at 940 k on a 1 M window; the CLI will compact it on its own (seen twice on 2026-09-08 at 940–965 k → ~35 k). When it happens it is logged against the gate, not treated as a result. Original text kept below for the record.

#### (as proposed, superseded)

Supervisor, solo and communicator keep `resume: transcript` until step 4; the supervisor ran at 746–865 k context per call on 2026-09-08. Until then, pass `--autocompact` (or `CLAUDE_CODE_AUTO_COMPACT_WINDOW`) at ~300 k through the `environment` dictionary — applied **after** the `CLAUDE_CODE_*` scrub (verified: `PrintTurnRunnerModel.cs:116→120`, `StreamSessionProcess.cs:118→122`), so nothing is weakened. One config line, reversible, no protocol change. Measured: the CLI compacted twice in the window, both at ~940–965 k, so the mechanism works; unverified: its interaction with `--resume` across processes and the quality of its summary — hence a net with a logged alarm, not the design. Owner decision needed before it touches the VPS (production).

### Kept from the working document

`--autocompact` as a net that must never fire in fresh stages (firing is a logged alarm). The 1 M window stays enabled. Phase-1 read-only rules unchanged.

### Dropped from the working document

The 3.76 B / 18 480 / 66 % figures; "5-minute cache" (it is 1 h); "an interrupted attempt is never replayed" in absolute form; the token budget as the first structural step; switching off nudges as a savings item (0 turns triggered by them).

---

## 5. Rollout

Each step ships alone, measured on **one** orchestration for two days before extending. Acceptance is written before the work.

| step | change | done when |
|---|---|---|
| 0 | C8 drain (shipped 2026-09-08, `stage/4a`); C7 counter (dedupe, sidechains) — measured for now by the challenge's scripts, in-app in round 2; owner decided 2026-09-08: one account, the weekly limit is not a criterion | zero attempts ending within 4 min of a daemon stop over 5 deploys; registry ↔ transcripts within 5 % |
| 0.5 | live CLI test positional slash command + stdin (after 05:00, weekly limit); C7 counter by script (dedupe `message.id`, sidechains); session id per stage recorded in `executed_turns` | the test names which of the three stdin outcomes the CLI has; tonight's fresh turns measurable per stage |
| 1 | **Gate of round 1** (C1.1 fresh implementer + reviewer, live since 22:34) — criteria in §6, revision 2: tokens per delivered item ≥ 3× lower; production calls per item ±15 %; rework ≤ +10 %; attempts per turn ≤ 1.1; 10 verdicts per arm read blind by the owner. Below 2× on tokens: stop and rethink | 2026-09-09 afternoon |
| 2 | **C2 complete** (brief across live+archive, last own entry, git status/log, PLAN lines, owner tail for the supervisor) + C3 bookkeeping out of what a fresh session reads | calls per acknowledgement-sized turn ≤ 2; zero fresh sessions opening `channel.archive.md` |
| 3 | **C4 wake-up digest** for the supervisor, before it goes fresh | supervisor wake-ups per day −40 % `[estimate]`; owner reply latency unchanged |
| 2b | **C1.3 closing turn on timeout** — pulled ahead 2026-09-09 15:00 (revision 3): under fresh a killed attempt loses everything; `imp-5/2` burned 16.2 M tokens in two kills before a third attempt succeeded | kills followed by a report ≥ 90 %; tokens in killed attempts < 5 % |
| 4 | C1.2 `STATE:` (≤ 2 KB, stripped from the entry, two keys checked) — **only if the gate shows rework** (rework > +10 %) | rework back within +10 % |
| 5 | C1 for the supervisor, with the C2 pack | supervisor mean context < 80 k; verdict quality judged by the owner (blind) |
| 6 | C6 model per task (orthogonal — any time) | share of implementer stages on sonnet reported, quality per verdict |
| **3a** | **C9 budget per stage — pulled forward by Gate 2 (§5f)**: a soft boundary inside a fresh turn (calls / tokens), the closing turn as the hard net | share of member tokens in deadline-killed turns from 41 % to < 10 %; turns > 40 calls reported |
| — | C5 skill diet — PARKED; C10 net for `transcript` roles — owner decision pending | — |

Expected end state `[estimate]`: main-session context 5.5× lower as an upper bound; 3–4× on total tokens once state packs, `PLAN.md` and re-reads are paid; 2–4× on the weekly limit depending on how it weights cache reads — unknowable while the Mac shares the account.

---

## 5b. Status — 2026-09-08 22:50 (round 1 live)

Owner decisions taken: one account (the weekly limit is not a criterion); round 1 approved and deployed the same evening; the guinea pig is every open orchestration (`fincanva-2`, `fincanva-3`) because the flag is per role.

| item | state | evidence |
|---|---|---|
| C1.1 fresh implementer + reviewer | **live** | VPS `config.json` `runners.{implementer,reviewer}.resume = "fresh"` (backup `config.json.pre-round1-*`); log 22:34 `print turn fincanva-3/imp-2/7 … fresh session dcb15892…` |
| C2 first slice — fresh turn gets its entries on stdin | **live** | `stage/4a` commit `3537d57`, merged as `926cc6b`; `/opt/aiorchestrator` DLL built 22:33 carries `FRESH_SESSION_PREAMBLE` |
| C8 drain before restart | **live** | commit `7e13ff8`; systemd drop-in `aiorchestrator.service.d/drain.conf` → `TimeoutStopUSec=35min` |
| recipe `aiorch-vps/03-aiorch.sh` no longer reverts the flag | done | Mac + VPS copies identical, `resume:"fresh"` for implementer/reviewer |
| C7 counter (in-app) | round 2 | tomorrow's measurement uses the challenge scripts (dedup by `message.id`) |
| CLI contract: positional slash command + stdin | **measured 2026-09-09 10:16 (Mac, claude 2.1.263, haiku) — a FOURTH outcome** | the CLI **appends stdin to the positional prompt in the same message**: the slash command's `$ARGUMENTS` became `positional-arg-1\nAdditional instruction…` (test FAILED on that assert; the model still executed both parts — `result: DONE`, 3 turns). On the VPS all 40 fresh sessions of the night carried `[bridge turn` in their first user message and **0 of 758 tool calls** had a polluted argument: the implementer/reviewer skills say "split `$ARGUMENTS` at the `/`" and the env vars carry the ids, so the models coped. **Risk stays for skills that interpolate `$ARGUMENTS` into paths verbatim** (`solo` ×6, `communicator` ×3, `supervisor` ×12 — the supervisor is stream, so unaffected: its follow-up is a separate message). The live test must be rewritten to pin the real shape; the pack's delivery channel is a stage/4b design question (positional concatenation made explicit vs a pack file the skill reads). |
| suite | green | worktree: 2 583 tests, 2 574 passed, 9 skipped, 0 failed (+8); baseline 1 red = the known intermittent |
| **C2 complete — `stage/4b` state pack** | **LIVE 2026-09-09 ~11:00 CEST** — merged `c9a36a8`, pushed, deployed by `03-aiorch.sh` (service active, DLL carries `StatePack_Builder`, plugin at `c9a36a8`, skills carry the pack sentence, resume flags untouched) | 7 commits `bd6f942..0b7dec5`: `Running/StatePack/` (Brief_Finder, StatePackInputs_Reader, StatePack_Builder, Locator, Writer); executor writes `<member>/pack.md` and passes NO stdin on fresh turns; `executed_turns.session_id`; one boot sentence in six skills; live CLI test pins the measured stdin shape; `tools/token-gate/`. CoreLib 2 595 passed / 9 skipped / 1 known intermittent (3/3 green alone); contract tests 30 passed; trial merge 0 conflicts. First real pack read: pending |
| **Round 2 first evidence — 14:50 CEST** | **the pack is read**: 7/7 fresh sessions since the deploy (rev-4 ×5, imp-5 ×2) opened `pack.md` at tool call #3–5 (after the two env-resolve Bash calls), 0 stdin in the first message, channel reads down to 0–1 per session (from 7.8 for the general before 4a); first-call context 40–53 k on short turns | **but one turn burned 16.2 M tokens in kills**: `fincanva-2/imp-5/2` — attempt 1 killed at 30 min (63 calls, 6.7 M), attempt 2 killed at 30 min (88 calls, 9.6 M), attempt 3 succeeded (40 calls, 3.4 M, 25 min). Under fresh a killed attempt loses everything: **C1.3 (closing turn on timeout) is now the next stage (`4c`)**, ahead of C3/C4. Also seen: supervisor stream "said nothing for 1 781 / 3 872 s" against a 600 s limit — the silence timer fires late; handed to the speed work (point 1c) |
| **C1.3 closing turn** | **landed via the speed work's `stage/7f`** (`be969ec`, merged `1a05159` 2026-09-09 ~15:45 CEST) — my `stage/4c` (`b87e87d`, same feature, built in parallel) is **superseded and parked**, branch kept on origin for reference, not merged | 7f: `ClosingTurn_Rule` / `ClosingTurn_Words`, `--resume <killed> --max-budget-usd 2.00`, closing timeout derived from the turn timeout, the attempt is NOT spent when the report lands, entries count as answered, every fallback logged; applies to deadline kills in both modes (fresh mints a new id afterwards). **Deployed 2026-09-09 15:10 CEST as `5e78114`** (7f + the speed work's 7e coalesce-only-for-members): service active, DLL carries `ClosingTurn_Rule` + `StatePack_Builder` + `CoalesceWindow_Policy`, plugin `5e78114`, flags untouched. Suite on `5e78114` (Mac): 2 674 passed / 9 skipped / 1 red `PrintTurnLimitResetTests.ResumeClear_…` that passes 3/3 alone — a new timing-sensitive intermittent from 7b, reported to the speed work |
| **C8 drain — actually broken; `stage/4e`** | **built + tested 21:00 CEST, pushed, not deployed** | the speed report's open item #1 blamed `TimeoutStopSec` = 90 s: wrong scope (`--user`); the system unit has 35 min. The real cut: `HostOptions.ShutdownTimeout` at its default 30 s — journal 17:24 / 17:25 / 17:29: "draining 2 in-flight turn(s)" then "Deactivated successfully" 30 s later, every time; 5 fresh sessions killed, **15.2 M tokens**, one turn restarted 4×. `ShutdownGrace_Rule` sizes drain < engine grace < host timeout (39 min); unit `TimeoutStopSec` 2400 s (the VPS drop-in `drain.conf` is still 2100 — needs root). Suite 2 754 / 0 red. Also: two deploy procedures exist (the recipe `03-aiorch.sh` reinstalls the kit; the manual `cp -a` does not) — use the recipe |
| **Models per role — live** | **2026-09-09 21:50 CEST** | app defaults (`OrchestratorConfig_Factory`) and both kit installers already carried the decision; the VPS recipe `03-aiorch.sh` overrode it with four `// "haiku"` on a fresh box — **fixed on both copies** (md5 identical). VPS `config.json`: general + communicator → sonnet, supervisor + implementer/reviewer fallback opus. `stage/4f` (`c51068b`, merged `f5d6b76`) closes the defect that made the decision unapplicable: a print-run general's model was frozen at first registration (watchdog exempts it, so `Spawn_GeneralSupervisor` never runs twice) — config said sonnet, the session ran haiku. The dispatcher now reconciles it at the start of a turn. Suite 2 756 / 0 red. Root on the VPS: `drain.conf` 2100 → **2400 s** (effective 40 min), above the host's 39 min |
| Gate check | 2026-09-09 afternoon | tokens per delivered item ≥ 3×, tool calls per item ≤ +30 %, attempts per turn ≤ 1.1, verdicts read by the owner |

## 5c. Plan of the day — 2026-09-09 (revision 2, the review session takes command)

Facts the plan rests on, re-measured 2026-09-08 by the independent review (VPS, dedupe by `requestId`; full report in the review document): 2 040 M tokens / 9 770 calls / 97.8 % cache read / sub-agents 13.1 % with 5-min cache only on sidechains; implementer turns (172): first-call context median 249 k, p90 719 k, new tokens per turn median 6 k, tokens per turn median 690 k; 1.56 pending entries per turn; general supervisor (fresh) 95 % cache read on its boot; CoreLib suite on `926cc6b`: 2 574 passed, 0 failed, 9 skipped.

1. **Do not touch the VPS tonight.** Round 1 runs as deployed (implementer + reviewer fresh, drain live: `aiorchestrator.service` `TimeoutStopUSec=35min`, DLL 22:33 with `FRESH_SESSION_PREAMBLE`, 6 fresh member turns by 22:47).
2. **After 05:00:** run `Print_SlashCommandPositional_PlusStdinInstruction_BothReachTheModel` (weekly limit reset). Record which of the three outcomes holds.
3. **Morning measurement, one number first:** calls per turn and first-call context of tonight's fresh member turns vs yesterday's (`turns.jsonl` → `num_turns`, `usage.iterations[0]`). If fresh turns still spend 9–10 calls, the preamble lost to the skill's "read your channel top to bottom" and C2 moves ahead of the gate.
4. **Afternoon: the gate** with §6 revision 2. Owner reads 10 + 10 verdicts blind.
5. **Then:** `stage/4b` = C2 complete; owner decision on C10 (autocompact net for the supervisor) and on the C4 digest window.
6. Housekeeping: this spec is the single plan; the Italian working document is history. Nothing here is committed yet (both spec files are untracked) — commit them on a `stage/*` branch when the owner says so.

## 5d. Gate 1 — first reading, 2026-09-09 09:50 CEST (11 h after deploy, 3 h of real activity)

**Verdict: INCONCLUSIVE with a warning sign — the mechanism works, the bill did not move.** Read-only measurement on the VPS (`turns.jsonl`, transcripts deduplicated by `requestId`, channel headers only). The weekly limit (429) blocked everything from 22:34 to 04:51Z — the 6 "errors" at 20:34Z are that, not the fresh mode — so the sample is 04:51→07:50Z: 40 fresh member sessions, 6 ledger lines closed (fincanva-2 22→25, fincanva-3 1→4), 6 merges into `dev`.

| metric | baseline (transcript) | round 1 (fresh) | read |
|---|---|---|---|
| first-call context, members | median 249 k (imp) / 241 k (rev) | **median 39 k** (40 sessions, transcripts) | ✅ 6× — the mechanism works |
| calls per turn, implementer | median 2, mean 8.3 | median 4, **mean 19.8** | ⚠️ 2.4× more calls per turn |
| tokens per turn, implementer | median 508 k, mean 2.86 M | median 172 k, **mean 2.04 M** | median 3× better, mean only 1.4× |
| tokens per turn, reviewer | median 444 k, mean 0.99 M | median 391 k, **mean 1.75 M** | ❌ worse on the mean (full reviews of 33–57 calls, 2.4–6.8 M each) |
| tokens per call, main sessions | 209 k | 176 k | only −16 %: the two supervisors (still `transcript`) burned **87 M of 183 M** at 382 k and **813 k** per call |
| tokens per closed ledger line `[estimate — lines uneven, n=6]` | ~26–30 M (fincanva-2+3, 23 lines) | **~30 M** (183 M / 6) | ❌ no gain |
| production work per line (Edit/Write + commits) | 14.1 | 5.4 | today's lines were cheap closes of yesterday's work — which makes the flat tokens-per-line worse, not better |
| orientation calls per line | 159 | 112 | fewer, but see calls per turn |
| channel reads by fresh sessions | general: 7.8 per turn | **45 in 40 sessions ≈ 1.1** | ✅ the preamble won; archive opened 0 times; `online` greetings filed 0 |
| retries | — | 6, all 429 weekly limit | ✅ none caused by fresh |
| review rounds per day (supervisor REVIEW briefs) | 6 / 11 | 7 (by 09:50) | not yet readable |
| verdict quality | — | 7 reviewer entries listed for the owner's blind read (rev-3 [170][178]; rev-7..11 [2]) | owner's, pending |

**Why the bill did not move, in order of weight:**
1. **The supervisors are now half the bill (48 %) and untouched** — `fee5320d` runs at 813 k per call. C4 (fewer wake-ups) and C10 (autocompact net) stop being "later": they are the next lever.
2. **Fresh turns are long turns.** A fresh implementer that gets a fix round runs 50–92 calls in one turn (imp-2/fincanva-3: 11.2 M, 12.5 M, 4.5 M) and its context climbs to ~200 k inside the turn — the boundary the stage design (C9) exists for. Fresh alone removes the carry between turns, not the growth inside one.
3. **Confounders:** a post-limit backlog rush (7–8 members active at once), 3 hours, 6 lines of a different kind than the baseline's. Re-read at 18:00 CEST with ≥ 10 lines before calling it.

**Decisions taken on this reading (owner, 10:15):** C10 and the rotation **rejected** (no shortcuts — the supervisor goes through C2 → C4 → fresh and costs what it costs meanwhile); round 1 runs unchanged until the 18:00 reading, **members-only metrics** (tokens per closed line: baseline 333 M / 23 lines ≈ 14.5 M, round 1 so far 77 M / 6 ≈ 12.8 M — flat); blind read of 7 + 7 verdicts is the owner's.

## 5e. Models per role — owner decision 2026-09-09 17:40 CEST

| role | model | why |
|---|---|---|
| supervisor | **opus** | the judgement the owner pays for; the role watched "with eyes" |
| general supervisor | **sonnet** (runs haiku today although `config.json` says opus — the model is fixed at session registration, not re-read) | routing and memory-keeping; the historic incident (duplicate orchestration) was comprehension; its bill is negligible (11 M of 2 G), so this is about correctness |
| implementer | **the supervisor chooses per brief** — default sonnet, opus for design / ambiguity / unexplained failures | C6; today the supervisor chooses at member creation (`add-implementer … model`); the bridge reading `MODEL:` from the brief is `stage/4d` |
| reviewer | **opus** by default; sonnet only for the "quick delta" re-checks the supervisor asks for | adversarial review is where the large model pays; a missed finding costs a whole round |
| solo | **opus** | supervisor and implementer in one, owner-facing |
| communicator | **sonnet** (haiku today) | restates status, decides nothing; with the translation layer gone it writes to the owner directly |

Models do not change token counts — they change list cost and, unmeasured, the weight on the weekly limit (§9). Application: config + verification on the VPS for general/communicator/solo; `stage/4d` for C6.

## 5f. Gate — second reading, 2026-09-09 21:25 CEST (members only, by call timestamp; VPS read-only, dedupe by `requestId`)

Windows (UTC): **baseline** = transcript, 6 Sep → 8 Sep 20:34 · **R1** = fresh, 20:34 → 09:00 (8 h of it blocked by the weekly limit) · **R2** = + pack, 09:00 → 13:10 · **R3** = + closing turn, 13:10 → 19:23.

| | baseline | R1 fresh | R2 +pack | R3 +closing |
|---|---|---|---|---|
| member tokens / API calls | 1 335 M / 3 865 | 77 M / 769 | 44 M / 421 | 176 M / 1 804 |
| **tokens per call, members** | **345 k** | **100 k** | **105 k** | **98 k** |
| first-call context, median | 248 k | 47 k | 53 k | 66 k |
| calls per turn, median | 2 | 5 | 9 | 10 |
| tokens per turn, median | 450 k | 214 k | 224 k | 296 k |
| deadline kills / closing turns OK | 28 timeout-retries | 13 retries | 7 retries | **10 kills / 9 closed with a report / 0 retries** |
| tokens in deadline-killed turns | — | — | — | **73 M = 41 % of R3** |
| supervisor tokens per call | 380 k | 568 k | 170 k (post-compaction) | 215 k |
| merges into `dev` | 22 | 6 | 1 | 1 |
| member tokens per merge `[estimate — n small]` | 60.7 M | **12.9 M** | 51 M (n=1) | 169 M (n=1) |

Ledger: fincanva-2 closed lines 22 → 41 across the day; fincanva-5 (opened 15:56 CEST, 3 implementers, big briefs) has delivered 0 lines so far.

**Reading.**
1. **The mechanism holds and is stable: 3.4× fewer tokens per call for members, in all three windows.** Fresh does what it promised; the pack did not add cost (100 k → 105 k → 98 k).
2. **Per turn the gain shrinks as turns lengthen: 2.1× (R1) → 1.5× (R3).** Calls per turn went 2 → 10 (median), with turns of 60–90 calls: a fresh implementer with a large brief works until the 30-minute deadline at a context that climbs to ~200 k. **41 % of R3's member tokens sit in turns that hit the deadline** (10 in 6 hours, all fincanva-5 implementers, every turn).
3. **The closing turn works: 9 of 10 kills ended with a report, 0 timeout-retries** (R1/R2 had 13 and 7). It saved the state, not the tokens — the next turn starts from the report and runs another 30 minutes.
4. **Per delivery the evidence splits:** R1 — 6 merges at 12.9 M each, **4.7× better** than baseline (60.7 M), on short closing-round tasks; R2/R3 — one merge each, unusable, while fincanva-5 burns on long tasks not yet delivered. Not a pass, not a fail: **the dominant cost has moved from carried context to intra-turn growth of long turns.**
5. **Restarts cost 15.2 M in 6 minutes** (17:24–17:30: the .NET host cut the drain at 30 s — `stage/4e`).

**Consequences for the plan (revision 3):**
- **C9 (budget per stage) moves from last to next** — as a soft boundary inside a fresh turn ("you are at N calls / T tokens: reach a stable point, commit, report") with the closing turn already there as the hard net. The disease is now the 60–90-call turn, and only a boundary inside the turn treats it.
- The supervisor's brief size is the other half (a stage is a commit-sized step): protocol, owner's call — P9 was declined; noted, not proposed.
- Supervisor fresh (step 5) stays after C4; its per-call cost fell to ~200 k only because the CLI compacted it on its own.

## 5g. Schedule — owner asked for dates, 2026-09-09 21:45 CEST

| when | step | why here | size |
|---|---|---|---|
| **10 Sep, morning** | **C9 soft boundary inside the turn** — near call ~40 a hook advises "reach a stable point, commit, report"; the 30-min deadline stays the hard net (closing turn) | 41 % of tonight's member tokens sit in turns that ran to the deadline; nothing else treats a 60–90-call turn. Kit + one hook, no C# | 2–3 h |
| **10 Sep, afternoon** | **Gate 3** on a full day (≥ 10 delivered lines) + the owner's blind read of verdicts | first gate with enough deliveries to mean something | 1 h |
| **10 Sep, after the gate** | **C4 wake-up digest** — owner messages immediate, member entries collected at stage end or every 5 min, mechanical acknowledgements handled by the app | the supervisor is half the bill and this does not touch its memory: no continuity risk | half a day |
| **11 Sep, morning** | **C6 `MODEL:` in the brief** + split the reviewer's model key from the implementer's (they share one today) | the brief is where the supervisor decides; with fewer wake-ups the briefs are better | 2–3 h |
| **11 Sep, afternoon** | **Supervisor fresh with the pack** (built and inert since `stage/4b`), owner reads verdicts before/after | last because the risk here is judgement, and it only pays after C4 | 2 h + watching |
| **12 Sep / when there is time** | **C3** app bookkeeping out of the channel · **C7** counter inside the app · **`STATE:`** only if a gate shows rework | none of the three moves the bill alone; C7 is what lets the owner read the numbers without this session | ~1 day |

Two conditions that reorder this: a gate showing **rework** (briefs re-issued, REWORK verdicts) promotes `STATE:` to right after C9; a gate showing the long turns gone makes C4 the largest remaining piece and everything else slides.

## 6. Verification — two columns, always

**Tokens** (deduplicated transcripts, sidechains included): mean context per call from 209 k to < 70 k; tokens in calls above 200 k from 76 % to ~0; interrupted-work share from 19–27 % to < 5 %; registry ↔ transcripts within 5 %.

**Delivered work — three signals, never one.** A delivered item is a ledger line closed with a reviewer verdict **and** a merged commit **and** traceable to an `OWNER REQUESTS` row (decision 22); the set of lines is fixed when the experiment starts, so a supervisor cannot move the bar by splitting lines (fincanva-2 closed 22 lines in 329 turns, fincanva-1 5 in 204 — line granularity already varies 3×). Minimum **10 items per arm**.
- **Production calls** (`Edit`, `Write`, commits) per item within ±15 % — the work that must not shrink;
- **Orientation calls** (`Read`, `Grep`, read-only `Bash`) per item **reported without a threshold** — a fresh session re-reads by construction, and a rise here is the expected price, not rework;
- **Rework** ≤ +10 %: briefs re-issued for the same line, `REWORK`/`REJECT` verdicts per line, `fix`/`fixup` commits after a verdict.
Also: attempts per turn ≤ 1.1; kills < 2 % of tokens; stages per delivered item ≤ 1.5× today's turns. "Tool calls per item within +30 %" (revision 1) is dropped: it cannot tell re-orientation from rework.

**And one thing no metric sees:** the quality of the supervisor's verdicts and the reviewer's findings. Read by the owner **blind**: 10 verdicts per arm, shuffled, unlabelled; if the owner tells the arms apart better than chance, the fresh arm is shallower — stop. "Do not degrade" without a blind read is an impression, not a criterion.

The audit scripts must not be reused as they are: they double-count. Ratios would survive, absolutes would not.

---

## 7. Risks

| risk | where it bites | defence | residual |
|---|---|---|---|
| Hot understanding lost at a stage boundary → rework | debugging and design tasks, implementer | `STATE:` dead-ends section; closing on a verifiable state; one resume after a kill (C1.3); step 1 measures rework before anything is built | not zero — this is the price |
| Supervisor judgement thins without its transcript | supervisor verdicts, owner questions | `PLAN.md` + `state.md` + digested channel as memory; supervisor goes fresh **last** (step 4), owner reads verdicts | medium — the one risk to watch with eyes |
| Digest delays a member's answer | latency, not quality | owner messages immediate; digest ≤ 5 min or stage end | low |
| Sonnet on a task that needed opus | implementer quality | the supervisor chooses per task; reviewer catches; opus default for ambiguity | low-medium |
| Short thinking — an agent that knows it may be stopped takes the smaller decision | all writers | generous ceiling (80 % of today's turns whole), free continuation, stopping never rewarded; signal: stages per delivered item | medium |
| State pack too large or stale | boot cost, wrong start | `state.md` ≤ 8 KB, `PLAN.md` ≤ 20 KB, channel compaction per stage | low |
| `PreToolUse` cannot inject advice | C9 soft boundary | advice at the next stage; `--max-budget-usd` + closing turn still hold | low |

---

## 8. Open decisions for the owner

1. Mac on the same account: separate, or accept that the weekly limit is not measurable from the VPS alone.
2. ~~Which orchestration is the step-1 guinea pig~~ — decided: both open ones (flag per role).
5. ~~C10 autocompact / rotation~~ — **decided 2026-09-09: no.** No shortcuts; the supervisor takes the full road (C2 → C4 → fresh). The 48 % it costs meanwhile is accepted.
6. C4 digest window default (5 min proposed) — confirm before step 3.
3. Digest window default (5 min proposed).
4. Sonnet as the implementer default in the supervisor protocol, or opus with sonnet opt-in.

## 9. Not verified

- Whether `PreToolUse` hooks can inject context in 2.1.263 (C9 advice path).
- Whether the weekly limit weights cache reads like fresh input.
- List-price ratio opus/sonnet (~1.7× from memory).
- ~~Whether the `environment` dictionary is applied after the `CLAUDE_CODE_*` scrub~~ — **verified 2026-09-08** (independent review, branch source `926cc6b`): scrub at `PrintTurnRunnerModel.cs:116` and `StreamSessionProcess.cs:118`, `environment` applied at `:120` / `:122`.
- Whether `claude -p <positional slash command>` also delivers a stdin prompt in the same process (the live test is pending; the preamble is safe under all three outcomes).
- The exact semantics of `usage.iterations[0]` and `num_turns` in the CLI result (inferred from magnitudes: `iterations[0]` ≈ the turn's first call; `num_turns` overcounts API calls by ~1.5–2×).
- The split of a member's 34–47 k boot between Claude Code's system prompt, tool definitions, MCP and the skill (inferred by differences, not measured on an empty `-p`).
- The content of the heaviest turns — measured how much they weighed, not what they did.

*Nothing here is certified by its author. What proves it is the before/after of §6, with the C7 counter already running.*
