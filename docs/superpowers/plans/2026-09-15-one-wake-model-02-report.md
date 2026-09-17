# One Wake Model — Plan 02 report

**Date:** 2026-09-15/16 · **Branch:** `feat/bookkeeping-close`, off `feat/one-wake-model` at `a3cc8c5` (worktree `../AIOrchestrator-close02`) · **Plan:** `2026-09-15-one-wake-model-02-bookkeeping.md` · **Spec:** `../specs/2026-09-15-one-wake-model-design.md`, step 3 · **Predecessor:** `2026-09-15-one-wake-model-01-report.md`

## Copies read

**Branch source, this worktree** (CLAUDE.md decision 18) — for every finding below unless the line says
otherwise. Two readings were deliberately taken elsewhere and are marked as such: `tools/wake-baseline/`
was read in the MAIN checkout on `ours/integration` at `5b1c479`, because that is where the baseline
work landed and this branch does not contain it. No running app on this machine, so nothing here is a
reading of the fourth copy (decision 23). The kit was verified against `kit/` in this worktree and its
own bash benches — **not** against a restarted session, which under decision 17 is the weaker of the two
verifications and is stated rather than glossed.

`dotnet` is at `~/.dotnet/dotnet` (SDK 10.0.401), not on PATH.

## What shipped

| task | commit | what |
|---|---|---|
| 1 | `6a2cc7a` | the census — every app-authored write site is a register, not a paragraph |
| 2 | `e93adb5` | `status.jsonl`, and one locator for a session's private files |
| 3 | `f8f10e9` | `runners.<role>.bookkeeping` = `channel \| log`, and the interlock that stops a session going deaf |
| 4 | `10493b9` | `AppNote_Writer` — the one place the routing decision is made |
| 5–6 | `962732e` | the eight dispatcher sites route; the decider that reads `turn_ended` follows |
| 7–9 | `719fb3a` | the ledger advisories, the orphan report and the five coaching sites route |
| 11 | `fb24521` | the pack carries the notes still standing, and says when it truncated |
| 10 | `cc094b1` | the notes ride from the log too, and the cap holds over the pair |
| 12a | `8e77834` | `wake` and `bookkeeping` get their catalogue rows |
| 12b | `c00d581` | the role skills stop promising a channel that no longer carries the app's notes |
| — | `a3cc8c5` | a note that already rides the turn is not rendered as standing too (merge-time defect between 10 and 11) |
| 12c | *this branch* | the router's row on the pause register, the count reconciliation, the dead promise, this report |

**Task 8 shipped nothing, and that is the finding.** `Nudge_Wording.RESPAWN_SUBJECT` has no writer:
`Recover_OrphanedImplementer_Async`, the only thing that ever wrote it, was deleted. Re-verified here
on 2026-09-15 — a repo-wide grep finds the constant, its recogniser (`Is_WakeSubject`), the comment at
`BridgeEngineModel.cs:3480` recording the deletion, and three tests, one of which asserts its absence.
There was nothing to move.

## Task 12 — what this branch adds

### 1. The router's row on the pause register, and the argument that had to be checked first

`AppNote_Writer`'s type header carried a paragraph claiming it **is not a waker**, on two grounds: it
writes what its caller decided to write, and *"a caller that must be gated on the pause is gated where
it already is."*

**The first ground holds; the second did not, and the way it did not is the interesting part.** The
engine reaches the router through two adapters.

- `Route_SupervisorNote` is only reachable through `Append_SupervisorAttention_UnlessMeeting`, which
  asks `Is_Paused(orchId)` **above** its append. Covered, and a scan case already pins the ordering.
- `Route_ChannelNote` had **no pause screen anywhere on its own path.** Its five callers are the
  message-contract and question-dedup coaching, reached from `Mirror_Append_Async` (2) and
  `Send_QuestionWithButtons_Async` (3). `Is_Paused` had exactly one caller in the whole engine — the
  choke point — and none of these is below it.

