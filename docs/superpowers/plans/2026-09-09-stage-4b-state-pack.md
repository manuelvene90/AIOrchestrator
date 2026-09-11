# stage/4b — the state pack: the bridge hands a fresh session its memory in a file

**Date:** 2026-09-09 · **Branch:** `stage/4b-state-pack` (worktree `../AIOrchestrator-stage4b`, from `ours/integration` `926cc6b`) · **Spec:** `docs/superpowers/specs/2026-09-08-token-efficiency-design.md` C2 (revision 2) · **Owner decision:** "file, not stdin" (2026-09-09 10:40).

## Why

Round 1 (`resume: fresh` for implementer and reviewer) cut the first-call context 6× (249 k → 39 k) but not the tokens per delivered line (14.5 M → 12.8 M, flat), because a fresh session spends its turn re-orienting: 2.4× the calls of a resumed one. The 2026-09-08 review measured that the bridge already holds what the session goes looking for — the brief, the member's last report, the ledger lines, the code state — and that the live CLI test of 2026-09-09 showed stdin is **appended into the slash command's `$ARGUMENTS`**, so the 4a preamble pollutes the argument every skill parses (solo and communicator interpolate it into paths verbatim).

## Done when

- `dotnet test AIOrchestratorCoreLib.Tests` on the branch: same set of names green as `926cc6b` (2 574 passed, 9 skipped, 0 failed) plus the new tests, zero new reds.
- With the FakeClaude harness: a fresh member turn (a) passes **no stdin**, (b) leaves `<orch>/<member>/pack.md` containing the pending entries verbatim, the brief, the member's last own entry, the git state and its ledger lines, (c) a resumed (transcript) turn is byte-identical to today's.
- Every print role's skill boot sequence carries the sentence "if `pack.md` exists, read it first" (one sentence added per skill, no rule rewritten; `RoleCommandMarkerTests` and `NoProtocolFileIsOrphanedTests` stay green).
- `print-session.json → executed_turns[]` carries `session_id`; old files without it still load.
- The live test `Print_SlashCommandPositional_PlusStdinInstruction_BothReachTheModel` pins the measured shape (stdin appended to `$ARGUMENTS`) instead of the imagined one.
- Trial merge into `ours/integration` clean.

## Design

`Running/StatePack/` — one folder, the CoreLib triple convention where state exists, static builders where it does not:

| type | role |
|---|---|
| `StatePack_Locator` (static) | `Get_File(paths, role, orchId, memberId)`: members `<orch>/<member>/pack.md`; supervisor `<orch>/.supervisor.pack.md`; general `general/pack.md` |
| `StatePackInputs` (immutable data) | orch/member/role, request id, pending entries (raw text, per source), brief (entry or null), last own entry (or null), git lines, ledger lines, owner tail (supervisor only), plan text (supervisor only), sources (for the contract) |
| `StatePackInputs_Reader` (static) | reads live+archive history via `ChannelHistory_Counter.Read_AllEntries`, the plan via `paths.Get_PlanFile`, git via `GitSnapshot_Reader.Read_RepoAndWorktrees`; every read swallows into "section unavailable: <why>" — a pack must never stop a turn |
| `Brief_Finder` (static, pure) | latest supervisor entry whose subject starts with a task marker (`BRIEF`, `REVIEW`, `FINAL REVIEW`, `RE-REVIEW`, `FINDINGS`, `TASK`, `GO AHEAD`); fallback: the longest supervisor entry among the last 20; null when none |
| `StatePack_Builder` (static, pure) | inputs → markdown text; fixed section order (stable first: identity, contract; then brief, last own entry, ledger, git; volatile last: pending entries); per-section truncation with a visible marker; whole-pack cap 24 KB |
| `StatePack_Writer` (static) | `Atomic_FileWriter.Write_AllText` — replaced every fresh turn, never appended |

Hook: `PrintTurnExecutorModel.Execute_Async` — when `roleConfig.Resume == Fresh && !resumeTranscript`: build + write the pack, **prompt = null** (the role command alone is the positional prompt; nothing on stdin, so `$ARGUMENTS` is exactly `<orch>/<member>`). `Build_FreshSession` and its preamble stay for one release as dead code with a note, then go. The stream executor is untouched (its follow-up is a separate message; the supervisor pack is built but inert until step 5).

Kit: one added sentence at boot step 1 of implementer, reviewer, solo, communicator, general-supervisor (and the supervisor, for step 5): *"If `<folder>/pack.md` exists, the bridge started you FRESH: read it first — it carries your brief, your last report, the code state, your ledger lines and the entries that woke you. Read the channel only for a fact the pack lacks."*

Attribution: `IExecutedTurn.SessionId` (nullable on read for old files), written by the dispatcher from `result.SessionId ?? sessionId`.

## Steps (TDD, one commit each)

1. `Brief_Finder` + tests (marker hit, fallback longest, none, archive+live).
2. `StatePack_Builder` + tests (section order, truncation marker, multi-source contract, supervisor variant with plan + owner tail).
3. `StatePackInputs_Reader` + `StatePack_Locator` + `StatePack_Writer` + tests (missing files → "unavailable", archive read, git failure swallowed).
4. Executor hook + harness test (fresh: no stdin, pack present; transcript: unchanged).
5. `executed_turns.session_id` + store round-trip test (old file loads).
6. Kit sentence ×6 + kit tests green.
7. Live test rewrite (pins the measured shape).
8. Full suite, trial merge, report with commands and outputs.

## Not in this stage

C3 (bookkeeping out of the channel) → `stage/4c`; C4 (supervisor wake-up digest) → `stage/4d`; `STATE:` block → step 4 of the spec, only if the 18:00 gate shows rework.
