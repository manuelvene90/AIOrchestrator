# Porting the fork onto master — execution plan

> **For agentic workers:** REQUIRED SUB-SKILL: superpowers:subagent-driven-development.
> Steps use checkbox (`- [ ]`) syntax.

**Goal:** produce a branch that is `upstream/master` with the fork's 49 unmerged commits
applied, the three conflict blocks resolved by hand, building clean and with the full suite
green — so the upstream author can take it in one step.

**Architecture:** a real `git merge` of `ours/integration` into a branch cut from
`upstream/master`, not a replay. Git auto-merges everything except three blocks, all in the
owner-questions domain; those three are resolved by hand against the rulings in the spec.

**Tech Stack:** .NET 10.0.401 (`~/.dotnet`), xUnit. `AIOrchestratorCoreLib` and its tests are
`net10.0` and build on macOS; the WPF app is `net10.0-windows` and does not.

**Spec:** `docs/superpowers/specs/2026-09-14-porting-the-fork-upstream-design.md`

**Worktree:** `/Users/nvene/Visual Studio/AIOrchestrator-port`, branch
`integration/fork-to-master`, cut from `upstream/master` `19f1a1c`, merge already in progress.

## Global Constraints

- **No push to `upstream`.** Write access is denied (verified: HTTP 403). The branch stays on
  `origin` and the handover is the fork owner's.
- **Master's rule stands on contested ground.** The one-question-at-a-time hold is adopted; the
  fork's guards are added *behind* it, never as replacements (spec §3.1).
- **No sub-agent runs a git write** — no `add`, `commit`, `merge`, `stash`, branch operations.
  Sub-agents edit files; the main session does all git.
- **A sub-agent's report is a claim.** The main session reads the real diff and runs the suite
  itself before calling anything done.
- Baseline to beat, measured on the fork today: `Failed: 0, Passed: 3572, Skipped: 9`. The nine
  skips are compared **by name**, never by count.

---

### Task 1: Resolve `BridgeEngineModel.cs` — both conflict blocks

**Files:** Modify `AIOrchestratorCoreLib/Bridge/BridgeEngine/BridgeEngineModel.cs`
(conflict blocks at lines 4447–4500 and 12915–12996 as the merge left them).

**Interfaces:**
- Consumes: `Find_RepeatedQuestion_OrNull`, `Find_ClosedRepeat_OrNull`, `Note_ReaskWithheld`,
  `Handle_RepeatedQuestion`, `Handle_ReaskOfADecidedQuestion` — all from the fork side.
- Produces: a file with zero conflict markers that compiles.

- [ ] **Step 1: Block one (≈4447) — keep master's comment AND the fork's two guards.**

Master's side is *only* a comment explaining that the second-open-question coaching is gone
because the hold makes it false. The fork's side is real code: the repeated-question guard and
the already-decided guard. Both belong: master's comment states why the coaching went, and the
guards cover what the hold does not (terminal presence raises no flag; the hold only delays, so
a verbatim re-ask is delivered seconds after the owner answers).

Resolution: keep master's comment block verbatim, then the fork's two guards verbatim, and
**drop the fork's trailing comment line** `// A SECOND OPEN QUESTION IS COACHED, NOT REFUSED —
and the refusal was tried first.` — it introduces a block that no longer exists.

- [ ] **Step 2: Block two (≈12915) — take the fork's helpers, minus one dead method.**

Master's side of this block is empty; the fork's side is the helper methods the guards call.
Take the fork's side, **except `Would_BeASecondOpenQuestion`**, which was the coaching block's
helper: after Step 1 nothing calls it. Verify before deleting:

```bash
grep -n 'Would_BeASecondOpenQuestion' AIOrchestratorCoreLib/Bridge/BridgeEngine/BridgeEngineModel.cs
```
Expected after resolution: no matches.

- [ ] **Step 3: Verify no markers remain**

```bash
grep -c '^<<<<<<<\|^=======\|^>>>>>>>' AIOrchestratorCoreLib/Bridge/BridgeEngine/BridgeEngineModel.cs
```
Expected: `0`

---

### Task 2: Resolve `QuestionContractProbeTests.cs`

**Files:** Modify `AIOrchestratorCoreLib.Tests/Bridge/QuestionContractProbeTests.cs`
(conflict block at lines 529–785).

**Interfaces:**
- Consumes: master's helper `Reach_TwoQuestionsOpen_Async`.
- Produces: a test file with zero markers where every probe compiles.

- [ ] **Step 1: Master's helper wins; the fork's probe bodies are ported onto it.**

Master deleted the fork's setup — appending both questions side by side before the engine
runs — because it "manufactured a state the app deliberately no longer produces". Under the
hold that is correct. Master's `Reach_TwoQuestionsOpen_Async` reaches the same state through
the app's own ten-minute cap.

Resolution: keep master's helper and its documentation; keep the fork's six new probes, but any
that needs two questions open must call master's helper instead of appending both directly.
Probes that need only one open question keep their own setup.

- [ ] **Step 2: Verify no markers remain**

```bash
grep -c '^<<<<<<<\|^=======\|^>>>>>>>' AIOrchestratorCoreLib.Tests/Bridge/QuestionContractProbeTests.cs
```
Expected: `0`

---

### Task 3: Build and run the full suite on the merged tree

**Files:** none modified.

- [ ] **Step 1: Build**

```bash
export PATH="$HOME/.dotnet:$PATH" DOTNET_ROOT="$HOME/.dotnet"
dotnet build AIOrchestratorCoreLib.Tests/AIOrchestratorCoreLib.Tests.csproj -c Debug
```
Expected: `0 Error(s)`.

- [ ] **Step 2: Suite**

```bash
dotnet test AIOrchestratorCoreLib.Tests/AIOrchestratorCoreLib.Tests.csproj -c Debug --no-build
```
Expected: `Failed: 0`. The skip list is compared by name against the nine known skips; a tenth
skip is a regression, not a pass.

- [ ] **Step 3: Commit the merge** (main session only)

---

### Task 4: The residues the spec names

**Files:** Modify `kit/skills/supervisor/SKILL.md`.

- [ ] **Step 1:** the example `'deep adversarial review of the order-sizing rewrite,
  /code-review xhigh'` uses `deep`, a level name the table no longer contains. Replace with a
  level the table does contain.
- [ ] **Step 2:** re-run Task 3's suite — `RoleCommandMarkerTests` and
  `TheSkillsQuoteTheAppsOwnNumbersTests` read these files.

---

## Deliberately NOT in this plan

The new catalogue keys (spec §7.2) and the `quiet.json` lines. They are additive work in
master's own files, they depend on plan 03 landing for several of them, and they are better
proposed than imposed. The spec carries them; the branch does not.