They were nonetheless unreachable in a paused orchestration, **by a mechanism that never says the word
"pause"**: `EffectiveMode_Resolver.Resolve` answers `Deferred` when paused, `Freezes_Offsets` is true
for `Deferred`, and `Find_ActiveChannels` drops a frozen channel out of the tick — so the mirror never
reads the entry these notes would answer. A delivery-mode screen, two thousand lines away, standing in
for a pause screen that nobody had written. **That is a guard the next caller does not inherit**, and
it is precisely what the plan's step 1 anticipated ("if the five coaching sites are already only
reachable from a path that screened the pause, say so … and add the screen anyway").

Added, and the header paragraph is rewritten to say what is actually true rather than what was
convenient. The row:

```csharp
{ "bool Route_ChannelNote", "AppNote_Writer.Write", "Is_Paused(channel.OrchId)" },
```

**`TheWakers` gained a third column** — the spelling of the screen, per row. The five existing sweeps
hold a `session` in hand and read `session.Paused`; `Route_ChannelNote` holds only an orchestration id,
which is `Is_Paused`'s case and the choke point's. Forcing the uniform literal would have meant copying
that one-line accessor into a method with no session — a second copy of a rule (decision 12) written to
satisfy a string match in a test. Each row still pins exactly one literal, so no case can pass for two
reasons.

A second `[Fact]`, `TheChannelNoteRouter_AsksAboutThePause_BeforeItWritesAnything`, pins the ORDERING,
because the row can only see that the words are in the body.

**Mutation-proved.** With the screen removed, the row and the Fact fail and the other ten cases pass:

```
Failed … EveryWakerThatWritesToAChannel_SkipsAPausedOrchestration(signatureMark: "bool Route_ChannelNote", …)
Failed … TheChannelNoteRouter_AsksAboutThePause_BeforeItWritesAnything
Failed!  - Failed: 2, Passed: 10, Skipped: 0, Total: 12
```

Restored; green at 12.

**No live behaviour changes.** Every path to `Route_ChannelNote` was already unreachable while paused,
for the reason above, and all five callers discard the return value. This is braces on an existing belt,
and the value is that the belt is now named where the write is.

### 2. The two counts — reconciled at 78, and the briefing's premise was itself stale

The plan's prose says **77**; `AppNoteKinds.cs` and `AppNote_Writer.cs` say **78**. The census is the
authority and it says **78** — and it was already right, as were both C# files. **The stale document is
the PLAN**, plus one live artifact nobody had noticed: the census test's own method name read
`…IsOneOfTheClassifiedSeventySeven`.

Re-counted by hand as well as run, replicating the test's own rules (skip comment lines, skip
declarations via its `Route_\w+|Append_\w+|Announce` regex) over `AIOrchestratorCoreLib/`:

| append name | hits |
|---|---|
| `Append_OrchestrationAppEntry(` | 27 |
| `ChannelAppender.Append_AppEntry(` | 17 |
| `AppNote_Writer.Write(` | 10 |
| `Append_SupervisorAttention_UnlessMeeting(` | 9 |
| `Append_GeneralAppEntry(` | 9 |
| `Announce(` | 7 |
| `Route_ChannelNote(` | 5 |
| `Append_AppEntry_Safe(` | 2 |
| **total** | **86** |

86 − 8 helper bodies = **78 event sites**. The eight bodies: the four pass-through appenders, the choke
point, `Drain_PendingAnnouncements`, and the two router adapters. Constants unchanged
(`EXPECTED_EVENT_SITES = 78`, `HELPER_BODIES = 8`) — the plan's step 5 expected them to need moving and
they did not, because tasks 7–9 had already kept the register honest when they routed.

**The split, which step 5 asked for in writing:**

| | sites |
|---|---|
| reach `AppNote_Writer` — 8 in `PrintTurnDispatcherModel`, 5 via `Route_ChannelNote`, 4 via `routedKind` on the choke point | **17** |
| still call an appender directly | **61** |
| | **78** |

