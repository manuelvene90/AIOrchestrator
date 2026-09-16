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

Recorded per task in §6 below. The three known-flaky families (`ClosingTurnReviewFix…`,
`TolerantFileReader…`, `EffortDial…`) are re-run alone before any red in them is believed.

---

## 6. What was done

Filled in per task as the work lands.

---

## 7. What is still open

The plan's six open questions, unanswered. The two that block Task 8:

- **OQ2** — `resume` is one key read by both runners, so flipping the supervisor fresh also revokes the
  owner's 2026-09-10 request that a respawned terminal supervisor continue its own conversation.
- **OQ6** — `/tokens` and `/cost` attribute by session id, and a fresh supervisor mints a new one every
  turn. Whether the respawn accumulator counts each once was **not verified**. Check before the flip,
  or the first thing the owner notices afterwards is a cost figure moving for a reason that is not real.
