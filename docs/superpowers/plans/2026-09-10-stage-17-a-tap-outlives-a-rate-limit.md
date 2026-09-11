# stage/17 — a tap's rewrite outlives a rate limit, and PULSE honours its own back-off

**Date:** 2026-09-10 · **Branch:** work done on `stage/16-the-supervisor-never-queues` after stage 16's own change (same session, same checkout; the two are separable by file — see the table) · **Owner decision:** "retry for up to about a minute, in the background" (2026-09-10).

**OWNER REQUEST (2026-09-10):** *"Let's talk does not work as it should — the buttons should disappear and the message should change — and a button I pressed printed the wrong message but did the right thing."*

## What was measured (VPS journal, build `481efc9`, 20:56–21:52)

**382** `HTTP 429 Too Many Requests` in 56 minutes. 357 were the PULSE status line (192 fincanva-5, 165 fincanva-6), retried **every 2 s** — the mirror tick — with Telegram's `retry_after` counting down 34, 31, 29, 27, 25, 22, 20 … The 30-second back-off (`MirrorRetryBackoffSeconds`, `TopicStatusLine_Planner.Is_AttemptDue`) was in place and tested, and never reached the branch that mattered.

At 21:11:40, inside that storm, the owner's tap: `Answered-question edit failed: … 429 … retry after 24` then `Button keyboard removal failed for message 2306: … 429`. Neither was retried. The tap itself was routed (`Owner message delivered to the supervisor`, 21:11:44). The phone kept the old text and a live keyboard on a question already answered — which is what the owner saw.

## Root causes (both read in the code, not inferred)

1. **PULSE.** `Refresh_TopicStatusLines_Async`: after `Plan` returns `None` under back-off, the "button-only change" promotion (`renderKey != lastText → Edit`) re-derived the action from the rendering alone. A failed edit deliberately leaves `lastText` at the last text SENT, so after one 429 the rendering "differs" on every tick and the back-off is overruled every 2 s for as long as Telegram refuses. Ruled out on the way: the planner's branch order (the back-off really is last), the failure stamp being removed between ticks, non-production timing in the daemon (`BridgeEngine_Factory.cs:34` uses `Create_Production`), local/UTC clock mismatch (both `DateTime.Now`).
2. **Tap.** `Handle_CallbackTap_Async` and `Record_AnsweredQuestion_BestEffort_Async` caught the edit's exception, logged, and fell back to `Remove_Buttons_BestEffort_Async` — one shot each. The client's inline retry covers only `retry_after ≤ 2 s`; every edit 429 measured here carries 20–34 s, so the client always throws and the engine always gave up. `TelegramApiException.RetryAfterSeconds` was carried all the way up and read by nobody.

## What changed (branch source)

| file | change |
|---|---|
| `Telegram/RateLimitedRetry_Policy.cs` (new) | Pure: `Wait_BeforeNextAttempt_OrNull(failure, attemptsMade)` — a 429 waits Telegram's own `retry_after` (clamped to 30 s; 5 s when absent), at most 3 attempts in total; anything else (400, 5xx, other exceptions) is not retried. Worst case inside the owner's "about a minute". |
| `Bridge/BridgeEngine/BridgeEngineModel.cs` | `Rewrite_AnsweredQuestion_WithRetry_Async`: first attempt inline (the common case is unchanged); on a rate limit the retries run on a detached task (flow suppressed, engine cancellation), one INFO line when it lands (`landed on attempt N after waiting S s`), and only after the last attempt the old fallback (remove the keyboard, best effort) plus one WARN naming the attempts and the wait. Both call sites use it. **PULSE:** the promotion to `Edit` now passes through the same `Is_AttemptDue(lastFailedAttemptAt, now, MirrorRetryBackoffSeconds)` as the planner. |
| `AIOrchestratorCoreLib.Tests/Bridge/DecisionStateSurvivesARestartTests.cs` (`CapturingTelegram_Fake`) | Fault injection: `Refuse_Edits_WithRateLimit(count, retryAfterSeconds, messageId?)` — scoped to one message so the status line cannot spend a budget meant for the tap; `Count_EditAttempts(messageId)`; `Find_MessageIdOfSentContaining`. The button-row edit variants (PULSE's) are counted and refusable through the same path but their text is NOT recorded as "edited" — a probe asking whether the second question was stamped must not find its words inside PULSE's "waiting on you" field. |

## Probes

- `RateLimitedRetryPolicyTests` — Telegram's number is waited; above the cap it is clamped; no usable number still costs the default; the last attempt is the cap; 400/5xx/other are not retried; the schedule stays inside a minute.
- `QuestionContractProbeTests.ATapWhoseRewriteIsRateLimited_IsRewrittenAnyway_AfterTelegramsWait` — the question message refuses its first two edits (`retry_after: 1`); the tap is routed regardless; the rewrite lands on attempt 3 and the log says so. One engine run for both outcomes: the retries live on the engine's cancellation, and a probe that stopped the engine between the two would cancel the wait it is probing (that is how the first draft of this probe failed).
- `QuestionContractProbeTests.APulseEditThatIsRateLimited_IsNotRetriedEveryTick` — every edit of the PULSE message is refused; an open question makes the line due; across forty ticks it is attempted once, not forty times.

**Mutation-checked:** policy that never retries → the tap probe fails; promotion with the back-off removed (`&& true`) → the PULSE probe fails; restored (with `touch`, see stage 16's lesson) both pass.

## Relationship to stage/15 (merged, not yet deployed)

Stage 15 makes PULSE change text at most every five minutes and adds a per-message edit gate in the send budget. It reduces the *pressure*; it does not stop the promotion from asking for an edit every tick once one has failed, and that per-message gate is **awaited** inside the status-line refresh — so without this fix the mirror tick could have been held for up to 30 s by a line that should simply have waited for its next turn. Both belong in the same deploy.

## PARKED (found on the way — decision 22)

- `Remove_Buttons_BestEffort_Async` (the last-resort fallback) is still one shot; if it, too, is rate-limited the keyboard stays. A durable "pending rewrite" that survives a restart would remove the whole class; bigger than this stage.
- `Busy-supervisor narration failed` (10 of the 382) and `Topic name sync failed` (10) are the same shape — the topic-name path already honours a retry-after stamp; the narration does not. Not the owner's request.
- The control bucket in `TokenBucket_Gate` is sized from the 20/min group ceiling; the per-message ceiling Telegram actually enforces on edits is different, as the `retry_after` values show. Stage 15's per-message gate is the answer to that; re-measure after the deploy (`journalctl … | grep -c "HTTP 429"`).