17 + 61 = 78. The 4 `routedKind` sites are the three PLAN.md advisories and the orphan report,
hand-counted at `BridgeEngineModel.cs:3017, 3562, 3643, 3731`.

Aligned: the census test's method name, a docstring on `EXPECTED_EVENT_SITES` carrying the split, and
the two places in the plan that state the count as a fact about the tree rather than as the instruction
they were at the time. **The plan's task-1 title and its embedded 2026-09-15 snippets are left at 77 on
purpose** — they are what that task was told to do, and a reader of `6a2cc7a` needs them to still say it.

This is decision 18 arriving from the other side, and it is worth writing down: the briefing that
dispatched this task named `AppNoteKinds.cs` as one of the two wrong numbers. It was not wrong. A
finding is true when it is written and it **expires without notice** — which is the same lesson plan 01's
report recorded about the marketplace registration, twice in one week.

### 3. The dead promise in the implementer skill

`kit/skills/implementer/SKILL.md` told every implementer that *"the idle nudge, the **orphan-respawn
notice**, and `GO AHEAD — resume`"* arrive as `FROM app` entries. The app has not respawned an orphaned
member since `Recover_OrphanedImplementer_Async` was deleted; there is no writer, only the constant and
its recogniser (re-verified above). Removed, and replaced with what actually happens: a nudged member
that stays silent is REPORTED to the supervisor, in the supervisor's channel, not the member's.

### 4. Three more per-role lists that were wrong about the same notice

Found while discharging point 5 below, and in scope because it is the same notice. `c00d581` wrote a
per-role paragraph naming exactly what each role stops seeing under `bookkeeping = log`. Checked site by
site against the eight dispatcher writes and their audiences:

| dispatcher site | audience |
|---|---|
| `turn_ended` (2126), superseded-final (1770), misaddressed reply (1887) | always `Agent` — **moves** |
| the five stall / usage-limit / deadline-kill notices | `Stall_Audience` → `Owner` for supervisor, solo and general; `Agent` for members |

So the superseded-final notice moves for **every** role, and the misaddressed one for every role that
can address a `TO:` block. The paragraphs omitted it three times: **solo** said only `turn_ended` moves
(three do), **general supervisor** omitted both, **supervisor** omitted the superseded one. Corrected.
The implementer and reviewer paragraphs were already right.

### 5. The seven "and tells you it did" sentences — verified, not assumed

Seven role reference files carry *"The bridge files the superseded one for you and tells you it did"*
(`implementer`, `reviewer`, `solo`, `general-supervisor`, `supervisor`, `communicator` print-runner
files, plus `supervisor/stream-runner.md`; an eighth variant is `implementer/SKILL.md:318`). Under
`bookkeeping = log` the second half became a claim about a file the session no longer reads. Walked, in
the branch source, in both halves:

- **"files the superseded one for you"** — `Write_SupersededFinals_Async` → `Write_Reply_Async` →
  `Append_Async` → `Append_SessionEntry_WithRetry_OrNull_Async`. An ordinary session-authored channel
  entry, **not routed**, identical in both regimes. Unaffected.
- **"and tells you it did"** — `Append_SupersededNotice` routes (`TurnMachinery`, `Agent`), so under
  `log` it lands in `status.jsonl`. It gets back to the session by two independent paths, and both are
  live on this branch:
  1. `StatusLog_Store.Append` applies the `[agent]` tag itself, so the record satisfies
     `PrintTurn_Trigger.Is_AgentNote` (author `app` + agent-tagged + not `turn_ended`);
     `WakeDecision_Resolver.With_AgentNotes` reads the log **whatever the sink says** and merges it with
     the channel into ONE call of `Select_AgentNotes`. The note rides the next turn's prompt.
  2. Task 11's `## What the app has told you` section of the state pack.

