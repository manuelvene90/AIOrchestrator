# One Wake Model — Plan 06 report: the supervisor goes fresh

**Plan:** `docs/superpowers/plans/2026-09-15-one-wake-model-06-supervisor-goes-fresh.md`
**Branch:** `feat/supervisor-fresh`, worktree `../AIOrchestrator-fresh-sup`, based on `ours/integration` at `c3b53fc`.

**Status: Tasks 1–7 implemented. Task 8 (the flip) NOT DONE and not startable** — all three
preconditions are unmet (see below), there is no running app on this machine and no supervision data
to flip. Tasks 9 and 10 follow the flip and are therefore also open.

---

## 1. The figures

### Before — `context_share.py`, 2026-09-15

| | |
|---|---|
| machine | the owner's Mac (`darwin 25.6.0`), **not the VPS** |
| source | `~/.claude/projects` — every Claude Code session on this box |
| window | whatever those transcripts span; not bounded by this run |
| attribution | **one `unattributed` bucket** — this repo keeps no session→role map, so `--role-of` had nothing to map |

```
== unattributed ==
  calls                    166846
  mean_input_tokens        225600
  mean_inherited_tokens    220127
  inherited_share          0.9757
```

**How to read this, and how not to.** `[measured]` 97.6 % of the context handed to a model call on
this machine had already been handed to it on an earlier turn, across 166 846 deduplicated calls.
That is **not** the spec's 91 %, because it is **not the same population**: the spec's figure is
supervisor sessions on the VPS, and this is every session on a laptop, interactive ones included.
It corroborates the *shape* of the claim — the inherited half dominates, by a lot — and it replaces
nothing.

**The spec's 91 % therefore remains `[quoted]`.** Re-deriving it needs the script run on the VPS,
which needs credentials this plan does not have (one-wake-model spec, open question 1). Until then the
acceptance gate of Task 9 compares like with like or not at all (plan open question 5).

### After

Not measured — the flip has not happened.

---

## 2. Preconditions — checked 2026-09-15, none met

| # | precondition | status |
|---|---|---|
| P1 | plan 01 shipped, `runners.<role>.wake = ticket` live | **NOT MET** — plan 01 is written, not shipped on this branch |
| P2 | the pack proven on both runners ≥ 3 days | **NOT MET**, and not measurable until P1 |
| P3 | the C1.2 `STATE:` block shipped (`<member>/state.md`) | **NOT MET** — `grep -rn "state\.md" AIOrchestratorCoreLib kit` returns nothing |

`bash tools/fresh-supervisor/preflight.sh <orch-id>` on this machine exits **2** — there is no
supervision data here (`~/.claude/supervision` holds `config.json`, `secrets.json`, `statusline.sh`,
and `"repos": []`). That is the correct answer: it could not check, and it says so rather than passing.

---

## 3. The blind read

Not run. It follows the flip, and it is the owner's — this plan delivers it as a one-step pass and stops.

---

## 4. Which copies were read

Every claim in the plan and in this report about code or kit content was read from the **branch
source**, `feat/supervisor-fresh` at `c3b53fc` (2026-09-15). Specifically **not** read:

