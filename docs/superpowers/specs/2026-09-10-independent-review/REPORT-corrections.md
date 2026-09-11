# Corrections to REPORT.md — token frame, checked against sources on 2026-09-10

Written by a third session that read REPORT.md, its four `scripts/*.py`, `tools/token-gate/*.py`,
`kit/hooks/soft-boundary-check.sh` and `StatePack_Builder.cs` (branch `ours/integration`), and
verified every external claim against the live source. **No VPS access, no message text, no test
run.** Owner's decision after the first draft: **the objective is fewer tokens, not fewer dollars.**
Dollar figures stay in the report as the owner's own lens only; nothing below is ranked by price,
and levers that only change price are removed.

Tags as in the report: [VERIFIED-SOURCE] = I read the cited page/file; [DOCUMENTED] = Anthropic
docs, quoted; [INFERRED] = my reasoning; [estimate] = not measured.

---

## 0. The unit — replace §2.2(b) with this

The report's objection to the headline ("cache reads at full weight") is right, but the fix it
gives is a price. In a token frame the fix is to **split the classes, not to weight them**:

| Class | Fields | What it is |
|---|---|---|
| **new** | `input_tokens` + `cache_creation_input_tokens` | text the model processes from scratch this call |
| **re-read** | `cache_read_input_tokens` | context identical to the previous request, passed again (97 % of the raw sum) |
| **output** | `output_tokens` | answer + thinking |

Two metrics, both model-independent, both already computable from the scripts:

- **Context per model call** = new + re-read. This is the report's own `ctx tokens/call` (§2.3:
  391 k → 95 k, **−76 %**; §2.4: growing → flat). It becomes the headline. The day totals in §2.1
  are retired, as the report itself argues.
- **Output per turn**, from `turns.jsonl` `usage.output_tokens` (`gate_turns.py` already reads it as
  `out`). The report never quotes it; it is the only number the effort lever moves.

A cache miss does not change context per call — it moves tokens from *re-read* to *new*, i.e. the
same context processed from scratch. So a third, derived number: **new tokens on the first call of
a Fresh turn**, which is the cold-start cost the state-pack design pays once per turn and the only
place where cache behaviour shows up as tokens.

All three carry the report's caveat: level is task-mix-confounded, shape and direction are not.

---

## 1. Findings — what changes

### F3 — sub-agents: right bucket, wrong questions. [VERIFIED-SOURCE + DOCUMENTED]

Keep the numbers (2,197 calls on 09-10, context/call +42 %). Drop "governed by nothing" and the
dollar comparison. In token terms the three questions are: how many spawns per turn, how much
context each loads cold, how much comes back to the parent. Facts to add:

- A non-fork sub-agent starts with its own system prompt, CLAUDE.md, git snapshot and task; "each
  subagent starts with a fresh, isolated context window" (code.claude.com/docs/en/sub-agents).
  Issue #74318 measures ~37 k tokens per spawn, ~97 % identical across same-type sub-agents.
- The brake that exists and is not passed: `claude -p --max-turns <n>` ("Limit the number of
  agentic turns, print mode only", code.claude.com/docs/en/cli-reference). `--max-budget-usd`
  also exists and counts sub-agent spend (v2.1.217+), but it is dollar-denominated — the owner's
  lens, not the objective. Either one is "the app enforces at the point of effect" (decision 21).
- Sub-agent tokens are governed by the parent's prompt and the sub-agent's return contract, not by
  the hook. Anthropic's own pattern: sub-agents "explore extensively but return condensed summaries
  (typically 1,000–2,000 tokens)" (anthropic.com/engineering/effective-context-engineering-for-ai-agents).

Model routing of sub-agents (`CLAUDE_CODE_SUBAGENT_MODEL`) is **removed** from the analysis: it
changes price, not tokens, and a smaller model may take more turns.

### F6 — "no cache-aware logic" is true and beside the point. [DOCUMENTED + INFERRED]

The orchestrator never calls the API; Claude Code places every `cache_control` marker and orders
requests "system prompt → project context → conversation" itself (code.claude.com/docs/en/
prompt-caching). A `grep` in C# was always going to return 0. What the orchestrator controls, in
tokens:

1. **Order of what it injects** — the pack already puts stable material first, trigger last
   (`StatePack_Builder.cs:18-21`). That is the ProjectDiscovery change (dynamic content moved from
   the system prompt to a tail user message: hit rate 7 % → 84 %). Credit the pack for it.
2. **Cold start of a Fresh turn.** "Sessions you run in parallel in the same directory build
   matching prefixes and read each other's cache. Sequential sessions share the prefix only when the
   git status snapshot at startup matches" (same page → "Cache scope"). So whether a Fresh turn's
   first call re-processes system prompt + CLAUDE.md from scratch depends on directory and git
   state — after every commit it does. **Measurable now**: *new* tokens on the first call of each
   fresh implementer turn (§0, third metric). Add to P3.
3. **TTL** (`promptCacheTtl` / `subagentPromptCacheTtl`, v2.1.242+) decides whether a turn-to-turn
   gap turns re-read into new. Set from the measured gap, not by default: #74318 measures a blanket
   1 h on sub-agents as **worse** (+8.6 %), because the win there is placement, not lifetime.

### F7 — documented as append-only; downgrade from "risk" to "check". [DOCUMENTED + VERIFIED-SOURCE]

Claude Code "appends system context mid-conversation, such as file-change notices, and marks that
block for caching"; skills and plan mode are cache-safe because they "append their instructions as
conversation messages, so the cached prefix stays intact" (prompt-caching page). The hook's own
header records the advisory as a `hook_additional_context` attachment rendered as
`<system-reminder>` after the tool call — conversation layer, at the tail. [INFERRED, high
confidence] It cannot sit ahead of stable content. Token effect: the advisory's own tokens, once.
The check is one number on data already on disk: *new* tokens on the call after the advisory
should not jump. Keep P1 at that size.

### F5 — keep, add two columns to the rebuild. 

Beyond the four defects listed: the tool must print the three classes of §0 separately (it sums
them), and the per-turn first-call *new* count. The `cache_creation.{ephemeral_5m,ephemeral_1h}`
split exists in the transcripts (checked locally on 2.1.267, the VPS version) — useful to the
owner's dollar lens, not to the token metric; print it, don't rank by it.

### §5 "levers this design does not have" — one of them the runtime already has. [DOCUMENTED]

Tool-result clearing is done by Claude Code itself (`/usage` counts "expected rebuild (compaction
or tool-result clearing)", code.claude.com/docs/en/costs). The Anthropic quote ("one of the safest,
lightest-touch forms of compaction") is verbatim; keep it, but the missing lever is a **tool-output
cap of the design's own**: `bashOutputMaxChars`, and PreToolUse hooks that filter output before
Claude sees it — the costs page's own example takes a test run "from tens of thousands of tokens
to hundreds". Claude Code's default cap on tool responses is 25,000 tokens (writing-tools-for-agents,
verbatim); the "~3×" is that article's single example (206 → 72 tokens), say so.