**True again, and the timing caveat is worth stating:** under `bookkeeping = log` the notice no longer
touches the channel, so under `wake = watcher` it no longer wakes anything by itself. It arrives WITH
the next turn rather than starting one. That was already true under `wake = ticket` before this plan
(`Is_Inbound` excludes `ChannelAuthors.App`), and `With_AgentNotes` returns early on an empty pending
set precisely so a note can never start a turn. The sentences promise that the session is told, not that
it is woken; they are true.

## Commands run, and their output

Only targeted `--filter` runs — the full suite is the dispatching session's, per the brief.

```
dotnet test … --filter "FullyQualifiedName~PauseGatesEveryWakerScanTests"
  Passed!  - Failed: 0, Passed: 12, Skipped: 0, Total: 12

dotnet test … --filter "…NotesRideFromTheStatusLogTests|…StatePackCarriesStandingNotesTests|
                        …AppNoteRoutingTests|…NothingMovedBecomesInvisible|…AppAuthoredWritesCensusTests"
  Passed!  - Failed: 0, Passed: 55, Skipped: 0, Total: 55

dotnet test … --filter "FullyQualifiedName~Tests.Kit"
  Passed!  - Failed: 0, Passed: 235, Skipped: 0, Total: 235

dotnet test … --filter "…QuestionContractProbeTests|…QuestionWaterfallProbeTests|
                        …APausedOrchestrationIsDormantTests|…AwaySuppressesAppAlertsScanTests|
                        …ContractCoaching|…NudgeDeciderTests"
  Passed!  - Failed: 0, Passed: 72, Skipped: 0, Total: 72

bash kit/hooks/watcher-behaviour-check.sh   →  44 checks, 0 failures
bash kit/hooks/hook-behaviour-check.sh      →  All cases match intent.
bash kit/self-write-suppression-check.sh    →  all cases passed
```

The kit failure plan 01's report recorded as pre-existing (`FP_ERR` per-file vs `"md5sum failed"`) has
since been FIXED; the bench is clean at 44/0 and that row should not be cited again.

**The full suite was not run from here** and no figure for it is claimed. The dispatching session's
baseline for this branch is 4 113 passed / 10 skipped / 0 failed.

## Acceptance, item by item

| # | criterion | state |
|---|---|---|
| 1 | full suite exits 0, no red outside the three flakes | **the dispatching session's run** — not claimed here |
| 2 | `grep "ChannelAppender.Append_AppEntry(" …/PrintTurnDispatcher/` returns nothing | **met** — returns nothing; all eight go through the router |
| 3 | census passes at the new count, report states the split | **met** — 78 / 17 + 61, above |
| 4 | under `bookkeeping: log`, no `[agent]` app entry in the channel for the routed kinds; they appear in `.status.jsonl` | **met at unit level** (`AppNoteRoutingTests`, `UnderTheLogSink_ARoutedAgentNoteLeavesTheChannel` + 7 more). NOT observed over a live full turn — see gap 1 |
| 5 | the next prompt still carries the notes; the pack carries `## What the app has told you` | **met** — `NotesRideFromTheStatusLogTests` (10 cases), `StatePackCarriesStandingNotesTests` (10 cases) |
| 6 | under default config, a channel after a turn is byte-for-byte identical to before this plan | **NOT measured.** True by construction — the router's default branch calls `ChannelAppender.Append_AppEntry` with the identical arguments — and pinned at unit level by `UnderTheDefaultSink_ARoutedNoteStillGoesToTheChannel`. The plan called this "the one worth measuring rather than asserting", and it is asserted. See gap 2 |
| 7 | `AnAppEntryStillCountsAsInbound_BecauseEscalationDependsOnIt` green, and `TheMemberNudgeIsNotRouted…` says why | **met** — both green in the runs above |

## The measurement

