# stage/19 — a final report is never superseded

**Date:** 2026-09-10 · **Branch:** `stage/19-a-final-report-is-never-superseded` (worktree `../AIOrchestrator-stage19`), from `ours/integration` @ `a67a83b`. · **Reported by:** a supervisor session, measured 3× on 2026-09-09/10.

**OWNER DECISION (2026-09-10):** *"the best choice, whatever the effort"* — detection AND no loss, on BOTH transports, plus the kit rule that stops it happening.

## The defect

With both runners the session's final message IS its channel entry, and `ITurnResult.ResultText` was the ONLY text ever filed. The print runner read the last JSON line's `result`; the stream runner skipped every non-result event and took the `result` event's text. So when a session wrote its final report and a **BACKGROUND sub-agent** (`Task` with `run_in_background`) returned afterwards, the CLI resumed the session, a later message was produced, and THAT became the entry. Measured losses: once a 19,771-character report, once a nine-agent review. Both turns reported **success**. `PrintTurnDispatcherModel.Execute_Async` called `Write_Reply_Async` exactly once with that one string, and no `parent_tool_use_id` / sidechain field was read anywhere.

## What changed (branch source)

| # | Part | Where | What |
|---|---|---|---|
| 1 | **The rule** | `AIOrchestratorCoreLib/Running/TurnResult/SupersededFinals_Rule.cs` (new) | One rule, read by both transports. FINAL-LOOKING = an `assistant` event that carries text, asks for no tool, is **not** a sub-agent's (`parent_tool_use_id` / `isSidechain`) and did not stop for a tool call or a token ceiling (`stop_reason` `tool_use` / `max_tokens`). DEDUPE, one-directional: a final-looking text is NOT superseded when the result text is identical to it or **begins with** it (trimmed, ordinal); a result the earlier text is not a prefix of is content the result does not carry, so it is filed. Nothing is deduped the other way — a result that is a prefix of a longer earlier message means the longer one was lost, which is this defect. |
| 2 | **The carrier** | `TurnResult/ITurnResult.cs`, `TurnResultModel.cs`, `TurnResult_Factory.cs` | New member `IReadOnlyList<string> SupersededFinals` — empty by default, so every existing call site compiles unchanged. `CreateFrom_NothingToClose` and the stream executor's `With_BootAddedIn` carry it through their rebuilds. `TurnResultModel` is the only implementation and the factory the only construction path, so there was nothing else to extend. |
| 3 | **Detection, stream** | `Running/StreamTurn/StreamSessionProcess.cs` | The await loop keeps every final-looking assistant text in order and hands the list to `Build_Result`. That loop is the last place they are visible: once the `result` event arrives the earlier messages are gone from everything downstream. |
| 4 | **Detection, print** | `Running/PrintTurnCommand_Builder.cs`, `Running/PrintTurnRunner/PrintTurnRunnerModel.cs` | The print command line is now `-p --output-format stream-json --verbose …` (`--verbose` is not decoration — the CLI refuses stream-json without it), the same format the stream runner has always used in production. The runner still reads to end, still honours the turn timeout and the two-cancellations rule, still drains stderr; only the parse changed. |
| 5 | **The NDJSON reader** | `TurnResult/TurnResult_Parser.cs` | `Parse_Stream` / `Read_Stream`: the result is the LAST line whose `type` is `result`, never simply the last JSON object — the `Stop` hook's `hook_response` lands AFTER it. Assistant events are tracked on the way past. No `result` line at all falls back to the legacy reading (last JSON object, else the raw text), so a turn that died before its result is still reported and the runner's own unit tests, which still pass `--output-format json`, parse exactly as before. `Parse` keeps its old signature plus an optional final-looking list. |
| 6 | **No loss** | `Running/PrintTurnDispatcher/PrintTurnDispatcherModel.cs` (`Write_SupersededFinals_Async`, ~:1282) | Each superseded final is filed as its own channel entry through the SAME splitters, under the same author word, in order, BEFORE the turn's own entry. One INFO line names the count. |
| 7 | **The session is told** | same file (`Append_SupersededNotice`), `Running/PrintTurn_Words.cs` | One app-authored entry in the session's own channel, audience Agent — the same shape as the misaddressed-block notice — naming the count, saying both were filed, and naming the cause (a background sub-agent returning re-opens the turn). Agent audience per decision 15: the action is the session's and the owner cannot take it. |
| 8 | **The turn log** | `Running/TurnLog/TurnLog_Store.cs` | `Append_TurnResult` reads the RESULT LINE (`TurnResult_Parser.Find_TurnDocument_OrNull`) rather than parsing the whole of `RawStdout`. A multi-line NDJSON blob parses as nothing, which would have filed every print turn as the synthesised fallback record and silently dropped the fields `/tail` reads. |
| 9 | **The harness** | `tools/claude-contract/FakeClaude/FakeClaudeScenario.cs`, `StreamEventJson_Builder.cs`, `StreamJson_Responder.cs`, `Program.cs` | A turn gains `"assistant_messages": [...]` (N text-only assistant events) and `"tool_use_between": true` (a `tool_use` event between them). ONE builder (`Build_AssistantSequence`) serves both responders, so a print turn and a stream turn of the same scenario put the same events on the wire. Print mode now speaks `--output-format stream-json` (`init`, the assistant events, the `result`) and refuses it without `--verbose`, as the real CLI does. |
| 10 | **The kit rule** | 5 × `SKILL.md`, 6 × `reference/print-runner.md`, `supervisor/reference/stream-runner.md` | One sentence per file, in the skill's own voice: never write the final message while a background sub-agent is still running — wait for every agent first, because a late return re-opens the turn and a later message replaces the entry. |