---

## 2. Literature — three citations do not say what the report says

| Report says | Source says | Change |
|---|---|---|
| "Summarisation raises confidence while lowering accuracy, discarding rejected approaches and implicit constraints (ACON, arXiv:2510.00615)" | ACON is the opposite: learned compression cuts peak tokens **26–54 %** with accuracy preserved on large models (AppWorld 56.0 → 56.5 %) and improved on small ones. Its warning is narrower — "over-compression may remove subtle facts needed for final decisions" — and it lists what a compressed context must keep: "causal relations, evolving states, preconditions, task-relevant decision cues". No confidence claim. [VERIFIED-SOURCE] | ACON is **evidence for** the digest done carefully and gives its checklist. The confidence/accuracy sentence needs another source or [UNVERIFIED]. |
| "Frozen task suite under both configurations (as in arXiv:2509.23586)" | 2509.23586 is **AgentDiet** — trajectory reduction removing *useless, redundant, expired* content inside a run: input tokens **−39.9 % to −59.7 %**, task success −1.0/+2.0 %, SWE-bench Verified + Multi-SWE-bench, Claude 4 Sonnet. [VERIFIED-SOURCE] | It is the strongest token lever in the report's own bibliography and it is filed as a method. Move to the levers paragraph; it is what the pack does per turn, applied inside a turn. Cite the fixed-suite design generically. |
| "Zep's audit of Mem0 found full-context (~73 %) beating graph memory (~68 %)" | The 73 / 68 are **Mem0's own paper's** numbers, highlighted by Zep; the vendors dispute each other's LoCoMo scores. [VERIFIED-SOURCE] | Attribute correctly; keep "contested". |

Confirmed as cited: MAST (1,642 traces, κ=0.88, FM-1.4 2.80 %, FM-2.4 0.85 %); arXiv:2606.14589
("fail-plausible"); LongMemEval scores abstention; Cost-of-Pass; Chroma context-rot (18 models);
Anthropic context-engineering post; ProjectDiscovery 7 % → 84 %; issue #74318 (1,777 sub-agents,
16 % of static prefix from cache, ~9-minute median parent gap, open, no maintainer reply — **add**
its "+8.6 % worse for blanket 1 h" row).

---

## 3. Proposal — §6 rewritten for tokens

Ranked by tokens at stake × evidence. Done-when uses the §0 metrics on a frozen task suite.

- **P1 — advisory check.** One number (F7). Ten minutes.
- **P2 — sub-agent discipline (replaces "make sub-agents cache-correctly").** (i) `--max-turns` per
  turn, per role, in `PrintTurnCommand_Builder.cs`; (ii) a return contract for sub-agents (one
  summary, ≤ 2 k tokens) in the role prompts; (iii) measure spawns/turn and context/spawn before
  and after. Done when: sub-agent context per call and spawns per turn fall on the same suite.
- **P3 — rebuild `tools/token-gate/`** with the three classes, first-call *new* per Fresh turn,
  real role map, one timestamp format, no magic divisors. Done when it reproduces §2.3/§2.4.
- **P4 — ledger-close event** for tokens per delivered item. Unchanged.
- **P5 — pack global ceiling.** Unchanged.
- **P6 — effort per role at launch (new).** Thinking is output; default effort is `high` on every
  model (code.claude.com/docs/en/model-config). `--effort` is a launch flag the command builder
  controls, cache-safe for Fresh members (a level is set once per session). Caveat: parallel members
  sharing a prefix in one directory should share a level. Done when: output per turn falls with
  task success flat on the suite.
- **P7 — tool-output cap (new).** `bashOutputMaxChars` + output-filtering hooks for the noisiest
  commands (tests, builds). Done when: context per call in long turns falls.
- **Later — trajectory reduction** (AgentDiet): drop useless/redundant/expired tool results inside a
  turn. Biggest published effect, needs its own design.

Removed from the proposal: sub-agent model routing; TTL pricing. Both are price only.

---

## 4. Where these corrections are weakest

1. Nothing here was re-measured on the VPS; every "measurable now" is a script change, not a number.
2. F7's append-only placement is documented for Claude Code's own mid-turn context and evidenced by
   the hook's transcript probe; no docs sentence names `additionalContext` placement explicitly.
3. P6 and P7 are levers with documented mechanisms and no measurement on this system.