**Before — measured, and it is plan 01's figure, not a new one.** 9 days on the VPS, 20 orchestrations,
119 channels, live files and archives: 4 955 app-authored entries of 6 868 total wakes under the
fingerprint watcher (72 %). `wake_causes_under_watcher_measured` in `tools/wake-baseline/baseline.py`.

**The column beside it is MODELLED, not measured, and the script now says so in its own key names**
(`wake_causes_under_ticket_MODELLED`, `wakes_the_ticket_would_avoid_MODELLED`, `ours/integration`
`5b1c479`). It is what the app's policy WOULD have counted over the same entries, derived from the
author word; no ticket-mode machine produced it. A reader comparing the two columns is comparing a
measurement against a model, and the names are the only thing stopping that from being read as a
before/after.

**After — NOT TAKEN, and not takeable from here.** It needs `"bookkeeping": "log"` set for one role on a
machine that actually runs orchestrations, real traffic, about two days, and the script re-run:

```json
{ "runners": { "supervisor": { "runner": "print", "bookkeeping": "log" } } }
```

```
python3 tools/wake-baseline/baseline.py --root ~/.claude/supervision
```

## The boot read — plan 02's own figure: the instrument exists, the number does not yet

**This is what gap 3 used to be.** Plan 01's saving is *wakes*; this plan's is the *boot read* — a
session starts by reading *"`session.json` and every channel file in your home, top to bottom"*
(`kit/skills/supervisor/SKILL.md:131`), and this plan moves 17 kinds of app bookkeeping out of that
read. Nothing measured it. It does now.

**What was written** (branch `fix/close-measure`, worktree `AIOrchestrator-measure`, 2026-09-17 —
branch source, decision 18): `tools/wake-baseline/baseline.py` gained three sections and one scalar,
and `test_baseline.py` nine cases (2 → 11, all green).

| key | kind | what it holds |
|---|---|---|
| `boot_read_bytes_measured` | **measured** | live channel bytes, the app's share, bytes outside any entry, and the archived bytes that are NOT in a boot read |
| `boot_read_bookkeeping_MODELLED` | **modelled** | the 17 routed kinds by entry and by byte, and their share of the live bytes |
| `boot_read_tokens_ESTIMATE_at_3_87_bytes_per_token` | **estimate** | those bytes ÷ 3.87 |
| `bookkeeping_out_of_the_boot_read_MODELLED` | **modelled** | the plan's own scalar — *renamed from the plan's `bookkeeping_out_of_the_boot_read`*, because a classification made by a heuristic must say so in its key. That naming rule is this tool's, learned the hard way, and it is the rule this same report states two sections above |

**Three things the number is made of, and each is labelled rather than blended:**

1. **The bytes are measured** — read off the disk, on BYTES not characters (this prose is em dashes
   and accented words; a character count understates a UTF-8 channel by a few percent).
2. **The classification is MODELLED.** Which app entries get routed is decided in C# at the call site
   by `AppNoteKinds`, and nothing in a channel file records which site wrote an entry. The script
   matches author `app` + the `[agent]` prefix + one of 16 subject family words, each read off the
   constant that produces it. **The plan's own marker list was wrong on one:** it listed
   `"has gone deaf"` for the orphan report, which writes
   `OrphanEscalation_Decider.Describe_Report`'s `"<member> may be deaf to wakes"` — a marker that
   matched nothing, and a figure quietly too small. Corrected, and a test fails if it is put back.
3. **The token conversion is an `[estimate]`**: 3.87 bytes/token, measured on this repo's channel
   prose with real `-p` turns, replacing the 2.8–3.2 an earlier document guessed (which overstated
   derived token figures by 20–38 %). One constant, `BYTES_PER_TOKEN`, and every figure through it
   carries `ESTIMATE` in its key.

**One parser, and it is now fence-aware — which changes the wake columns too.** `read_entries` is the
only channel reader in the file, shared by the wake counts and the boot read, and it screens fenced
blocks the way `ChannelFence_Screen` does (only a CLOSED fence suppresses). For bytes this is
load-bearing: a fence-blind reader hands the tail of a supervisor's brief to `app` and inflates the
very share being measured. **Consequence to state plainly:** plan 01's 6 868 was taken with a
fence-blind reader, so a re-run may land below it — in the direction of removing phantom entries.

