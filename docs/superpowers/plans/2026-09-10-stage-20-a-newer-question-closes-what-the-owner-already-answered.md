# stage/20 — a newer question closes the older ones the owner had already answered in words

**Date:** 2026-09-10 · **Branch:** work done on `stage/16-the-supervisor-never-queues` (same session; separable by file — `QuestionClosure_Wording.cs`, `QuestionPrompt_Builder.cs`, the `Find_QuestionsToSupersede` / `Supersede_OlderQuestions_Async` pair and the one stamp in the owner router) · **Owner decision:** the *conditional* rule — "close the older ones only if I had written something in between" — chosen 2026-09-10 over "a new question always closes the old ones".

**OWNER REQUEST (2026-09-10):** PULSE showed *"waiting on you · Sup: ❓ 294 and 279 are blocked by other (since 15:58) · ❓ Which do I start now? (since 16:46) · ❓ Should attaching over-cap screener strategy be refused (since 16:56) · +3 more"* at 21:50 — six questions the owner had answered hours earlier, in prose.

## Why they never closed

With one question open a typed reply binds to it and closes it (`AnswerBinding_Decider`). With two or more open it binds to **none** — the app refuses to guess which was meant (stage 8a's root fix, and right). Nothing else closes a question except a tap, a deadline the asker rarely sets, away-mode parking, or the orchestration closing. The supervisor asked questions in pairs all afternoon (`a second question went out while one was still open` ×3 in four minutes in the journal), the owner answered in words, and every one of them stayed on the PULSE line as "waiting on you".

## The rule (and the one it is not)

**A typed reply from the owner, FOLLOWED BY a newer question from the same asker in the same topic, closes the questions asked before that reply** — as *superseded*: out of the registry with a recorded reason, keyboard consumed (a late tap is refused, never routed as a stale answer), message rewritten `⏭ superseded — you had replied in words and a newer question followed. It will be re-asked if it still matters.`, any pending high-risk read-back discarded, and one Agent-audience entry telling the asker to re-ask what still matters.

**Not** "a new question always closes the old ones": two genuinely parallel questions with no reply between them stay open — the contract three existing probes pin (`ATapOnOneQuestion_LeavesTheOtherOpen_TappableAndUnstamped`, `FiveDecisionsPending_SurviveTheProcessThatWasHoldingThem_…`, `AnEntryWrittenAfterAnAppEntry…`) is unchanged, and none of them was touched.

The "replied in words" stamp is per topic and **in memory**: after a restart nothing is superseded until the owner types again, which errs on the side of leaving a question open.

## What changed (branch source)

| file | change |
|---|---|
| `Bridge/Decisions/QuestionClosure_Wording.cs` | `SUPERSEDED` reason. |
| `Formatting/QuestionPrompt_Builder.cs` | `Build_SupersededText` + `SUPERSEDED_SUFFIX` (no ✅ — no choice was recorded). |
| `Bridge/BridgeEngine/BridgeEngineModel.cs` | `_ownerRepliedInWordsUtcByOrchId`, stamped in `Route_OwnerMessage_Async` for every non-app-composed message (bound or not); `Find_QuestionsToSupersede` decided **before** the send (so the "second question while one is open" coaching is not written about questions this send closes) and `Supersede_OlderQuestions_Async` run **after** it (a failed send leaves the older ones open). The rewrite goes through stage 17's rate-limit retry. |

## Probes

- `QuestionContractProbeTests.ANewQuestion_AfterTheOwnerRepliedInWords_SupersedesTheOlderOnes` — two open, a typed reply that binds neither, a third question: exactly the third is open, both older messages carry the suffix, the asker is told, a late tap on the first is `Callback REFUSED`. The harness clock is frozen, so it is stepped before the reply and before the third question — "replied after asked" compares two stamps of that clock.
- `QuestionPromptBuilderTests.ASupersededQuestion_KeepsItsWords_SaysWhy_AndRecordsNoChoice`.
- `QuestionClosureWordingTests.EveryReason_IsDistinctAndReadable` now includes `SUPERSEDED`.

**Mutation-checked:** with `Find_QuestionsToSupersede` always empty, the probe fails (two stay open, the coaching line is written instead); restored (with `touch`), it passes.

## PARKED (found on the way — decision 22)

- The stamp is not persisted with the engine state; a restart between the reply and the next question leaves the older questions open. Cheap to add to `EngineStateSnapshot` if it bites.
- `Append_Supervisor` in the probe harness numbers entries by hand and collides with the app's own `[5]`/`[6]` (the tailer warns `index runs backwards`); the probe numbers past them. A helper that reads the last index would remove the class.