## Probes

New, `AIOrchestratorCoreLib.Tests/Running/SupersededFinalTests.cs` — both transports driven through FakeClaude, nothing touches the real CLI:

- `AFinalLookingMessageFollowedByALaterOne_IsFiledAsWell_AndTheSessionIsTold` — **stream**: two text-only assistant messages with a `tool_use` between them, a result equal to the second. Two member entries in the written order, then the app notice.
- `AFinalMessageTHATISTheResult_IsFiledOnce_AndNothingIsSaidAboutIt` — **stream**: one assistant message equal to the result. Exactly one entry, no notice.
- `ThePrintTransportSeesTheSameThing_BecauseItReadsTheSameStream` — **print**: the same scenario through the print transport, the same assertions. (A stream session spends its first message on the boot and a print one does not, so the briefed turn is scenario turn 2 for one and turn 1 for the other; everything else about the pair is identical.)
- `AnAssistantMessageThatAsksForATool_IsNotAFinalMessage`, `ASubAgentsMessage_IsNeverAFinalMessageOfTheSession`, `AMessageTheResultBeginsWith_IsNotSuperseded_AndOneItDoesNotIs` — the rule on its own.
- `TheStreamReader_TakesTheRESULTLine_NotTheLastJsonLine` (with a hook response after the result and a non-JSON line in the middle), `AStreamWithNoResultLine_StillReportsTheTurn` — the NDJSON reader on its own.

Contract stub, `tools/claude-contract/ClaudeContract.Tests/Stub/FakeClaudeStubTests.cs`: `PrintStreamJson_EmitsInit_TheAssistantMessages_ThenTheResult` — pins the harness extension itself, including the `--verbose` refusal.

Existing, updated for the new command line (assertion only, no behaviour): `PrintTurnCommandBuilderTests` (2), `PrintTurnDispatcherTests.FirstTurn_…`, `StreamTurnDispatcherTests.ThreeDeathsInARow_FALLBACKTOPRINT_AndTheLogSaysSo` — the fallback rung is now told apart from the stream rung by the absence of `--input-format`, since both ask for `--output-format stream-json`. `TurnResultParserTests` are untouched and still hold: `Parse` still reads a single `--output-format json` document exactly as it did.

## Mutation checks

Each mutation was applied to the branch source and the affected probes were RUN; the names below are copied from the failing output.

| Reverted | Fails (measured) |
|---|---|
| **A — detection**: `Select_Superseded` returns `[]` | 4 of 8: `AFinalLookingMessageFollowedByALaterOne_…`, `ThePrintTransportSeesTheSameThing_…`, `AMessageTheResultBeginsWith_…`, `TheStreamReader_TakesTheRESULTLine_…` |
| **A — dedupe**: drop the `result.StartsWith(candidate)` guard | 5 of 8: the four above plus `AFinalMessageTHATISTheResult_IsFiledOnce_…` (the ordinary turn's entry is filed twice) |
| **B — print visibility**: `PrintTurnCommand_Builder` back to `-p --output-format json` | 5: `ThePrintTransportSeesTheSameThing_…` (one entry instead of two, no notice), `PrintTurnCommandBuilderTests` ×2, `PrintTurnDispatcherTests.FirstTurn_…`, `StreamTurnDispatcherTests.ThreeDeathsInARow_FALLBACKTOPRINT_…` |

## Suite

`dotnet test AIOrchestratorCoreLib.Tests/AIOrchestratorCoreLib.Tests.csproj -c Release --nologo`, copied from the run: **Failed: 0, Passed: 3273, Skipped: 9, Total: 3282, Duration: 1 m 16 s**. Baseline on `a67a83b` was 3264 passed / 1 red (`ClosingTurnReviewFixTests.WithNoPrintRungBeneathIt_…`, a known intermittent) / 9 skipped — that test passed here in the full run, and the 8 new probes account for the difference. `tools/claude-contract/ClaudeContract.Tests` separately: **Failed: 0, Passed: 31, Skipped: 8, Total: 39** (the 8 skips are the Live tests, which need the real CLI).

## Not in this stage

- **Nothing is inferred about WHY the turn re-opened.** The bridge files what it can see. `parent_tool_use_id` is read only to EXCLUDE a sub-agent's own messages, never to attribute a supersede to one.
- **The closing turn does not carry superseded finals.** A deadline-killed turn has no result document; its "where I got to" report is the closing turn's job, and passing final-looking texts into a failure path would file them with nothing to dedupe against.

## PARKED (found on the way, not fixed — decision 22)

- **Mid-turn narration is structurally indistinguishable from a final message** when the CLI emits a text-only assistant event with no `stop_reason`. The rule excludes the two `stop_reason` values that prove a message did not end the turn (`tool_use`, `max_tokens`) and excludes sub-agent traffic, which covers what was measured; an absent `stop_reason` is not evidence either way and stays final-looking. If a live round shows narration being filed, the next lever is to require `stop_reason == "end_turn"` — which needs a measurement of what the real CLI actually emits per assistant event, and this stage has none.
- **`kit/skills/subagents/SKILL.md` has no such sentence.** It is not a role skill and was outside the brief's list, but it is the one skill whose whole subject is delegation, and the rule belongs there too. One line, someone else's call.
- **The Live contract tests still pin `-p --output-format json`** (`tools/claude-contract/ClaudeContract.Tests/Live/ClaudeCliContractLiveTests.cs`, four call sites). They are `[Skip]`-gated and were not run here; they measure the CLI, not the bridge, so they are not wrong — but nothing in the suite now measures the real CLI's `-p --output-format stream-json --verbose` shape against the reader that consumes it. That is a live-round item.