**The archive is deliberately on the other side of this reading.** The wake counts span live +
archive (decision 13). The boot read is LIVE ONLY, because a booting session reads the live file and
the archive is what the compactor took out of its way. Counting archived bytes into it would inflate
every long-running orchestration. Both readings are in the output, each naming which it is.

**Why the number is still not taken.** There are **no channel files on this machine**:
`~/.claude/supervision/` here holds `config.json`, `secrets.json` and `statusline.sh` and nothing else
(`find … -name 'channel*.md'` → 0). Run there, the script **exits 2 rather than reporting zero**
(decision 20) — the guard was added with this work, because a moved supervision home reads exactly
like a quiet machine.

**What it takes to make it real, and it needs no config change at all** — unlike the *after* above,
this is a BEFORE figure over traffic that already exists:

```
python3 tools/wake-baseline/baseline.py --root ~/.claude/supervision
```

on the VPS that produced plan 01's figures (9 days, 20 orchestrations, 119 channels). It is read-only
and aggregates-only. Read off it:

- `boot_read_tokens_ESTIMATE_…["median_per_orchestration"]` — what a supervisor's boot read costs;
- `boot_read_bookkeeping_MODELLED["share_of_live_bytes"]` — **the headline this plan lacks**: the
  fraction of that read this plan takes out of the channel;
- `boot_read_bytes_measured["archived_bytes_outside_the_boot_read"]` beside `live_bytes` — how much
  the compactor is already keeping out, which is the other half of the same story.

**And the honest caveat to carry with it:** the routed entries do not leave the *history*, they leave
the *channel* — under `bookkeeping = log` they land in `status.jsonl`, and task 10's merged read plus
task 11's pack hand the still-standing ones back to the session. So the share above is the gross
figure, and the net saving is it minus whatever the pack carries (capped at the newest five notes).
Nothing measures the pack's own size yet; gap 5 is about that.

## Gaps, stated

1. **A BASIC (solo) orchestration will never move its owner-channel bookkeeping**, and the gain does
   not reach those orchestrations. A solo shares the owner channel, so both engine adapters resolve the
   SUPERVISOR role, whose state file does not exist there; `BookkeepingSink_Policy` is handed null and
   keeps the channel. **Safe direction** — a note in a log nobody is handed would be a note nobody reads
   — and it changes nothing today. But a solo's ledger advisories, orphan report and question coaching
   stay in its channel whatever `runners.solo.bookkeeping` says. Its DISPATCHER notes (`turn_ended`,
   superseded-final, misaddressed) do move, because those sites carry the solo's own state. Noticed by
   task 7–9 (`719fb3a`), unresolved: tasks 11 and 12 were asked to decide and neither did.
2. **Acceptance criterion 6 is asserted where the plan asked for it to be measured.** No test compares a
   channel byte-for-byte across the change, and none can without a live turn; the construction argument
   and the unit case are what stands.
3. **Task 12 step 3 is DONE as of 2026-09-17** — the measuring function exists; **the figure itself is
   still not taken, because it needs live channels.** See *The boot read* above. Written on
   `fix/close-measure` (worktree `AIOrchestrator-measure`, head `f3f1dfe`), which HAS `5b1c479` as an
   ancestor — so it extends the `_MODELLED`-named copy this gap pointed at, and lands no conflicting
   third version. What the gap said until then, kept because it dates the hole: the function did not
   exist on any branch, and plan 02 had no headline figure of its own.