- **the running app** — there is none on this machine (decision 23's fourth copy does not exist here);
- **the installed kit** — `~/.claude/plugins/cache/aiorch-local/…`, whose marketplace still registers a
  deleted directory. So the `kit/skills/supervisor/SKILL.md` edit in Task 5 is a **branch-source**
  change and is NOT yet in any session's hands (decisions 17, 18). Task 8 Step 3 is where that is
  verified, against a restarted session.

---

## 5. The reds

**None.** Full suite, 2026-09-15, `dotnet test AIOrchestratorCoreLib.Tests/AIOrchestratorCoreLib.Tests.csproj`
(dotnet 10.0.401 at `~/.dotnet`):

```
Failed: 0, Passed: 3991, Skipped: 10, Total: 4001, Duration: 3 m 1 s
```

Baseline before this work was **3 955 passed / 10 skipped / 0 failed** `[documented — the brief's
figure]`, so this series adds **36 tests and no red**. The three known-flaky families
(`ClosingTurnReviewFix…`, `TolerantFileReader…`, `EffortDial…`) were green in the full run and needed
no isolated re-run.

---

## 6. What was done

**Tasks 1–7 implemented, one commit each, each red-first.** Task 8 is the flip and is not startable
(§2). Tasks 9–10 follow it.

| task | commit | what it closed | evidence |
|---|---|---|---|
| 1 | `16e0875` | the 91 % was quoted, never re-derived — `context_share.py` reads it, deduping by `message.id` across all files, `cache_creation` counted as new | 6 pytest cases; red 6/6 first |
| 2 | `7d2b339` | the three preconditions become one command that exits **2** when it cannot check | self-test PASS; **4 sabotages each caught** |
| 3 | `2dd627a` | **`StreamTurnExecutorModel` wrote no pack at all** — the supervisor's own transport | red 1/2, then 2/2; 98 existing oracles green, unedited |
| 4 | `99dba94` | a fresh stream turn paid for its entries twice — file and stdin | red 1/4, then green; 50 oracles |
| 5 | `02f5843` | **the supervisor had nowhere to write a conclusion** — `Get_ConclusionsFile_OrNull`, the pack section, the skill paragraph | compile-red, then 8/8; 45 pack oracles green, unedited |
| 6 | `c019faf` | the app refuses to make a supervisor or solo fresh while its conclusions are empty | red, then 8/8; 104 oracles; `~General` 177 green |
| 7 | `2486417` | the transcript-era verdicts are captured **before** the era ends, archive included | self-test PASS; **3 sabotages each caught** |

### The three findings that changed the work

1. **`StreamTurnExecutorModel` contained zero references to `StatePack`.** The supervisor runs on
   `stream` by design. So `resume: fresh` on the supervisor — the entire content of spec step 6 —
   would have handed it a role command and its pending entries and nothing else. The spec gates step
   6 on "the pack proven on both runners" and assumes both runners have one. Closed by Task 3.
2. **`StatePack_Locator.Get_ProgressFile_OrNull` returns null for `SessionRoles.Supervisor`.** It has
   no member folder, so it had nowhere to put a conclusion. Closed by Task 5.
3. **`STATE:` is already taken** — `DeclaredState_Parser` reads it as one line capped at 120
   characters for the PULSE row. The C1.2 block is five keys and ≤ 2 KB. **Not decided here**: plan
   open question 1.

### Two defects found by the tests, in this work

- **Task 3:** the pack was first gated on `resumeTranscript` rather than on the resume MODE. The two
  differ on the first turn of a transcript-mode session — it has no transcript to resume yet — so a
  session that had not asked for a pack was handed one. Caught by the second case, which exists to
  stop the fix over-reaching.
- **Task 6:** the one-shot notice was keyed on `"<orchId>/<memberId>"`, so two orchestrations sharing
  a logical id under different supervision roots shared a key. Not hypothetical — it is every test in
  the class, and the first case to run spent the key for all the others. Keyed on the channel file.

### And one about a harness, which is the reason both shell tools carry a `--self-test`

The preflight's first self-test **passed with its own P3 check deleted.** The empty-conclusions case
asserted only `rc = 1` while the pack was also too young, so it passed via P2 and pinned nothing about
P3 — an assertion with two routes to its state pins neither (decision 20). Each case now satisfies the
two preconditions it is not about and asserts the failing one **by name**. Both shell tools were then
sabotaged, seven times between them, and every sabotage was caught.

---

## 7. What is still open

The plan's six open questions, unanswered. The two that block Task 8:

- **OQ2** — `resume` is one key read by both runners, so flipping the supervisor fresh also revokes the
  owner's 2026-09-10 request that a respawned terminal supervisor continue its own conversation.
- **OQ6** — `/tokens` and `/cost` attribute by session id, and a fresh supervisor mints a new one every
  turn. Whether the respawn accumulator counts each once was **not verified**. Check before the flip,
  or the first thing the owner notices afterwards is a cost figure moving for a reason that is not real.
