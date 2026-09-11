# stage/8d — the six points of brief C the owner had already decided

**Date:** 2026-09-10 · **Branch:** `stage/8d-pulse-glyphs-and-the-general-bar`, branched from `ours/integration` at `6429cd9` (8a+8b+8c landed, plus the other agent's 9a/9b/10). · **Brief:** `docs/superpowers/specs/2026-09-09-telegram-briefs-A-B-C.md`, brief C — the **Decisions** and **Done-when** sections.

**OWNER REQUEST (2026-09-10):** *"C is not done. These six were DECIDED by the owner on 2026-09-09 (brief C, Decisions and Done-when), so they are in scope, not behaviour calls."*

Stage 8c delivered brief C's body and **parked six items as behaviour calls**. That was the wrong reading: all six are written down in the brief the owner had already approved. This branch closes them. The one genuine ambiguity in the list — how far "topic-name glyphs reduced to ❓ ⏸ 🏁" went — was put to the owner as a single question and answered the same day; their ruling is quoted in point 2 below.

**Language:** every string here is **English**, owner-facing ones included (their rule of 2026-09-09: *"if the strings are hardcoded, English; the rule holds"*). Decision 11 governs what a ROLE writes.

## The six

| # | What was wrong | What it does now |
|---|---|---|
| 1 | **PULSE re-posted whenever it was buried**, changed or not. A quiet orchestration says the same thing minute after minute, so every pause in a talkative topic bought a delete plus a post carrying no news — the surface breaking its own promise, since PULSE exists so that status costs no notifications. | The repost **rides on the decider** instead of overriding it: "something new to say" is `Decide() != None`, which already answers None for identical text and for blank text. One rule, not a second comparison. |
| 2 | **Topic-name glyphs.** Nine glyphs competed for the front of every topic name, and two of them — ✈ away and 🤐 quiet — are **app-wide**, so one toggle renamed every open topic at once and each rename writes a service message into the thread it renames. | The name keeps what is about the **work or the owner**: ❓, ⏸ (paused for a usage limit), 🏁 (closed), plus the two the owner sets by hand, 🧪 and ✅. Every **mode** glyph moved to PULSE's header: 🌙 🔕 ✈ 🤐 💻. ⛔ folded into ❓ ("same meaning for the owner"). The ledger recap gave 🏁 up and took 🎯. |
| 3 | **General's command bar was built, unit-tested, and never rendered** — `Build_ForGeneral` had no production caller at all — and three of its five commands had no case in the tap handler. | The dashboard is sent and edited through the **row-aware** calls, so the bar rides on it exactly as the topic bar rides on PULSE. `/summary`, `/resume` and `/dnd_all` call the same code the typed commands call. |
| 4 | **DND froze everything**, including the two surfaces that make no sound. The owner's check-in ritual — "show me where everything stands" — was reading a PULSE and a dashboard frozen at the moment the mute went on. | 🌙 holds only what rings. The two silent surfaces run before the DND return; everything else stays held. **Deferred and Silenced part company**: 🌙 keeps content and replays it, 🔕 drops it, so a status surface stays current under one and out of the way under the other. |
| 5 | **PULSE field 4 paired a clock with a subject from a different event** — the subject was the newest member-spoke entry, the clock was the supervisor's last owner-channel stamp. `last · 10:00 · <the implementer's subject>`: two true facts, one false sentence. | The picker returns the winning entry's subject **and its stamp** as one `TopicLastEvent`. `LastEventAt` is deleted from `TopicStatusFields` rather than left unused — keeping it would keep the bug available. |
| 6 | **"Once per question" was keyed on the agent-written `[n]`** — the one number in this system that must never be an identity (decision 12). | Keyed on a hash of **author + subject + body** via the existing `ChannelEntry_Digest`. Deliberately not `RawText`, which carries the header and would key on the index through the back door. |

## Why 5 and 6 were invisible

Neither was a wrong line of code; both were two right halves that were never asserted together.

- Field 4's clock was **entirely untested** — `grep LastEventAt` across the test project returned nothing — because no test ever set it, so the pair could not be compared. The fix makes the pairing structural: one argument, so there is no second one left to fill from a second file.
- The stall-alert key produced **two silent failures, each looking like the other's fix**: on a duplicate index (`option-lab-2` carried two `[80]` and two `[81]` in one evening) two different questions shared one key and the second was never alerted about at all; across a compaction that renumbered an entry, the same question earned a second alert.

## Probes

Unit, on the pure decision surfaces (the engine is `internal sealed` with no `InternalsVisibleTo`, so anything decided inside it is unreachable from the suite — the reason these classes exist):

- `TheRepostDoesNotFireWhenTheTextHasNotChanged` — the retired claim, kept under an inverted name
- `BuriedAndUnchangedStaysPut_BuriedAndChangedMovesOnce` — the brief's own probe, including the third call that proves a changed line moves **once** and not on every later tick
- `AfterARestartNothingIsRepostedUntilRealTrafficIsSeen` — the pair of in-memory blind spots that cover each other
- `ADeferredTopicIsStillPostedInto_Silently`, `ADeferredTopicStillMovesItsLine_Silently`, `ASilencedTopicIsNotPostedInto`, `ASilencedTopicIsNotRepostedIntoAndFallsBackToTheEdit`
- `TheLastFieldsClockIsTheClockOfTheEventItNames`, `AnEventWhoseStampCannotBeReadPrintsNoClock`
- `TwoQuestionsSharingAnIndexAreTwoQuestions_AndOneQuestionRenumberedIsStillOne`
- `EveryButton_HasACaseInTheTapHandler` / `EveryButton_IsKnownToTheCommandLexer`, now walking **both** bars

**Mutation-checked, every one**: the gate forced true (1); either mode gate widened back to `!= Normal` (4); the clock sourced from a different member's entry, which is the original defect exactly (5); the key returned as the index again (6); a renamed `case` (3).

### The guard that was green over three dead buttons

`EveryTopicButtonIsWired` walked `TopicCommandButtons.Commands` and never `GeneralCommands`, so `/summary`, `/resume` and `/dnd_all` sat with no case in the switch under a green suite. It had also never been possible to notice by hand, because the bar was never drawn — nobody can tap a button that is not there. A guard reporting "every button wired" over a list it had never read is decision 20's harness, in the file written to prevent it. It walks both lists now, concatenated rather than duplicated, so a third bar is covered by construction.

## What the branch review found, and why it mattered

An adversarial review after the six points were written found that **two of them did not actually hold**, and that two of my own probes proved less than they claimed. All of it is in `a67b651`. The two that matter:

- **Point 4 turned held traffic into LOST traffic.** `Tell_LedgerMovement_Async` remembers the reading it compares against and *then* sends through a method that returns silently in any mode but Normal — mutate first, gate second. Running the status refresh under a mute advanced the remembered ledger on every tick while the notice was dropped, so on unmute there was nothing left to report; and because `_recappedOrchIds` was already armed, the owner could **never** be told their endeavour had finished. It now gates before it consumes, which also closes the case a per-topic 🌙 has always had.
- **Point 1 was defeated by PULSE's own heartbeat.** Field 6 is `updated HH:MM`, emitted unconditionally, so the text differs at every minute boundary however still the orchestration is: "buried AND content changed" silently meant "buried, then within sixty seconds" — worse than the old rule, because it looks fixed. The whole suite was blind to it, since every planner test freezes `now` and both compared texts come from one clock.
- **🏁 could never render** — every caller of the name composer skips closed sessions, so the owner's glyph was built, documented, unit-tested and unreachable. That is the `Build_ForGeneral` defect, reintroduced by the commit that fixed it.
- **Point 6 opened a new silent failure**: a byte-identical question asked again hashed to the remembered key and was never alerted about. The key is now cleared where the debt is settled.

And two of mine: the DND probe was carried by the moon glyph rather than by the event it claimed to measure, and the sound guard had two routes to passing. Both rewritten and mutation-checked.

**The lesson worth keeping:** four of these six findings are the same shape — *a statement that was true when it was written and that my change made false*. Two were comments, one was a test's own summary, and two were behaviours whose justification I inherited without re-checking. Writing the change is not the work; re-reading what the change makes untrue is.

## Not in this branch

- **`Push_PeriodicStatus_Async` is still misnamed** — there is no periodic status any more. A rename touching every call site is not one of the six.
- **The dead code 8c parked** (`_lastPostedProgressByOrchId`, `Should_SendHandoffLine`, `Build_BusyHandoffLine_OrNull`, the orphaned `Channel` / `Age_Everything` test helpers) stays parked.
- **An unreadable member state file can still log on every tick.**
- **~100 test fixtures delete their temp tree unguarded**, any of which can hit the macOS teardown race `TempTree` was written for; only the fixture that actually failed uses it.
- **`⛔`'s constant is kept** though nothing draws it, so `Strip_Glyph` can take it off names an older build wrote. Every currently-blocked topic is wearing one.
- **A member row's duration ticks with the clock too**, so a topic with a live member can still move its line about once a minute — the same class as the heartbeat, with a narrower blast radius. Whether "5 min instead of 4" is news worth moving the message for is the owner's ruling, not mine to assume.
- **📸 is still a mode glyph on a name.** The General topic's own name carries the status-screenshots marker, which is a delivery setting — the category point 2 moved to PULSE's header. General has a dashboard rather than a PULSE, so where it should go is a design question. `Strip_Glyph` does not know 📸 either.
- **`AwayMode_Policy.AWAY_GLYPH` / `QUIET_GLYPH`** are a second spelling of two characters, read by nothing but the test that keeps them in step with the first spelling. Two lines to delete; outside the six.
