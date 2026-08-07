# Investigation — why the task ledger could go stale (2026-08-07)

Framing accepted: "the supervisor failed to update it" is the symptom. The finding is that PLAN.md
was the **only artifact in the protocol with no feedback loop**, and the fix is mechanism.

## The lever, from your own controlled experiment

Same session, same day, same load: the ledger update (prose in a role command) was skipped at four
consecutive boundaries; `/style-check` (blocked by a Stop hook) was skipped zero times. Enforcement,
not diligence. So the ledger now gets the same lever.

## What was built

| Failure mode from the report | Mechanism | Would it have caught it? |
|---|---|---|
| 1. Unrepresentable line (`tasks 3-9`) | `PlanShape_Validator` — flags task lines naming a numeric RANGE or listing 4+ deliverables; the app posts the complaint into the owner channel and texts the owner | **Yes**, at the moment the line appeared |
| 2. Four verdicts, zero ledger updates | `LedgerHealth_Tracker` + `kit/hooks/supervisor-ledger-check.sh` — a supervisor entry in a spoke marks the ledger DUE; if PLAN.md is older 90 s later, a flag file is raised and the **Stop hook blocks turn-end** | **Yes**, at the first boundary |
| 3. Ledger write that silently failed | Same as 2 — the flag clears only when PLAN.md's mtime actually moves, so an append that never landed keeps the block | **Yes**, the intent-vs-result gap is what is measured |

False-positive behaviour, since a nudge that cries wolf gets ignored:
- 90-second grace, so a supervisor still mid-turn is never blocked.
- The block is self-clearing: write the file, end the turn. No acknowledgement step to forget.
- Shape complaints are re-reported only when the offending SET changes, never on a loop.
- A missing/unparseable settings file, a missing flag, or any hook error **allows** the turn —
  enforcement must never wedge a session.

## Answers to the five questions

1. **Derived vs declared — hybrid, and the split is principled.** Commits, channel entries and
   verdicts are observable; *not-yet-started work is not*, and that is exactly what a progress bar
   needs. So the declared list keeps ownership of the task set and of intent, while the app owns
   **freshness**: it can always tell whether the declaration still matches observable reality, which
   is the half that was missing. Full derivation would silently drop the untouched-work denominator.
2. **What enforces it:** the Stop hook above — the smallest mechanism that catches all three modes.
   Reconciling commit counts against `[x]` was considered and rejected as the primary: commit counts
   and task counts legitimately diverge (one task, five commits), so it would fire spuriously and be
   ignored. It remains available as a report, not a block.
3. **Shape validated at write time** — warn, do not reject. The app cannot intercept a file write,
   and a rejected ledger would just be rewritten unvalidated; a complaint in the channel plus a text
   reaches both the supervisor and the owner within one tick.
4. **One bar for two branches is dishonest** — acknowledged, NOT yet fixed. The detail window
   already shows per-worktree git ground truth (branch, ahead/behind, commits), which is the honest
   per-branch view; splitting PLAN.md into per-member sections is the follow-up.
5. **Every other artifact in the same trap** — the highest-value question. Audited, with status:

   | Artifact | Signal if skipped | Status |
   |---|---|---|
   | Watcher re-arm | none (session unreachable) | **fixed** — nudge probe + orphan respawn |
   | Reply to the owner | none | **fixed** — app detects and nudges, receipt updated |
   | `reason` on spawn/close | none | **fixed** — request REJECTED without it |
   | PLAN.md update | none | **fixed** — this change |
   | PLAN.md shape | none | **fixed** — this change |
   | Append-only channel writes | none (destroys entries) | **partly** — the tailer detects the shrink and logs it; it should ALERT |
   | `WORKTREE:` marker in a brief | none (UI just shows nothing) | open |
   | Periodic STATUS entries | none | open (stall detection catches the extreme case only) |
   | Orchestration display name | none (card shows the raw id) | open, cosmetic |
