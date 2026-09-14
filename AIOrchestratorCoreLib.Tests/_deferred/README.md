# Deferred tests — kept out of the build by `<Compile Remove="_deferred\**" />`

These are master-only test files that the 2026-09-11 fork merge dropped and that plan 01 could NOT
un-park, because the feature each one asserts does not exist in this tree yet. They are kept — not
deleted — so the plan that builds the feature has the oracle already written.

A file leaves this folder by being MOVED into its proper namespace folder, not by being rewritten:
each one is master's file and master's assertions.

| file | what it asserts | owned by |
|---|---|---|
| `AStatusLineDoesNotSpendTheOwnersWaitTests.cs` | the owner-answer credit (decision 25) is not spent by a `STATUS` / `WAITING ON` line, and the entry filed after the answer arrives inside a turn-ended completion | **plan 03** — all five of its oracles are the narration filter's (`phone.push = filtered`) plus the `_suppressedEntries` digest. This build pushes every entry, so the file cannot go green before that lands. See the Task 7 row of `docs/superpowers/plans/2026-09-11-fork-merge-01-report.md`. |
| `ModelOnTheStatusLineTests.cs` | `supervisorModel` on the topic status line (`TopicStatusLine_Builder.Build` / `TopicStatusLine_Planner.Plan`) | **plan 03** — the Telegram PULSE status line, with its `modelEffort` field. |

`_pending-reports/`, the plan-01 parking lot this folder replaces, was deleted by Task 14 once every
other parked file had been un-parked by its task.