4. **The `.usage.json` locator stays parked** (decision 22, the owner's ruling of 2026-09-16, recorded in
   plan 01's report). Roughly 19 hand-built copies of the same path; not this plan's endeavour.
5. **The state pack has no global token budget.** The spec's ≤ 6 k is enforced nowhere as a TOTAL.
   `StatePack_Builder` holds nine per-section caps in CHARACTERS — brief 8 000, last-own 6 000, ledger
   6 000, PLAN 24 000, git 3 000, owner-tail 8 000, progress 6 000, conclusions 8 000, standing notes
   4 000 — summing to **73 000 characters**, roughly 18 k tokens, with nothing measuring the sum and
   the pending entries never cut at all. Every section can be inside its own limit and the pack still
   three times the spec's figure. Task 11 added the ninth cap and cites the 6 k budget in its own
   docstring as the reason for it, which is the closest anything comes to enforcing it.
6. **The kit was not verified against a restarted session.** `KitAssets_Bootstrapper` records its verdict
   at STARTUP, and neither the marketplace registration nor a diff on `kit/` says what verdict the
   running process holds (decision 17). What was verified: the branch source, and the three bash benches.
7. **`STATUS` stays in the channel**, contradicting the spec's own list. The plan's open question 1 puts
   it to the owner and assumes the answer is "leave it"; the owner has not answered. Its audience is
   `Owner`, it is mirrored with a named exception in `MirrorText_Formatter`, and moving it means
   reimplementing Normal/Deferred/Silenced outside the mirror.

## What this plan did NOT change

No channel format change — `ChannelAppender`, `channel-append.sh`, `ChannelWrite_Lock`, the compactor
and the entry header are untouched; what changed is which FILE some entries go to. No history is
migrated: every reader in this plan reads both the channel and the log, for the whole of the transition.
No Telegram change, and no owner-facing entry moved — `AppNote_Writer` refuses to route an `Owner`
audience, for all 78 sites at once, and `AnOwnerFacingNoteNeverLeavesTheChannel_WhateverTheKindAndWhateverTheSink`
is the oracle. The member nudge is not routed, because orphan escalation counts it.
`runners.<role>.bookkeeping` defaults to `channel`, so **no live session behaves differently until
somebody sets it.**

## The figure — taken 2026-09-17 on the VPS, read-only

`python3 tools/wake-baseline/baseline.py --root ~/.claude/supervision`, over 22 orchestrations,
124 live channel files and 24 archives.

| | |
|---|---|
| live boot read, all orchestrations | **6 318 979 bytes** ≈ 1 632 811 tokens `[estimate]` |
| of that, authored by the app | 746 541 bytes |
| **of that, the bookkeeping this plan moves** | **508 017 bytes · 1 469 entries** |
| **share of the live boot read** | **8.0 %** |
| **share of what the app writes** | **68 %** |
| in tokens `[estimate]` | ≈ **131 271** |
| median per orchestration | 32 930 bytes ≈ 8 509 tokens |

**Eight per cent, and that is the honest size of it.** Two thirds of what the app writes into the
channels does leave them; but the app is only twelve per cent of what a session reads at boot. The
rest is the conversation, which is what the channel is for.

**Measured, modelled and estimated, kept apart.** The bytes are measured off the disk. WHICH app
entry is bookkeeping is MODELLED — the decision is made in C# at the call site and nothing in a
channel file records which site wrote a line — so the classification is a 16-marker heuristic read
off the constants that emit the strings. Tokens are `[estimate]` at 3.87 bytes/token.

**And the share is GROSS, not net.** A routed entry leaves the channel, not the history: under
`bookkeeping = log` it lands in `status.jsonl` and the state pack hands back up to five of them. The
net is this figure minus what the pack returns, and nobody has measured the pack's own size yet
(gap 5 below).

**One reading that moved, and it is worth recording.** The same run reports 5 272 app-authored
entries against plan 01's 4 955 — but the window is two days longer and carries 22 orchestrations
against 20, while this reader is fence-aware where plan 01's was not. The two effects pull opposite
ways and the window dominates; neither number is directly comparable to the other, and a like-for-
like re-run has not been done.
