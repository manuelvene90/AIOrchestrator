# stage/8a — every tap changes the message it was tapped on

**Date:** 2026-09-09 · **Branch:** `stage/8a-a-tap-closes-its-message` (worktree `../AIOrchestrator-stage8a`, from `ours/integration` `3735bc7`) · **Brief:** `docs/superpowers/specs/2026-09-09-telegram-briefs-A-B-C.md`, brief A.

**OWNER REQUEST (2026-09-09):** *"Let's talk does nothing when I tap it — it stays there, all the other options stay too. Make it behave like the other buttons: buttons disappear, the message says 'ok, tell me what you have in mind'."*

## Why

`💬 Let's talk` was built to leave the question and its keyboard alone, so the owner could discuss a decision without taking it off their phone. What they actually got was a button whose tap changed nothing on screen — the only feedback is Telegram's transient toast — so on 2026-09-09 they tapped it twelve times in `fincanva-5`, four of them inside one minute.

Underneath it sat a defect that was never confined to that button: every tap is routed as an owner message, and that routing binds a message to a question whenever exactly one is open. The tapped question is removed *before* routing, so "exactly one other question open" is the ordinary case — the log recorded a second question going out while one was still open three times that afternoon. At 17:53 the owner tapped a high-risk option on one question; at 17:55 they tapped `Let's talk` on another; the talk text bound to the *orphaned-processes* question and stamped it `✅ answered:`. Nobody ever decided it.

And the one log line that reports a lapsed high-risk read-back asserted "the question is still open" without ever reading the registry — at ~16:03Z it said that about a question already stamped closed.

## What changed (branch source)

| file | change |
|---|---|
| `Bridge/EngineState/EngineStateSnapshot.cs` | `PendingButtonRecord.KeepsGroupOpen` → `AnswersNothing` (consumes the group like any option; records no choice). `OpenQuestionRecord.InDiscussion` removed — there is no third state between open and closed any more. |
| `Bridge/EngineState/EngineState_Serializer.cs` | writes `answersNothing`; reads `answersNothing` **or** the old `keepsGroupOpen`, so a state file from the previous build maps onto the new record instead of stamping a whole instruction text over a question. `inDiscussion` is no longer written and is ignored on read. |
| `Bridge/OwnerPush_Policy.cs` | `MORE_DETAIL_LABEL`/`MORE_DETAIL_REQUEST` removed — one button. `TALK_REQUEST` rewritten to end by **re-asking** (it used to order the opposite). New `TALK_ACKNOWLEDGEMENT`. |
| `Formatting/QuestionPrompt_Builder.cs` | `Build_TalkText` — the question, with the acknowledgement under it and no `✅`. |
| `Bridge/Decisions/QuestionClosure_Wording.cs` (new) | the fixed vocabulary of closure reasons, and the lapsed-read-back line built from what was actually read. |
| `Telegram/TelegramOwnerMessage/*` | `IsAppComposed` on the message triple — the root fix: a tap, a released read-back and an applied default never enter the typed-answer binding. Defaults to `false` (typed) so a new call site cannot get it wrong by omission. |
| `Bridge/BridgeEngine/BridgeEngineModel.cs` | one app button registered, not two; the `KeepsGroupOpen` branch gone (every tap edits its own message); closure reasons recorded beside every removal from `_openQuestions` (bounded, in memory, diagnostic only); `Close_AnsweredQuestions_Async` skipped for app-composed messages; the lapse line reads the registry. |
| `kit/skills/supervisor/SKILL.md`, `kit/skills/solo/SKILL.md` | ONE button; a tap closes the question; **re-ask once the discussion settles**. (Delivery is the app build / plugin reinstall — decision 17 — not the edit.) |

## Probes

- `LetsTalk_ClosesItsQuestionLikeAnyOption_EditsTheMessage_AndRecordsNoChoice` — replaces `LetsTalk_KeepsTheQuestionOpen_AndATypedReplyDoesNotCloseIt_UntilATapDoes`, which asserted the behaviour being removed.
- `ATapOnOneQuestion_LeavesTheOtherOpen_TappableAndUnstamped` — two questions open, tap on the first, `[Theory]` over an option and over `Let's talk`.
- `AReadBackThatLapsesOnAClosedQuestion_NamesWhatClosedIt`.
- `QuestionClosureWordingTests` (4 unit tests), `OwnerPushPolicyTests` — the explain-button test rewritten for the single button plus the acknowledgement.

One existing assertion changed with the button count, honestly: `DecisionStateSurvivesARestartTests` counts the buttons two questions put in the snapshot — 8 before (two options each plus the app's two), 6 now that the app adds one.

Each of the three engine probes was mutation-checked: with its fix reverted, it fails.

## PARKED (found on the way, not in the OWNER REQUEST row — decision 22)

- A supervisor entry appended to the owner channel *after* the app has appended an entry of its own in the same run was never read by the tailer in the probe harness: entry `[9]` sat in `owner-channel.md` with no `[owner] entry #9` log line across two 20 s engine runs (both `[Theory]` cases). Cause unproven — the mirror cursor or the self-write baseline are the suspects. Worked around in the probe (both questions appended before the engine runs); if it reproduces outside the harness it is a lost supervisor entry, which would be a defect of brief B's family.
- `Build_GeneralCommandMessage` turns a typed `/summary`-style command into a canned English request and does **not** mark it app-composed, so with exactly one question open in General it can still bind as that question's answer. Same family as this brief's root fix, but a command rather than a tap — out of the row.
