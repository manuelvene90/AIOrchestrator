# stage/8b — the bridge neither loses the owner's messages nor lies about them

**Date:** 2026-09-10 · **Branch:** `stage/8b-no-lost-messages-no-false-receipts` (worktree `../AIOrchestrator-stage8b`), branched from **`stage/8a-a-tap-closes-its-message`** rather than from `ours/integration`: fold-in 1 marks the canned command requests `IsAppComposed`, and that field is stage 8a's. · **Brief:** `docs/superpowers/specs/2026-09-09-telegram-briefs-A-B-C.md`, brief B, plus two fold-ins from the owner (2026-09-10).

**OWNER REQUEST (2026-09-09):** *"Fix the six critical defects of the Telegram audit: the bridge must never lose a message of mine or tell me it was received when it was not."*

## What changed (branch source)

| # | Defect | Fix |
|---|---|---|
| 1 | **409 Conflict** read as any other `getUpdates` failure: one Error per retry, for ever, and the owner told nothing while their taps went to whichever host won the race. | 409 is caught by status code: **one** message to General **naming this machine** and the bot, **one** log line per state change, backoff unchanged, and one line when it recovers. `getMe` + `deleteWebhook(drop_pending_updates: false)` once at startup — a webhook produces the same 409 and could not be cleared from here at all. New `telegramInbound` config key (`poll` default / `off`) for the host that must not poll. |
| 2 | **Batch replay:** `_lastUpdateId` advanced only after the whole batch, so anything that ended one early re-served every update in it — four times on 2026-09-08, from a Windows P/Invoke on the Linux daemon. | Each update's work is isolated (`try`/`catch`/`finally` per update, per message and per tap) and recorded in a bounded handled-set, so a replay skips what was already done. Taps are deduplicated by `callback_query.id` — Telegram's own identity for the gesture. |
| 3 | **False ✓:** the receipt was sent after the routing call whatever the routing did — unknown topic dropped with a warning and ticked; a CLOSED orchestration written into a dead channel and ticked. | `Route_OwnerMessage_Async` returns an outcome (`Routed` / `UnknownTopic` / `ClosedOrchestration` / `AnsweredDirectly`); the ✓ follows `Routed` alone, and the other two get one line naming the way out. The closed check lives at the one site that WRITES the owner's words, not in the store's thirty-call-site lookup. |
| 4 | **Documents dropped in silence:** the parser knew text, photo, voice and callback, so a file with no caption produced no owner message at all and the offset advanced over it. A file WITH a caption was worse — the caption landed, so the owner had every reason to think the file had. | `message.document` is parsed (`TelegramDocumentRef`), downloaded to `media/` beside the channel, and named in the entry as `FILE: <path>`. 20 MB cap checked against the declared size AND the downloaded bytes; over it, one line to the owner. The file name is sanitised (`OwnerFileName_Sanitizer`) — it is remote input, and `Path.Combine` discards its folder the moment the second argument is rooted. |
| 5 | **30-minute give-up:** the append was CONFIRMED — cursor past those entries for ever — and one Error line went to a log on a machine the owner does not read. | The entries are **parked** (bounded, mirror's own predicate, in memory) and the first send that works carries them as ONE document (`UndeliveredDigest_Builder`), never as a replayed burst. The window is now read off the injected clock, which is what makes it testable. |
| 6 | **Windows-only calls by name from the engine:** `/show`, `/screen`, `/organize`, `/organize_mains` and the periodic screenshot reached three static classes of unguarded user32/dwmapi/gdi32 P/Invoke. | `IHostWindowing` (Windows / Unsupported / factory). The engine asks `Is_Supported` and refuses in one line — every time to the owner, once to the log. Nothing OS-specific is named in `BridgeEngineModel` any more. |
| F1 | **Fold-in:** a typed `/summary`-style command becomes a canned English request, unmarked, so with one question open in General it bound as that question's answer. | `Build_GeneralCommandMessage` marks it `IsAppComposed` — the same root fix stage 8a applied to taps, one route further along. |
| F2 | **Fold-in:** stage 8a parked a suspected lost-entry defect (a supervisor entry appended right after an app entry was never mirrored). | **Probed, reproduced, diagnosed — and it is NOT a product defect.** See below. |

## The parked finding from stage 8a: what it actually was

Reproduced first with the engine loop left running (not the start/stop harness), so it was not a harness artifact of that shape. The cause is the tailer's **trailing-entry rule**: the last entry in a channel file is held back until the file has stopped growing for `TrailingEntryQuietMilliseconds`, and that quiet is measured against the **injected clock** — which these probes freeze. Entry `[3]` was released only because `[4]` and `[5]` arrived behind it and made it no longer trailing; entry `[9]` was trailing, and "quiet" could never elapse. Stepping the clock delivers it immediately.

So: **nothing was ever lost in production**, my stage-8a diagnosis (mirror cursor / self-write baseline) was wrong, and the harness rule that cost two debugging sessions is now written down where the next reader will hit it — in `AnEntryWrittenAfterAnAppEntryStillReachesThePhoneTests`, which keeps the claim pinned, and in the stage-8a probe's own comment, which has been corrected.

## Probes

Engine-driven (real engine, scripted client):
- `A409Conflict_TellsTheOwnerOnce_KeepsRetrying_AndSaysWhenItIsOver`
- `OneUpdateThatThrows_CostsOnlyItself_AndTheOffsetMovesPastTheWholeBatch` — the throw is the incident's own `DllNotFoundException: user32.dll`, from an injected capability
- `TheSameCallbackDeliveredTwice_IsActedOnOnce`
- `AMessageIntoAClosedOrchestrationsTopic_IsNotTicked_NotAppended_AndExplained`
- `AMessageIntoATopicNobodyOwns_IsNotTicked_AndExplained`
- `ADocumentWithNoCaption_LandsInMedia_IsNamedInTheChannel_AndIsTicked`
- `EntriesTheMirrorGaveUpOn_ArriveAsOneDigestWhenTelegramAnswersAgain`
- `ACommandThisHostCannotDo_IsRefusedInOneLine_AndThrowsNothing`
- `AnEntryAppendedAfterTheAppsOwnEntry_IsStillMirrored`

Unit: `OwnerFileNameSanitizerTests` (traversal, rooted paths, dots-only, unicode — 33 cases), `TelegramInboundModesTests`, `UndeliveredDigestBuilderTests`, `OwnerRouteWordingTests` (over every enum value, so a new outcome cannot silently earn a receipt), `DocumentUpdatesParserTests`.

**Mutation-checked** — with each fix reverted, its probe fails: honest receipt (2 fail), per-update isolation (1), tap dedup (1), parking (1), document parsing (4 of 8).

## Not in this brief

- **E1** (retry + reconcile the topic delete) rides later with E2 — the owner's call, 2026-09-10: B's Scope list and its Done-when contain no delete probe.
- **No poller lease.** The brief asked for a lease file under the supervision root; `SingleInstance_Guard` already excludes two hosts that share a root, and no file under either root can see a host on the OTHER machine — which is the case that actually bites. Owner agreed (2026-09-10): 409 detection + `telegramInbound` instead. The key name was checked first, as asked: `telegramMode` already exists as the per-session delivery mode in `session.json`, so a new key was taken rather than overloading it.
- **`Held` is not a route outcome.** A held message IS routed — it lands in the delivery buffer and reaches the session on GO — so the hold stays a decision the caller makes after a successful route about which receipt to show. Making it an outcome would put one fact in two places.

## PARKED (found on the way, not in the row — decision 22)

- `Build_PhotoEntryText_Async` folds a failed photo download into the channel entry and never tells the OWNER — the document path, written here, replies to them as well. Same class, different marker; out of B's six.
- The mirror retry clock (`_mirrorRetryFirstFailureUtc`) is in memory only, so a restart inside the 30-minute window restarts the window. It now parks rather than drops, so the cost is a later give-up, not a loss.
