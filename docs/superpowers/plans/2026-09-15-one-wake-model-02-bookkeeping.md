# One Wake Model — Plan 02: bookkeeping out of the channels

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Take the app's own bookkeeping — `turn_ended`, the turn-machinery notices, the ledger advisories, the orphan note and the question/contract coaching — out of the channel files and into a per-session `status.jsonl`, so a session's boot read is the conversation and nothing else, while every one of those lines still reaches the session that needs it (through the state pack and the riding notes) and every line the owner sees today still reaches their phone.

**Architecture:** One router (`AppNote_Writer`) decides, per app-authored write, whether the entry goes to the channel or to the session's status log. The log stores each record as **the markdown entry the app would have appended**, so every existing reader — `ChannelEntry_Parser`, `ChannelEntry_Digest`, `PrintTurn_Trigger.Is_AgentNote`, `PrintTurnPrompt_Builder` — works on it unchanged. The notes keep riding the next turn (they are read from the log instead of the channel) and the state pack grows one section for the ones that are still standing. The whole series ships behind `runners.<role>.bookkeeping`, default `channel`, and the router refuses `log` for any session that does not get a state pack.

**Tech Stack:** C# / .NET 10 (`net10.0-windows` for the app, the lib is platform-neutral), xUnit, bash (kit skills), python3 (measurement script only).

**Spec:** `docs/superpowers/specs/2026-09-15-one-wake-model-design.md` — step 3, "Bookkeeping out of the channels".

**Depends on:** plan 01 (`2026-09-15-one-wake-model-01-ticket-and-decider.md`), shipped. In particular `WakeDecision_Resolver`, `SessionCursors_Bookkeeper`, `StatePack_Writer.Write_ForSession_OrNull` on both runners, and `runners.<role>.wake`.

---

## Global Constraints

- **Coding patterns:** `AIOrchestratorCoreLib` is STRICT — interface + `Model` + `_Factory` triples, immutable types, no mutable public state. New static helpers follow the shape of `Running/TurnLog/TurnLog_Store.cs` and `Channels/ChannelAppender.cs`, which are the two files this plan sits between.
- **No channel-format change anywhere in this plan.** `ChannelAppender`, `kit/bin/channel-append.sh`, `ChannelWrite_Lock`, `Channel_Compactor` and the entry header are untouched. What changes is WHICH FILE some entries are written to, never how one looks.
- **No migration of history.** Entries already in the channels stay in the channels and are still read from them. Every reader this plan touches reads BOTH the channel and the log for the whole of the transition — that is why Task 10's selector takes a list of sources rather than one.
- **The VPS runs 24/7 on bridge; the Windows machine runs the app on terminal.** Both must keep working after every task. `bookkeeping` defaults to `channel`, so nothing on either machine behaves differently until somebody sets it.
- **Additive first, deleting last.** Nothing is deleted in this plan. The channel-writing branch of every routed site stays in the code and stays the default.
- **The owner's phone is protected by construction, not by care.** `AppEntryAudiences.Owner` is the audience that reaches Telegram (`MirrorText_Formatter.Should_Mirror` refuses `Is_AgentTagged` entries and nothing else about the app). **The router never moves an `Owner` entry**, and Task 4 pins that with a test that fails if the branch is ever widened. Constraint 4 of the brief — "every entry the owner sees today must go on being seen" — is discharged there, once, mechanically.
- **Decision 12 — one copy of a rule.** The note-selection rule (2 h window, newest 5, not yet delivered) stays in `PrintTurn_Trigger.Select_AgentNotes` and grows a second SOURCE, never a second implementation. The per-role "where this session's private files live" rule is extracted in Task 2 so `turns.jsonl` and `status.jsonl` cannot drift apart.
- **Decision 13 — never count the live file.** Nothing in this plan compares an entry count across time. The log's own trimming (Task 2) is the same shape as `Channel_Compactor`'s and the cursor's prune absorbs it for the same reason.
- **Decision 15 — an alert the owner cannot act on does not go to Telegram.** Every entry this plan moves is already `AppEntryAudiences.Agent`, i.e. already invisible to the owner. This plan cannot violate decision 15; it can only make the channel quieter.
- **Decision 22 — the endeavour is what the owner asked for.** Findings that are not step 3 go to `## PARKED` in this file's own Open Questions section, not into a task.
- **Branch:** `feat/one-wake-model`, worktree `../AIOrchestrator-relations`. Stage explicit paths, never `git add -A` / `git add .`. Multi-line messages via `git commit -F <tempfile>`. Commits in English, `type(scope): a descriptive clause`.
- **Run the suite as** `~/.dotnet/dotnet test AIOrchestratorCoreLib.Tests/AIOrchestratorCoreLib.Tests.csproj`. `dotnet` is NOT on PATH on this Mac — it lives at `~/.dotnet/dotnet` (SDK 10.0.401). Start every shell with `export PATH="$HOME/.dotnet:$PATH"`.
- **A full run is ~3 min 20 s and a sub-agent's command budget is 120 s.** Every step below gives a `--filter` that runs in seconds. **Only the dispatching session runs the full suite**, and only at Task 12. Filter a class with `--filter "FullyQualifiedName~<ClassName>"`.
- **Baseline suite, measured 2026-09-15 at the end of plan 01: 3 955 passed / 10 skipped / 0 failed.** Any other red is yours.
- **Three tests flake under parallel load, each green 3/3 alone:** `ClosingTurnReviewFixTests.AClosingTurnThatSaysNothing…`, `TolerantFileReaderTests.AFileLockedExclusivelyForAMoment…`, `EffortDialOnABridgeDrivenSupervisorTests…`. Seeing one of these red in a full run is not a finding until it has failed 2/3 in isolation.
- **`PauseGatesEveryWakerScanTests` is a REGISTER.** Anything that writes into a channel must have a row there. This plan adds a writer (`AppNote_Writer`) that can write to a channel, so Task 12 adds its row. Do not skip it because "the router only re-routes" — the register's whole value is that it fails when somebody reasons that way.

---

## The classification — every app-authored write in this system

Counted on this worktree, `feat/one-wake-model`, 2026-09-15. **Source read: the branch source, not the build output and not the installed kit** (decision 18).

Reproduce the raw list with:

```bash
grep -rn --include='*.cs' \
  -e 'ChannelAppender.Append_AppEntry(' -e 'Append_GeneralAppEntry(' \
  -e 'Append_OrchestrationAppEntry(' -e 'Append_AppEntry_Safe(' \
  -e 'Append_SupervisorAttention_UnlessMeeting(' -e 'Announce(' \
  AIOrchestratorCoreLib/ | grep -v '/bin/\|/obj/'
```

**77 sites write an app-authored channel entry.** (75 raw hits of the five append names, minus 5 that are the bodies of the pass-through helpers `Append_GeneralAppEntry`, `Append_OrchestrationAppEntry`, `Append_AppEntry_Safe`, the choke point `Append_SupervisorAttention_UnlessMeeting`, and the retry transport `Drain_PendingAnnouncements`; plus the 7 sites that write through the `Announce` queue.)

| # | bucket | sites | audience | this plan |
|---|---|---|---|---|
| A | **Turn machinery** — `turn_ended`, stall alerts, usage-limit notices, deadline-kill warnings, superseded-final and misaddressed-reply coaching. All in `PrintTurnDispatcherModel`. | **8** | `turn_ended` and the two coaching notices are always `Agent`; the five stall/limit notices resolve `Owner` for supervisor/solo/general and `Agent` for members (`Stall_Audience`, `Resolve_StallAlertAudience`) | **MOVES** on the `Agent` branch (Tasks 5–6). The `Owner` branch stays in the channel. |
| B | **Ledger advisories** — `PLAN.md is behind your verdicts` (3548), `PLAN.md has lines that cannot show progress` (3628), `PLAN.md claims work that nobody is doing` (3715). | **3** | `Agent` | **MOVES** (Task 7) |
| C | **Orphan note** — `OrphanEscalation_Decider.Describe_Report` from `Nudge_IdleImplementers_Async` (3008). | **1** | `Agent` | **MOVES** (Task 8) |
| D | **Contract and question coaching** — message contract broken (13084), question incomplete and NOT sent (13112), earlier question superseded (13284), question already decided (13339), question already open (13363). | **5** | `Agent` | **MOVES** (Task 9) |
| E | **`STATUS`** — `Post_StatusEntry` (15711), whose only caller is `Push_AwayDigests_Async` (14799). | **1** | **`Owner`** | **STAYS.** See "What does not move", below. |
| F | **Owner-facing app entries** — request confirmations and failures (`start-orchestration`, `add-implementer`, `close-*`, `promote-*`, `set-model`, `/model`, `/effort`), question defaults, topic-delete failures, the wake-ticket stall alarm, the kit refusal. | **22** | `Owner` | **STAYS.** These are the owner's phone. |
| G | **Agent-facing conversation** — request rejections and holds the requester must act on, nudges (the member nudge whose non-answer starts the orphan clock, the idle-supervisor nudge, the two owner-is-waiting nudges), `/resume`'s GO AHEAD, the DND / AWAY-MODE on-off announcements, the malformed-header report, the merge ritual, the presence entries, the too-long-for-a-phone nudge, the guards-not-in-force marker, the idle-member flag, the kit recovery note. | **37** | `Agent` | **STAYS.** See "What does not move". |

**Moved: 17 sites** — 11 unconditionally, 6 only when their audience resolves to `Agent`.
**Unmoved: 60 sites** — 23 because they are the owner's (F + E), 37 because they are the conversation or a deliberate waker (G).

### What does not move, and why

1. **`STATUS` (bucket E) stays in the channel, contradicting the spec's own sentence.** Three reasons, all verified here on 2026-09-15:
   - Its audience is `Owner`. It is the away digest and it is **mirrored to Telegram** — `MirrorText_Formatter.Format_Parts` has a named exception for it (`Is_StatusEntry` ⇒ the BODY is sent, not just the subject). Moving it off the channel takes it off the owner's phone, which constraint 4 forbids outright.
   - `Post_StatusEntry`'s own docstring says it is written to the channel **precisely so the delivery modes handle it**: Normal mirrors, Deferred queues and collapses to the newest, Silenced drops. Moving it means reimplementing all three.
   - The half-hourly status the spec was written against **no longer exists** — the owner deleted it on 2026-09-09 and `Push_AwayDigests_Async` still carries its old name. What is left under the `STATUS` subject fires only while the owner is away and only when its content changed.
   The cost of leaving it is one app entry per away slot per orchestration, which under `wake = ticket` wakes nobody (`Is_Inbound` excludes `ChannelAuthors.App`). **Open question 1** puts this to the owner.

2. **Respawn notes have no writer.** `Nudge_Wording.RESPAWN_SUBJECT` exists and `Nudge_Wording.Is_WakeSubject` still recognises it, but `Recover_OrphanedImplementer_Async` — the only thing that ever wrote it — was deleted (the comment at `BridgeEngineModel.cs:3468` says so and says why). There is nothing to move. The constant stays where it is so entries already in members' channels are still recognised.

3. **The nudges stay, and a test says so.** `Nudge_Decider` (line 397) deliberately COUNTS agent-tagged app entries when it decides whether a member owes an answer, because the orphan escalation is the only proof a monitor is dead and can only run on a member that has already been nudged. Its own test is named `AnAppEntryStillCountsAsInbound_BecauseEscalationDependsOnIt` and its comment says it was left as a tripwire for exactly this change. Moving a nudge out of the channel breaks orphan escalation silently.

4. **Request confirmations stay** (brief constraint 5). CLAUDE.md decision 7 makes the `FROM app` confirmation a first-class entry that wakes the requester's watcher; 22 of the 36 are `Owner`-audience and are the owner's only receipt. The 14 agent-facing ones are instructions the requester must act on ("promotion REFUSED — file your HANDOVER entry first"), which is conversation, not bookkeeping.

5. **`/resume` and the AWAY/DND announcements stay.** `/resume` exists for the case where nothing else will ever speak to a session again; the away announcements change how a session behaves for hours. Both are the app speaking on the owner's behalf, and the channel is where the owner's words live.

---

## File Structure

**Created:**

| file | responsibility |
|---|---|
| `AIOrchestratorCoreLib/Running/SessionFiles/SessionFile_Locator.cs` | the one answer to "where does this session's private file live", shared by `turns.jsonl` and `status.jsonl` |
| `AIOrchestratorCoreLib/Channels/StatusLog/AppNoteKinds.cs` | the closed list of bookkeeping kinds, one per routed site |
| `AIOrchestratorCoreLib/Channels/StatusLog/StatusLog_Store.cs` | path, append, read-as-entries, trim |
| `AIOrchestratorCoreLib/Channels/StatusLog/AppNote_Writer.cs` | the router: channel or log, one decision, one place |
| `AIOrchestratorCoreLib/Channels/StatusLog/BookkeepingSinks.cs` | `Channel` \| `Log`, with the config words |
| `AIOrchestratorCoreLib/Channels/StatusLog/BookkeepingSink_Policy.cs` | resolves the sink for a role, and REFUSES `Log` where no pack is written |
| `AIOrchestratorCoreLib/Running/StatusNotes/StatusNotes_Bookkeeper.cs` | reads the log's notes and advances their cursor, through the existing rule |
| `AIOrchestratorCoreLib.Tests/Channels/AppAuthoredWritesCensusTests.cs` | the register: 77 sites, and a new one fails until it is classified |
| `AIOrchestratorCoreLib.Tests/Channels/StatusLogStoreTests.cs` | |
| `AIOrchestratorCoreLib.Tests/Channels/AppNoteRoutingTests.cs` | including the owner-never-moves oracle |
| `AIOrchestratorCoreLib.Tests/Running/NotesRideFromTheStatusLogTests.cs` | |
| `AIOrchestratorCoreLib.Tests/Running/StatePackCarriesStandingNotesTests.cs` | |
| `AIOrchestratorCoreLib.Tests/Bridge/NothingMovedBecomesInvisibleTests.cs` | every routed kind has a reader that still sees it |

**Modified:**

| file | change |
|---|---|
| `AIOrchestratorCoreLib/Running/TurnLog/TurnLog_Store.cs` | `Get_File` delegates to `SessionFile_Locator` |
| `AIOrchestratorCoreLib/Running/RoleRunnerConfig/IRoleRunnerConfig.cs`, `RoleRunnerConfigModel.cs`, `RoleRunnerConfig_Factory.cs` | `+ BookkeepingSinks Bookkeeping`, default `Channel` |
| `AIOrchestratorCoreLib/Running/RunnerConfigs/RunnerConfigs_Json.cs` | parse AND write `bookkeeping` |
| `AIOrchestratorCoreLib/Running/PrintTurnDispatcher/PrintTurnDispatcherModel.cs` | 8 sites route through `AppNote_Writer` |
| `AIOrchestratorCoreLib/Running/StallAlert_Decider.cs` | `Has_AlreadyReachedOwner` reads channel + log |
| `AIOrchestratorCoreLib/Bridge/BridgeEngine/BridgeEngineModel.cs` | 9 sites route through `AppNote_Writer` |
| `AIOrchestratorCoreLib/Running/PrintTurn_Trigger.cs` | `Select_AgentNotes` takes a delivered-predicate instead of one cursor |
| `AIOrchestratorCoreLib/Running/WakeDecision/WakeDecision_Resolver.cs` | `With_AgentNotes` merges channel notes and log notes |
| `AIOrchestratorCoreLib/Running/SessionCursors/SessionCursors_Bookkeeper.cs` | advances the `.status` cursor with the notes it handed over |
| `AIOrchestratorCoreLib/Running/StatePack/StatePackInputs.cs`, `StatePackInputs_Reader.cs`, `StatePack_Builder.cs` | `+ StandingNotes` section |
| `AIOrchestratorCoreLib.Tests/Bridge/PauseGatesEveryWakerScanTests.cs` | a row for the router |
| `tools/wake-baseline/baseline.py` | counts the bookkeeping entries a boot read no longer carries |
| `kit/skills/{supervisor,implementer,reviewer,solo,general-supervisor}/SKILL.md` | one sentence: the pack's notes section is where the app speaks to you |

---

## Task 1: The census — 77 sites, and a new one cannot hide

**Files:**
- Create: `AIOrchestratorCoreLib.Tests/Channels/AppAuthoredWritesCensusTests.cs`

**Interfaces:**
- Consumes: nothing.
- Produces: nothing in code. It is the register that makes the classification table above falsifiable, and it is Task 1 because every later task changes these counts and each must change them deliberately.

**Why a scan and not a type:** the five append names are reached through four wrappers and one queue; a compile-time registry would need every call site to pass a kind it does not have yet. The scan is the same shape and the same honesty as `PauseGatesEveryWakerScanTests` next door — it proves the list is the list, not that each entry is right.

- [ ] **Step 1: Write the failing test**

```csharp
// AIOrchestratorCoreLib.Tests/Channels/AppAuthoredWritesCensusTests.cs
using System.Text.RegularExpressions;
using Xunit;

namespace AIOrchestratorCoreLib.Tests.Channels;

/// <summary>
/// THE LIST OF PLACES THE APP WRITES INTO A CHANNEL, and it is the list itself.
///
/// <para>
/// The 2026-09-15 one-wake-model plan 02 classified every one of these into "bureaucracy that leaves
/// the channel" and "conversation that stays". A classification in a document is not a check: the
/// failure this file exists to catch is a SEVENTH ledger advisory, or a fourth question-coaching
/// notice, added next year straight onto <c>ChannelAppender</c> and never routed — which puts the
/// line back in every session's boot read with nothing saying so.
/// </para>
/// <para>
/// A COUNT AND NOT A WHITELIST OF LINE NUMBERS: line numbers move on every edit and a test that has
/// to be re-baselined on every edit is a test nobody reads. When this fails, read the plan's
/// classification table, decide which bucket the new site is in, route it or state why it stays, and
/// move the number here in the same commit.
/// </para>
/// </summary>
public class AppAuthoredWritesCensusTests
{
    /// <summary>
    /// Counted 2026-09-15 on `feat/one-wake-model`. Raw hits of the five append names plus the
    /// announcement queue, MINUS the five that are the bodies of the pass-through helpers
    /// (Append_GeneralAppEntry, Append_OrchestrationAppEntry, Append_AppEntry_Safe, the
    /// Append_SupervisorAttention_UnlessMeeting choke point, Drain_PendingAnnouncements).
    /// </summary>
    const int EXPECTED_EVENT_SITES = 77;

    const int HELPER_BODIES = 5;

    static readonly string[] APPEND_NAMES =
    [
        "ChannelAppender.Append_AppEntry(",
        "Append_GeneralAppEntry(",
        "Append_OrchestrationAppEntry(",
        "Append_AppEntry_Safe(",
        "Append_SupervisorAttention_UnlessMeeting(",
        "Announce(",
    ];

    [Fact]
    public void EveryPlaceTheAppWritesAChannelEntry_IsOneOfTheClassifiedSeventySeven()
    {
        var hits = 0;

        foreach (var file in SourceTree.EnumerateCSharp("AIOrchestratorCoreLib"))
        {
            foreach (var line in File.ReadLines(file))
            {
                var trimmed = line.TrimStart();

                if (trimmed.StartsWith("//", StringComparison.Ordinal))
                    continue;

                // A DECLARATION IS NOT A CALL. The four wrappers declare themselves with these very
                // names; counting a declaration would make the total drift by exactly the number of
                // wrappers every time one is renamed, which is the kind of noise that gets a register
                // deleted.
                if (Regex.IsMatch(line, @"^\s*(?:bool|void|static|async|public|private|internal)\b.*\b(?:Append_\w+|Announce)\s*\("))
                    continue;

                foreach (var name in APPEND_NAMES)
                {
                    if (line.Contains(name, StringComparison.Ordinal))
                    {
                        hits++;
                        break;
                    }
                }
            }
        }

        Assert.Equal(EXPECTED_EVENT_SITES + HELPER_BODIES, hits);
    }
}
```

- [ ] **Step 2: Run it and watch it fail**

Run: `~/.dotnet/dotnet test AIOrchestratorCoreLib.Tests/AIOrchestratorCoreLib.Tests.csproj --filter "FullyQualifiedName~AppAuthoredWritesCensusTests"`
Expected: compile error — `SourceTree` does not exist.

- [ ] **Step 3: Write the source-tree helper**

`PauseGatesEveryWakerScanTests` already finds `BridgeEngineModel.cs` by walking up from the test assembly; read its `Extract_Method` helper first and put the shared walk beside it if there is already one. If there is not, create:

```csharp
// AIOrchestratorCoreLib.Tests/SourceTree.cs
namespace AIOrchestratorCoreLib.Tests;

/// <summary>
/// The repo's own source, found from the test assembly's location. Scan tests read the SOURCE, which
/// is the point: they assert about code shape, not about behaviour, and there is nothing to run.
/// A walk that cannot find the tree THROWS rather than returning nothing — decision 20: a harness
/// that cannot find what it tests must refuse to run, never certify an absence it never looked at.
/// </summary>
public static class SourceTree
{
    public static string Root
    {
        get
        {
            var directory = new DirectoryInfo(AppContext.BaseDirectory);

            while (directory != null && !File.Exists(Path.Combine(directory.FullName, "AIOrchestrator.slnx")))
                directory = directory.Parent;

            return directory?.FullName
                ?? throw new Exception($"Could not find the repo root above '{AppContext.BaseDirectory}' — this scan cannot judge code it has not read.");
        }
    }

    public static IEnumerable<string> EnumerateCSharp(string relativeFolder)
    {
        var folder = Path.Combine(Root, relativeFolder);

        if (!Directory.Exists(folder))
            throw new Exception($"'{folder}' does not exist — this scan cannot judge code it has not read.");

        foreach (var file in Directory.EnumerateFiles(folder, "*.cs", SearchOption.AllDirectories))
        {
            if (file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
                continue;

            if (file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
                continue;

            yield return file;
        }
    }
}
```

- [ ] **Step 4: Run it and watch it pass**

Run: `~/.dotnet/dotnet test AIOrchestratorCoreLib.Tests/AIOrchestratorCoreLib.Tests.csproj --filter "FullyQualifiedName~AppAuthoredWritesCensusTests"`
Expected: PASS, 1 test. **If the count is not 82 (77 + 5), do not adjust the constant to whatever you got.** Re-run the `grep` at the head of this plan, list the extra or missing lines, and record them in the report — the plan was counted on a specific commit and a difference is a finding.

- [ ] **Step 5: Commit**

```bash
git add AIOrchestratorCoreLib.Tests/Channels/AppAuthoredWritesCensusTests.cs AIOrchestratorCoreLib.Tests/SourceTree.cs
git commit -F /tmp/cm.txt   # "test(channels): the seventy-seven places the app writes a channel entry are a register, not a paragraph"
```

---

## Task 2: `status.jsonl` — the store, and one locator for a session's private files

**Files:**
- Create: `AIOrchestratorCoreLib/Running/SessionFiles/SessionFile_Locator.cs`
- Create: `AIOrchestratorCoreLib/Channels/StatusLog/StatusLog_Store.cs`
- Modify: `AIOrchestratorCoreLib/Running/TurnLog/TurnLog_Store.cs`
- Test: `AIOrchestratorCoreLib.Tests/Channels/StatusLogStoreTests.cs`

**Interfaces:**
- Produces: `StatusLog_Store.Get_File(paths, role, orchId, memberId)`, `.Append(logFile, subject, body, nowLocal)`, `.Read_Entries(logFile)` → `IReadOnlyList<IChannelEntry>`. Tasks 4–10 all read or write through these.

**The design in one line:** a record stores **the markdown entry the app would have appended**, so `ChannelEntry_Parser`, `ChannelEntry_Digest`, `Is_AgentNote` and the prompt builder all work on it with no change at all.

- [ ] **Step 1: Write the failing test**

```csharp
// AIOrchestratorCoreLib.Tests/Channels/StatusLogStoreTests.cs
using AIOrchestratorCoreLib.Channels;
using AIOrchestratorCoreLib.Channels.StatusLog;
using Xunit;

namespace AIOrchestratorCoreLib.Tests.Channels;

/// <summary>
/// THE LOG IS CHANNEL-SHAPED ON PURPOSE. Every reader in this system already knows how to read a
/// channel entry — the parser, the digest that identifies one, <c>Is_AgentNote</c>, the prompt
/// builder. A record that stored "subject" and "body" as loose fields would have needed a second
/// implementation of each of those, which is decision 12 four times over. So the record carries the
/// rendered entry and <see cref="StatusLog_Store.Read_Entries"/> hands back exactly what
/// <c>ChannelEntry_Parser.Parse_All</c> hands back for a channel file.
/// </summary>
public class StatusLogStoreTests
{
    static string TempFile() => Path.Combine(Path.GetTempPath(), Path.GetRandomFileName() + ".jsonl");

    static readonly DateTime NOW = new(2026, 9, 15, 14, 30, 0, DateTimeKind.Local);

    [Fact]
    public void ARecord_ReadsBackAsAnAgentTaggedAppEntry()
    {
        var file = TempFile();

        StatusLog_Store.Append(file, "turn_ended imp-1 turn 4 — success", "request_id: repo-1/imp-1/4", NOW);

        var entries = StatusLog_Store.Read_Entries(file);
        var entry = Assert.Single(entries);

        Assert.Equal(ChannelAuthors.App, entry.Author);
        Assert.Equal("[agent] turn_ended imp-1 turn 4 — success", entry.Subject);
        Assert.Equal("2026-09-15 14:30", entry.DateText);
        Assert.Contains("request_id: repo-1/imp-1/4", entry.Body, StringComparison.Ordinal);
        Assert.True(Running.PrintTurn_Trigger.Is_AgentNote(entry) is false or true); // compiles: the parser produced a real entry

        File.Delete(file);
    }

    /// <summary>
    /// ORDER IS THE FILE'S ORDER, and the index is diagnostic. It is written so a human reading the
    /// file sees the same shape as a channel; nothing decides anything from it (decision 12).
    /// </summary>
    [Fact]
    public void RecordsAreReadBackInTheOrderTheyWereWritten_AndAreNumbered()
    {
        var file = TempFile();

        StatusLog_Store.Append(file, "first", "a", NOW);
        StatusLog_Store.Append(file, "second", "b", NOW.AddMinutes(1));
        StatusLog_Store.Append(file, "third", "c", NOW.AddMinutes(2));

        var entries = StatusLog_Store.Read_Entries(file);

        Assert.Equal(["[agent] first", "[agent] second", "[agent] third"], entries.Select(entry => entry.Subject));
        Assert.Equal([1, 2, 3], entries.Select(entry => entry.Index));

        File.Delete(file);
    }

    /// <summary>
    /// A BODY WITH A CHANNEL HEADER IN IT DOES NOT BECOME TWO ENTRIES. The app quotes channel text
    /// back at sessions constantly — the malformed-header report quotes offending lines verbatim —
    /// and a naive "concatenate the raw texts and parse" would split a record in half. The JSON
    /// encoding is what makes the file safe to write; this pins that the READ side is too.
    /// </summary>
    [Fact]
    public void ABodyContainingAChannelHeader_IsStillOneRecord()
    {
        var file = TempFile();

        StatusLog_Store.Append(file, "3 entries are INVISIBLE", "## [7] FROM sup — 2026-09-14 10:00 — a malformed one\nand its body", NOW);

        var entry = Assert.Single(StatusLog_Store.Read_Entries(file));

        Assert.Equal("[agent] 3 entries are INVISIBLE", entry.Subject);

        File.Delete(file);
    }

    /// <summary>
    /// BOUNDED, like every other file this system keeps beside a session. Past the cap the oldest
    /// records are dropped, which is exactly what <c>Channel_Compactor</c> does to a channel and what
    /// the cursor's prune already absorbs — decision 13's rule is that nothing may COUNT this file
    /// across time, and nothing does.
    /// </summary>
    [Fact]
    public void ThePastIsTrimmed_NewestKept()
    {
        var file = TempFile();

        for (var index = 1; index <= StatusLog_Store.MAX_RECORDS + 20; index++)
            StatusLog_Store.Append(file, $"note {index}", "body", NOW);

        var entries = StatusLog_Store.Read_Entries(file);

        Assert.Equal(StatusLog_Store.MAX_RECORDS, entries.Count);
        Assert.Equal($"[agent] note {StatusLog_Store.MAX_RECORDS + 20}", entries[^1].Subject);

        File.Delete(file);
    }

    /// <summary>
    /// A LOG THAT CANNOT BE READ IS EMPTY, NEVER AN EXCEPTION. Losing a bookkeeping line must never
    /// be the reason a turn does not start — the same ruling <c>StatePack_Writer</c> makes about the
    /// pack, and for the same reason.
    /// </summary>
    [Fact]
    public void AnUnreadableLogReadsAsEmpty()
    {
        Assert.Empty(StatusLog_Store.Read_Entries(Path.Combine(Path.GetTempPath(), "no-such-folder-" + Guid.NewGuid(), "status.jsonl")));
    }
}
```

- [ ] **Step 2: Run it and watch it fail**

Run: `~/.dotnet/dotnet test AIOrchestratorCoreLib.Tests/AIOrchestratorCoreLib.Tests.csproj --filter "FullyQualifiedName~StatusLogStoreTests"`
Expected: compile error — `StatusLog_Store` does not exist.

- [ ] **Step 3: Extract the locator**

```csharp
// AIOrchestratorCoreLib/Running/SessionFiles/SessionFile_Locator.cs
using AIOrchestratorCoreLib.Running.PrintSessionState;
using AIOrchestratorCoreLib.SupervisionPaths;

namespace AIOrchestratorCoreLib.Running.SessionFiles;

/// <summary>
/// WHERE A SESSION'S PRIVATE FILES LIVE — one answer, reached from the one place that already had to
/// know: <see cref="PrintSessionState_Store.Get_StateFile"/>.
///
/// <para>
/// The rule is not "the member's folder". A member and a solo get a folder of their own; the
/// SUPERVISOR and the COMMUNICATOR share the orchestration folder and are told apart by a
/// <c>.supervisor.</c> / <c>.communicator.</c> prefix on the file name; the general supervisor sits
/// in its own folder. <c>TurnLog_Store.Get_File</c> already re-derived that prefix by string-editing
/// the state file's name, and <c>status.jsonl</c> would have been the second copy of the same
/// derivation — so it is here once and both call it (CLAUDE.md decision 12).
/// </para>
/// </summary>
public static class SessionFile_Locator
{
    /// <summary>
    /// The session's own file named <paramref name="fileName"/>, beside its state file and carrying
    /// the same role prefix, so two roles sharing a folder never share a file.
    /// </summary>
    public static string Get_File(ISupervisionPaths paths, SessionRoles role, string orchId, string memberId, string fileName)
    {
        var stateFile = PrintSessionState_Store.Get_StateFile(paths, role, orchId, memberId);
        var folder = Path.GetDirectoryName(stateFile) ?? paths.Root;
        var prefix = Path.GetFileName(stateFile).Replace(PrintSessionState_Store.STATE_FILE_NAME, string.Empty);

        return Path.Combine(folder, prefix + fileName);
    }
}
```

Then in `TurnLog_Store.cs` replace the body of `Get_File` with:

```csharp
    public static string Get_File(ISupervisionPaths paths, SessionRoles role, string orchId, string memberId)
    {
        return SessionFiles.SessionFile_Locator.Get_File(paths, role, orchId, memberId, FILE_NAME);
    }
```

and delete the three local lines it replaces, keeping the comment about the role prefix as a `<see cref>` to the locator.

- [ ] **Step 4: Write the store**

```csharp
// AIOrchestratorCoreLib/Channels/StatusLog/StatusLog_Store.cs
using System.Text.Json;
using System.Text.Json.Nodes;
using AIOrchestratorCoreLib.Channels.ChannelEntry;
using AIOrchestratorCoreLib.Running;
using AIOrchestratorCoreLib.Running.SessionFiles;
using AIOrchestratorCoreLib.Storage;
using AIOrchestratorCoreLib.SupervisionPaths;

namespace AIOrchestratorCoreLib.Channels.StatusLog;

/// <summary>
/// THE APP'S BOOKKEEPING ABOUT ONE SESSION, out of its channel. Measured on the VPS over nine days
/// (2026-09-15, plan 01's report): 4 955 app-authored entries against 1 122 from members and 714 from
/// the owner, and **zero turns in the whole window were started by an app entry** — yet every session
/// re-read all of them at boot. This file is where the ones nobody has to answer go instead.
///
/// <para>
/// IT IS NOT A SECOND CHANNEL AND NOT A SECOND LOG. <c>orchestrator.log.jsonl</c> is the operator's
/// record of the orchestration and <c>turns.jsonl</c> is the transcript of one session's turns; this
/// is the set of statements the app has made TO one session. The channel remains the human-readable
/// register and the wake mechanism (2026-09-15 one-wake-model spec, §3.4).
/// </para>
/// <para>
/// A RECORD CARRIES THE ENTRY THE APP WOULD HAVE APPENDED, rendered exactly as
/// <see cref="ChannelAppender"/> renders one, so every reader downstream is the one that already
/// exists: <see cref="ChannelEntry_Parser"/> parses it, <see cref="ChannelEntry_Digest"/> identifies
/// it, <see cref="PrintTurn_Trigger.Is_AgentNote"/> recognises it and
/// <c>PrintTurnPrompt_Builder</c> renders it. Storing loose fields instead would have meant a second
/// implementation of each (decision 12). The JSON envelope is what makes a body containing a channel
/// header safe to store — a plain concatenation would split such a record in two.
/// </para>
/// <para>
/// EVERY METHOD SWALLOWS ITS I/O. Losing a bookkeeping line is never a reason to lose a turn — the
/// same ruling <see cref="Running.StatePack.StatePack_Writer"/> makes about the pack. A log that
/// cannot be read reads as empty and the session falls back to what it had before this existed.
/// </para>
/// </summary>
public static class StatusLog_Store
{
    public const string FILE_NAME = "status.jsonl";

    /// <summary>
    /// The cursor key the notes of this log are delivered under, in the session's state file. It
    /// begins with a dot so it can never collide with a member id or with
    /// <c>TurnSource_Factory.OWNER_KEY</c> — a spoke is named by a word an agent typed.
    /// </summary>
    public const string CURSOR_KEY = ".status";

    /// <summary>
    /// How many records are kept. Above this the file is rewritten with the newest
    /// <see cref="MAX_RECORDS"/>, which is what <see cref="Channel_Compactor"/> does to a channel and
    /// what the cursor's prune already absorbs. Sized so that a session's whole 2 h note window
    /// (<see cref="PrintTurn_Trigger.AGENT_NOTE_WINDOW"/>) cannot fall out of it at any rate this
    /// system has ever produced: the busiest channel measured on the VPS wrote 4 955 app entries over
    /// nine days across 119 channels.
    /// </summary>
    public const int MAX_RECORDS = 400;

    const string INDEX_KEY = "i";
    const string STAMP_KEY = "at";
    const string SUBJECT_KEY = "subject";
    const string BODY_KEY = "body";

    public static string Get_File(ISupervisionPaths paths, SessionRoles role, string orchId, string memberId)
    {
        return SessionFile_Locator.Get_File(paths, role, orchId, memberId, FILE_NAME);
    }

    /// <summary>
    /// Appends one record. Returns whether it landed — the callers that record state on the strength
    /// of a note must check it, exactly as they check <see cref="ChannelAppender.Append_AppEntry"/>.
    /// </summary>
    public static bool Append(string logFile, string subject, string body, DateTime nowLocal)
    {
        try
        {
            var folder = Path.GetDirectoryName(logFile);

            if (!string.IsNullOrEmpty(folder))
                Directory.CreateDirectory(folder);

            var record = new JsonObject
            {
                [INDEX_KEY] = Count_Records(logFile) + 1,
                [STAMP_KEY] = nowLocal.ToString("yyyy-MM-dd HH:mm"),
                [SUBJECT_KEY] = AppEntryAudience_Tag.Apply(subject, AppEntryAudiences.Agent),
                [BODY_KEY] = body.Trim(),
            };

            File.AppendAllText(logFile, record.ToJsonString() + "\n");
            Trim_IfNeeded(logFile);

            return true;
        }
        catch
        {
            // Swallowed by design — see the type header.
            return false;
        }
    }

    /// <summary>
    /// The log's records as channel entries, oldest first. Empty for a log that is absent, empty or
    /// unreadable — never an exception (decision 21: a reader that cannot evaluate its input says so
    /// by answering nothing, and the caller's fallback is what the system did before this existed).
    /// </summary>
    public static IReadOnlyList<IChannelEntry> Read_Entries(string logFile)
    {
        try
        {
            if (!File.Exists(logFile))
                return [];

            List<IChannelEntry> entries = [];

            foreach (var line in File.ReadLines(logFile))
            {
                var entry = Parse_OrNull(line);

                if (entry != null)
                    entries.Add(entry);
            }

            return entries;
        }
        catch
        {
            return [];
        }
    }

    /// <summary>
    /// A line that is not a record is SKIPPED, not fatal. The file is appended to by one process, but
    /// a crash mid-write can leave a partial last line, and one torn line must not blind a session to
    /// the four hundred good ones above it.
    /// </summary>
    static IChannelEntry? Parse_OrNull(string line)
    {
        if (string.IsNullOrWhiteSpace(line))
            return null;

        try
        {
            if (JsonNode.Parse(line) is not JsonObject record)
                return null;

            var index = record[INDEX_KEY]?.GetValue<int>() ?? 0;
            var stamp = record[STAMP_KEY]?.GetValue<string>();
            var subject = record[SUBJECT_KEY]?.GetValue<string>();
            var body = record[BODY_KEY]?.GetValue<string>() ?? string.Empty;

            if (stamp == null || subject == null)
                return null;

            // RENDERED THE WAY ChannelAppender RENDERS ONE, then parsed by the parser that reads a
            // channel. Building an IChannelEntry by hand here would be a second implementation of the
            // entry's shape, and the one thing this file must never do is disagree with the appender.
            var text = $"## [{index}] FROM app — {stamp} — {subject}\n\n{body}\n";

            return ChannelEntry_Parser.Parse_All(text).FirstOrDefault();
        }
        catch (JsonException)
        {
            return null;
        }
    }

    static int Count_Records(string logFile)
    {
        // The INDEX ONLY, and it is diagnostic (decision 12: nothing decides delivery from a number).
        // Identity is the digest, exactly as it is for a channel entry.
        return File.Exists(logFile) ? File.ReadLines(logFile).Count(line => !string.IsNullOrWhiteSpace(line)) : 0;
    }

    static void Trim_IfNeeded(string logFile)
    {
        var lines = File.ReadAllLines(logFile).Where(line => !string.IsNullOrWhiteSpace(line)).ToList();

        if (lines.Count <= MAX_RECORDS)
            return;

        // ATOMIC, so a session reading while this runs sees the old file or the new one, never half
        // of each — the same guarantee StatePack_Writer gives the pack.
        Atomic_FileWriter.Write_AllText(logFile, string.Join('\n', lines.Skip(lines.Count - MAX_RECORDS)) + "\n");
    }
}
```

- [ ] **Step 5: Run the test and watch it pass**

Run: `~/.dotnet/dotnet test AIOrchestratorCoreLib.Tests/AIOrchestratorCoreLib.Tests.csproj --filter "FullyQualifiedName~StatusLogStoreTests"`
Expected: PASS, 5 tests.

- [ ] **Step 6: Run `TurnLog`'s own oracle — the locator refactor must be invisible**

Run: `~/.dotnet/dotnet test AIOrchestratorCoreLib.Tests/AIOrchestratorCoreLib.Tests.csproj --filter "FullyQualifiedName~TurnLog"`
Expected: PASS, unchanged. **A supervisor's `turns.jsonl` moving from `.supervisor.turns.jsonl` to `turns.jsonl` would silently merge two roles' logs**, which is the one way this refactor can be wrong.

- [ ] **Step 7: Commit**

```bash
git add AIOrchestratorCoreLib/Running/SessionFiles/ AIOrchestratorCoreLib/Channels/StatusLog/StatusLog_Store.cs AIOrchestratorCoreLib/Running/TurnLog/TurnLog_Store.cs AIOrchestratorCoreLib.Tests/Channels/StatusLogStoreTests.cs
git commit -F /tmp/cm.txt   # "feat(channels): a per-session status log that stores the entry the app would have appended"
```

---

## Task 3: `bookkeeping` — the gate, and the interlock that stops a session going deaf

**Files:**
- Create: `AIOrchestratorCoreLib/Channels/StatusLog/BookkeepingSinks.cs`
- Create: `AIOrchestratorCoreLib/Channels/StatusLog/BookkeepingSink_Policy.cs`
- Modify: `AIOrchestratorCoreLib/Running/RoleRunnerConfig/IRoleRunnerConfig.cs`, `RoleRunnerConfigModel.cs`, `RoleRunnerConfig_Factory.cs`
- Modify: `AIOrchestratorCoreLib/Running/RunnerConfigs/RunnerConfigs_Json.cs`
- Test: `AIOrchestratorCoreLib.Tests/Running/BookkeepingSinkConfigTests.cs`

**Interfaces:**
- Produces: `BookkeepingSinks { Channel, Log }`, `BookkeepingSink_Names`, `IRoleRunnerConfig.Bookkeeping`, and `BookkeepingSink_Policy.Resolve(roleConfig, state)`. Task 4 is its only consumer.

**The interlock:** the spec says step 3 is safe only after step 2, "because until then the terminal watcher's only knowledge of anything is the channel file". That is a per-session fact, not a per-release one: a TERMINAL session in `wake = watcher` gets no state pack and no riding notes, so a note in the log would reach it never. The policy refuses `Log` for exactly that combination — which means the gate cannot be misconfigured into silence.

- [ ] **Step 1: Write the failing test**

```csharp
// AIOrchestratorCoreLib.Tests/Running/BookkeepingSinkConfigTests.cs
using System.Text.Json.Nodes;
using AIOrchestratorCoreLib.Channels.StatusLog;
using AIOrchestratorCoreLib.Running;
using AIOrchestratorCoreLib.Running.RoleRunnerConfig;
using AIOrchestratorCoreLib.Running.RunnerConfigs;
using Xunit;

namespace AIOrchestratorCoreLib.Tests.Running;

/// <summary>
/// THE GATE IS OFF UNTIL SOMEBODY SAYS OTHERWISE, and the one configuration that would make a
/// session deaf is refused rather than trusted to the person editing the file.
/// </summary>
public class BookkeepingSinkConfigTests
{
    static IRunnerConfigs Parse(string json) => RunnerConfigs_Json.Parse(JsonNode.Parse(json) as JsonObject);

    [Fact]
    public void ARoleThatStatesNothing_KeepsItsBookkeepingInTheChannel()
    {
        Assert.Equal(BookkeepingSinks.Channel, Parse("{}").Get_ForRole(SessionRoles.Supervisor).Bookkeeping);
    }

    /// <summary>
    /// AND THE REST OF THE NODE IS STILL READ. Asserting only that the sink is Channel passes for two
    /// reasons — the word fell back, or nothing parses `bookkeeping` at all — and an assertion with
    /// two routes to its state pins neither (CLAUDE.md decision 20).
    /// </summary>
    [Fact]
    public void AnUnknownWord_FallsBackToTheChannel_WithoutCostingTheRestOfTheNode()
    {
        var role = Parse("""{"runners":{"supervisor":{"runner":"print","resume":"fresh","wake":"ticket","bookkeeping":"telepathy"}}}""")
            .Get_ForRole(SessionRoles.Supervisor);

        Assert.Equal(BookkeepingSinks.Channel, role.Bookkeeping);
        Assert.Equal(SessionRunners.Print, role.Runner);
        Assert.Equal(ResumeModes.Fresh, role.Resume);
        Assert.Equal(WakeModes.Ticket, role.Wake);
    }

    /// <summary>
    /// IT SURVIVES A SAVE. <c>RunnerConfigs_Json.Write</c> runs on every save of config.json, and a
    /// key it does not emit is returned to its default by the app's own next write — which is how an
    /// explicit <c>"wake": "ticket"</c> was silently reverted before plan 01's Task 2 fixed it. The
    /// same trap, one key later.
    /// </summary>
    [Fact]
    public void AnExplicitSink_SurvivesASaveAndAReload()
    {
        var parsed = Parse("""{"runners":{"supervisor":{"runner":"print","bookkeeping":"log"}}}""");
        var written = new JsonObject();

        RunnerConfigs_Json.Write(written, parsed);

        Assert.Equal(BookkeepingSinks.Log, RunnerConfigs_Json.Parse(written).Get_ForRole(SessionRoles.Supervisor).Bookkeeping);
    }

    /// <summary>
    /// THE INTERLOCK. A terminal session in watcher mode gets no state pack and no riding notes — its
    /// only knowledge of anything is its channel file (2026-09-15 spec, step 3: "safe only after step
    /// 2"). Honouring <c>log</c> there would put the ledger advisory, the orphan report and the
    /// "your question was NOT sent" notice in a file nothing ever reads, and the session would look
    /// exactly like one that had nothing to be told.
    /// </summary>
    [Fact]
    public void ATerminalWatcherSession_IsRefusedTheLog_EvenWhenTheConfigAsksForIt()
    {
        var role = Parse("""{"runners":{"implementer":{"runner":"terminal","wake":"watcher","bookkeeping":"log"}}}""")
            .Get_ForRole(SessionRoles.Implementer);

        Assert.Equal(BookkeepingSinks.Log, role.Bookkeeping);
        Assert.Equal(BookkeepingSinks.Channel, BookkeepingSink_Policy.Resolve(role));
    }

    [Fact]
    public void ATerminalTicketSession_AndEveryBridgeSession_MayUseTheLog()
    {
        var terminal = Parse("""{"runners":{"implementer":{"runner":"terminal","wake":"ticket","bookkeeping":"log"}}}""")
            .Get_ForRole(SessionRoles.Implementer);
        var bridge = Parse("""{"runners":{"implementer":{"runner":"print","bookkeeping":"log"}}}""")
            .Get_ForRole(SessionRoles.Implementer);

        Assert.Equal(BookkeepingSinks.Log, BookkeepingSink_Policy.Resolve(terminal));
        Assert.Equal(BookkeepingSinks.Log, BookkeepingSink_Policy.Resolve(bridge));
    }
}
```

- [ ] **Step 2: Run it and watch it fail**

Run: `~/.dotnet/dotnet test AIOrchestratorCoreLib.Tests/AIOrchestratorCoreLib.Tests.csproj --filter "FullyQualifiedName~BookkeepingSinkConfigTests"`
Expected: compile error — `BookkeepingSinks` and `IRoleRunnerConfig.Bookkeeping` do not exist.

- [ ] **Step 3: Write the enum and its words**

```csharp
// AIOrchestratorCoreLib/Channels/StatusLog/BookkeepingSinks.cs
namespace AIOrchestratorCoreLib.Channels.StatusLog;

/// <summary>
/// WHERE THE APP'S OWN BOOKKEEPING ABOUT A SESSION IS WRITTEN. <see cref="Channel"/>: into the
/// session's channel, where it has always gone and where it is re-read at every boot.
/// <see cref="Log"/>: into <see cref="StatusLog_Store"/>, shown to the session through its state pack
/// and its riding notes.
///
/// <para>
/// IT NEVER GOVERNS AN OWNER-FACING ENTRY. <see cref="AppEntryAudiences.Owner"/> is what reaches
/// Telegram, and the router refuses to move one whatever this says — the channel is the only route to
/// the mirror. This key decides where AGENT-facing bookkeeping goes and nothing else.
/// </para>
/// <para>
/// Orthogonal to <see cref="Running.SessionRunners"/> and to <see cref="Running.WakeModes"/>, but not
/// independent of them: <see cref="BookkeepingSink_Policy"/> refuses <see cref="Log"/> for a session
/// that is handed no state pack, because a note nothing reads is worse than a noisy channel.
/// </para>
/// </summary>
public enum BookkeepingSinks
{
    Channel,
    Log,
}

public static class BookkeepingSink_Names
{
    public const string CHANNEL = "channel";
    public const string LOG = "log";

    public static string Get_Word(BookkeepingSinks sink)
    {
        return sink switch
        {
            BookkeepingSinks.Channel => CHANNEL,
            BookkeepingSinks.Log => LOG,
            _ => throw new Exception($"Unhandled BookkeepingSinks: {sink}"),
        };
    }

    /// <summary>
    /// Null for anything this build does not know, INCLUDING a typo. The caller falls back to
    /// <see cref="BookkeepingSinks.Channel"/>: a machine whose config names a sink this binary has
    /// never heard of keeps the behaviour it already had.
    /// </summary>
    public static BookkeepingSinks? Parse_OrNull(string? word)
    {
        return word?.Trim().ToLowerInvariant() switch
        {
            CHANNEL => BookkeepingSinks.Channel,
            LOG => BookkeepingSinks.Log,
            _ => null,
        };
    }
}
```

- [ ] **Step 4: Write the policy**

```csharp
// AIOrchestratorCoreLib/Channels/StatusLog/BookkeepingSink_Policy.cs
using AIOrchestratorCoreLib.Running;
using AIOrchestratorCoreLib.Running.RoleRunnerConfig;

namespace AIOrchestratorCoreLib.Channels.StatusLog;

/// <summary>
/// THE SINK A SESSION ACTUALLY GETS, which is not always the one its config names.
///
/// <para>
/// A session only ever sees a note that left its channel through its STATE PACK or through the notes
/// riding its next turn, and both of those exist only where the app opens the turn: a bridge-driven
/// session (the dispatcher writes the pack), or a terminal session in <see cref="WakeModes.Ticket"/>
/// (the wake-ticket sweep writes it — 2026-09-15 plan 01, Task 11). A terminal session still on the
/// fingerprint watcher has neither, and its channel file is the whole of what it knows. So
/// <see cref="BookkeepingSinks.Log"/> is REFUSED there rather than honoured: the ledger advisory, the
/// orphan report and the "your question was NOT sent to the owner" notice would otherwise land in a
/// file nothing reads, and the session would be indistinguishable from one with nothing to be told.
/// </para>
/// <para>
/// A POLICY AND NOT AN <c>if</c> AT THE ROUTER, for the reason every other policy in this repo gives:
/// the refusal is arguable, it has one line per reason, and it has to be testable without a session.
/// </para>
/// </summary>
public static class BookkeepingSink_Policy
{
    public static BookkeepingSinks Resolve(IRoleRunnerConfig roleConfig)
    {
        if (roleConfig.Bookkeeping == BookkeepingSinks.Channel)
            return BookkeepingSinks.Channel;

        if (roleConfig.Runner == SessionRunners.Terminal && roleConfig.Wake != WakeModes.Ticket)
            return BookkeepingSinks.Channel;

        return BookkeepingSinks.Log;
    }

    /// <summary>The sentence the log writes when the policy overrides a stated sink — so the refusal is never silent.</summary>
    public static string Describe_Refusal(SessionRoles role)
    {
        return $"'{role}' asks for bookkeeping in the status log, but it runs in a terminal on the fingerprint watcher: it is handed no state pack, so a note there would reach nobody. Bookkeeping stays in the channel until that role's `wake` is `ticket`.";
    }
}
```

- [ ] **Step 5: Add `Bookkeeping` to the role config triple and parse it**

In `IRoleRunnerConfig.cs` add `BookkeepingSinks Bookkeeping { get; }`. In `RoleRunnerConfigModel.cs` add the primary-constructor parameter `BookkeepingSinks bookkeeping` and `public BookkeepingSinks Bookkeeping { get; } = bookkeeping;`. In `RoleRunnerConfig_Factory.cs` add `BookkeepingSinks bookkeeping = BookkeepingSinks.Channel` as the LAST parameter of `Create`, so no existing call site changes, and pass `BookkeepingSinks.Channel` explicitly from `Create_Default`.

In `RunnerConfigs_Json.cs`:

```csharp
    public const string BOOKKEEPING_KEY = "bookkeeping";
```

and, beside the existing `wake` read in `Parse_Role`:

```csharp
        var bookkeeping = BookkeepingSink_Names.Parse_OrNull(Read_String_OrNull(roleNode, BOOKKEEPING_KEY)) ?? defaults.Bookkeeping;
```

**Pass `bookkeeping` into all THREE `RoleRunnerConfig_Factory.Create(...)` calls in that method** — the non-bg branch, the bg-refused-falls-back-to-terminal branch and the bg-accepted branch. Miss one and that path silently drops the setting. Confirm with `grep -n "RoleRunnerConfig_Factory.Create(" AIOrchestratorCoreLib/Running/RunnerConfigs/RunnerConfigs_Json.cs` after editing; there must be three and each must name it.

And in `Write`, beside the `WAKE_KEY` line:

```csharp
            [BOOKKEEPING_KEY] = BookkeepingSink_Names.Get_Word(roleConfig.Bookkeeping),
```

- [ ] **Step 6: Run the tests and watch them pass**

Run: `~/.dotnet/dotnet test AIOrchestratorCoreLib.Tests/AIOrchestratorCoreLib.Tests.csproj --filter "FullyQualifiedName~BookkeepingSinkConfigTests|FullyQualifiedName~RunnerConfigsJsonTests|FullyQualifiedName~WakeModeConfigTests"`
Expected: PASS — 5 new, and the two existing classes unchanged.

- [ ] **Step 7: Commit**

```bash
git add AIOrchestratorCoreLib/Channels/StatusLog/BookkeepingSinks.cs AIOrchestratorCoreLib/Channels/StatusLog/BookkeepingSink_Policy.cs AIOrchestratorCoreLib/Running/RoleRunnerConfig/ AIOrchestratorCoreLib/Running/RunnerConfigs/RunnerConfigs_Json.cs AIOrchestratorCoreLib.Tests/Running/BookkeepingSinkConfigTests.cs
git commit -F /tmp/cm.txt   # "feat(running): a bookkeeping sink per role, defaulting to the channel, refused where no pack is written"
```

---

## Task 4: `AppNote_Writer` — the one place the routing decision is made

> **SIGNATURE CORRECTED AT TASK 3 (2026-09-16).** The policy takes the SESSION STATE as well as the
> role config — `Resolve(IRoleRunnerConfig, IPrintSessionState?)` — because config speaks for the
> ROLE and `DrivesTurns` speaks for the SESSION, and they disagree in a real case: a member demoted
> by `OrchestrationLauncherModel.Demote_ToTerminal` still reads `runner: print` in config while its
> own file says the dispatcher let it go, and NOBODY writes it a pack. The role-only version this
> plan first proposed would have answered `Log` and made that session deaf. So `AppNote_Writer.Write`
> takes an `IPrintSessionState? state` too and passes it through; read it with
> `PrintSessionState_Store.Read_OrNull(PrintSessionState_Store.Get_StateFile(...))`.
> `Describe_Refusal` is likewise `Describe_Refusal_OrNull(SessionRoles, IRoleRunnerConfig, IPrintSessionState?)`
> and returns null when nothing was refused, so a caller cannot log a refusal that did not happen.

**Files:**
- Create: `AIOrchestratorCoreLib/Channels/StatusLog/AppNoteKinds.cs`
- Create: `AIOrchestratorCoreLib/Channels/StatusLog/AppNote_Writer.cs`
- Test: `AIOrchestratorCoreLib.Tests/Channels/AppNoteRoutingTests.cs`

**Interfaces:**
- Consumes: `BookkeepingSink_Policy` (Task 3), `StatusLog_Store` (Task 2).
- Produces: `AppNote_Writer.Write(...)` — returns whether the note landed, in the same `bool` contract every caller already honours. Tasks 5–9 are its only callers.

**Why a kind and not a bool:** the router must be able to say NO to a site somebody routes by mistake. A closed enum of kinds, each of which the classification table names, is what makes "route this one" a decision with a name instead of an argument.

- [ ] **Step 1: Write the failing test**

```csharp
// AIOrchestratorCoreLib.Tests/Channels/AppNoteRoutingTests.cs
using AIOrchestratorCoreLib.Channels;
using AIOrchestratorCoreLib.Channels.StatusLog;
using AIOrchestratorCoreLib.Running;
using AIOrchestratorCoreLib.Running.RoleRunnerConfig;
using Xunit;

namespace AIOrchestratorCoreLib.Tests.Channels;

public class AppNoteRoutingTests
{
    static readonly DateTime NOW = new(2026, 9, 15, 14, 30, 0, DateTimeKind.Local);

    static IRoleRunnerConfig Role(BookkeepingSinks sink) =>
        RoleRunnerConfig_Factory.Create(SessionRunners.Print, ResumeModes.Transcript, null, null, WakeModes.Watcher, sink);

    sealed class Files : IDisposable
    {
        public string Channel { get; } = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName() + ".md");
        public string Log { get; } = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName() + ".jsonl");

        public Files() => File.WriteAllText(Channel, "## [1] FROM supervisor — 2026-09-15 10:00 — brief\n\ndo X\n");

        public IReadOnlyList<ChannelEntry.IChannelEntry> ChannelEntries() => ChannelEntry_Parser.Parse_All(File.ReadAllText(Channel));

        public void Dispose()
        {
            File.Delete(Channel);
            File.Delete(Log);
        }
    }

    /// <summary>
    /// THE DEFAULT IS THE BEHAVIOUR EVERY LIVE MACHINE ALREADY HAS. Until somebody sets the key, a
    /// routed note is written exactly where it was written before this existed, byte for byte.
    /// </summary>
    [Fact]
    public void UnderTheDefaultSink_ARoutedNoteStillGoesToTheChannel()
    {
        using var files = new Files();

        Assert.True(AppNote_Writer.Write(
            files.Channel, files.Log, Role(BookkeepingSinks.Channel), AppNoteKinds.TurnEnded,
            AppEntryAudiences.Agent, "turn_ended imp-1 turn 4 — success", "request_id: r/imp-1/4", NOW));

        Assert.Equal(2, files.ChannelEntries().Count);
        Assert.Empty(StatusLog_Store.Read_Entries(files.Log));
    }

    [Fact]
    public void UnderTheLogSink_ARoutedAgentNoteLeavesTheChannel()
    {
        using var files = new Files();

        Assert.True(AppNote_Writer.Write(
            files.Channel, files.Log, Role(BookkeepingSinks.Log), AppNoteKinds.TurnEnded,
            AppEntryAudiences.Agent, "turn_ended imp-1 turn 4 — success", "request_id: r/imp-1/4", NOW));

        Assert.Single(files.ChannelEntries());
        Assert.Equal("[agent] turn_ended imp-1 turn 4 — success", Assert.Single(StatusLog_Store.Read_Entries(files.Log)).Subject);
    }

    /// <summary>
    /// THE ORACLE OF THIS WHOLE PLAN, and it is one line of production code: the owner's phone is fed
    /// from the CHANNEL — <c>MirrorText_Formatter.Should_Mirror</c> refuses an <c>[agent]</c>-tagged
    /// app entry and mirrors every other one — so an entry that leaves the channel leaves the phone.
    /// The brief's constraint is that every entry the owner sees today goes on being seen; this is
    /// where that is discharged, mechanically, for all 77 sites at once.
    ///
    /// <para>
    /// It is a THEORY over every kind, not one case, because the failure it guards against is a
    /// future kind added without the audience screen. A single-kind assertion would pass while the
    /// next one was wrong.
    /// </para>
    /// </summary>
    [Theory]
    [MemberData(nameof(EveryKind))]
    public void AnOwnerFacingNoteNeverLeavesTheChannel_WhateverTheKindAndWhateverTheSink(AppNoteKinds kind)
    {
        using var files = new Files();

        Assert.True(AppNote_Writer.Write(
            files.Channel, files.Log, Role(BookkeepingSinks.Log), kind,
            AppEntryAudiences.Owner, "turn stalled solo-1 turn 9", "it has not answered", NOW));

        Assert.Equal(2, files.ChannelEntries().Count);
        Assert.Empty(StatusLog_Store.Read_Entries(files.Log));
    }

    public static TheoryData<AppNoteKinds> EveryKind
    {
        get
        {
            TheoryData<AppNoteKinds> data = [];

            foreach (var kind in Enum.GetValues<AppNoteKinds>())
                data.Add(kind);

            return data;
        }
    }

    /// <summary>
    /// A LOCKED CHANNEL IS STILL A FALSE, and the callers depend on it: several of them record a memo
    /// on the strength of the return and would suppress the retry the next tick owes.
    /// </summary>
    [Fact]
    public void AnUnwritableDestination_AnswersFalse()
    {
        using var files = new Files();

        Assert.False(AppNote_Writer.Write(
            Path.Combine(files.Channel, "not-a-folder", "c.md"),
            Path.Combine(files.Log, "not-a-folder", "s.jsonl"),
            Role(BookkeepingSinks.Channel), AppNoteKinds.LedgerAdvisory,
            AppEntryAudiences.Agent, "PLAN.md is behind your verdicts", "update it", NOW));
    }
}
```

- [ ] **Step 2: Run it and watch it fail**

Run: `~/.dotnet/dotnet test AIOrchestratorCoreLib.Tests/AIOrchestratorCoreLib.Tests.csproj --filter "FullyQualifiedName~AppNoteRoutingTests"`
Expected: compile error — `AppNoteKinds` and `AppNote_Writer` do not exist.

- [ ] **Step 3: Write the kinds**

```csharp
// AIOrchestratorCoreLib/Channels/StatusLog/AppNoteKinds.cs
namespace AIOrchestratorCoreLib.Channels.StatusLog;

/// <summary>
/// THE BOOKKEEPING THAT MAY LEAVE A CHANNEL — a closed list, one value per family in the 2026-09-15
/// one-wake-model plan 02's classification table. 77 places in this codebase write an app-authored
/// channel entry; 17 of them name a kind here and the other 60 do not, because they are the owner's
/// receipts or the conversation itself.
///
/// <para>
/// A NAMED KIND RATHER THAN A BOOLEAN AT THE CALL SITE. "route this one" has to be a decision with a
/// name that a reader can look up in the table, or the next person routes a nudge — and a nudge that
/// leaves the channel breaks the orphan escalation, which is the only proof a member's monitor is
/// dead (<c>Nudge_Decider</c>, and its test
/// <c>AnAppEntryStillCountsAsInbound_BecauseEscalationDependsOnIt</c>, left as a tripwire for exactly
/// this change).
/// </para>
/// </summary>
public enum AppNoteKinds
{
    /// <summary>The dispatcher's record of the turn that just ended. Nobody answers it; the session already knows.</summary>
    TurnEnded,

    /// <summary>
    /// Stall alerts, usage-limit notices, deadline-kill warnings, and the two coaching notices about a
    /// turn's own shape (a superseded final message, a reply addressed to a channel that is not a
    /// source). Owner-facing on a supervisor, solo or general session — and then it does not move.
    /// </summary>
    TurnMachinery,

    /// <summary>PLAN.md is behind the verdicts / has lines that cannot show progress / claims work nobody is doing.</summary>
    LedgerAdvisory,

    /// <summary>A member has been nudged and has not moved — <c>OrphanEscalation_Decider.Describe_Report</c>.</summary>
    OrphanReport,

    /// <summary>
    /// The message and question contracts, and the three question-deduplication notices. The session
    /// must read these — "your question was NOT sent to the owner" is the only trace that a question
    /// died — which is why the pack carries them (Task 11) rather than the log merely holding them.
    /// </summary>
    ContractCoaching,
}
```

- [ ] **Step 4: Write the router**

```csharp
// AIOrchestratorCoreLib/Channels/StatusLog/AppNote_Writer.cs
using AIOrchestratorCoreLib.Running.RoleRunnerConfig;

namespace AIOrchestratorCoreLib.Channels.StatusLog;

/// <summary>
/// CHANNEL OR LOG — ONE DECISION, ONE PLACE. Every site that writes a kind of
/// <see cref="AppNoteKinds"/> calls this instead of <see cref="ChannelAppender.Append_AppEntry"/>,
/// and nothing else about the site changes: same subject, same body, same <c>bool</c> meaning "an
/// entry is on disk".
///
/// <para>
/// AN OWNER-FACING NOTE NEVER MOVES, and this is the whole of the protection the owner's phone gets.
/// The mirror reads entries back out of the CHANNEL FILE — it never sees the call that wrote one
/// (<see cref="AppEntryAudience_Tag"/> exists because of that) — so an entry written to the log is an
/// entry the owner will never be shown. The audience screen here is therefore not a nicety: it is the
/// reason the 2026-09-15 plan can claim that nothing the owner sees today disappears.
/// </para>
/// <para>
/// THE SINK IS THE POLICY'S, NOT THE CONFIG'S. <see cref="BookkeepingSink_Policy"/> refuses the log
/// for a terminal session on the fingerprint watcher, which is handed no state pack and would never
/// read the note. Asking the config directly here would put that refusal at every call site.
/// </para>
/// </summary>
public static class AppNote_Writer
{
    /// <summary>
    /// Returns whether the note landed — false means the channel stayed locked for the whole budget,
    /// or the log could not be written. Callers that record a memo on the strength of a note MUST
    /// honour it, for the reason each of them already states at its own append.
    /// </summary>
    public static bool Write(
        string channelFilePath,
        string statusLogFilePath,
        IRoleRunnerConfig roleConfig,
        AppNoteKinds kind,
        AppEntryAudiences audience,
        string subject,
        string body,
        DateTime nowLocal)
    {
        // The kind is not consulted for WHERE — every kind in the enum is movable, by construction —
        // but it is required so that routing a site is a named decision. It is carried into the log
        // by nothing and into the channel by nothing: the entry's text is identical either way, which
        // is what makes the switch reversible with no migration.
        _ = kind;

        if (audience == AppEntryAudiences.Owner || BookkeepingSink_Policy.Resolve(roleConfig, state) == BookkeepingSinks.Channel)
            return ChannelAppender.Append_AppEntry(channelFilePath, audience, subject, body, nowLocal);

        return StatusLog_Store.Append(statusLogFilePath, subject, body, nowLocal);
    }
}
```

- [ ] **Step 5: Run the tests and watch them pass**

Run: `~/.dotnet/dotnet test AIOrchestratorCoreLib.Tests/AIOrchestratorCoreLib.Tests.csproj --filter "FullyQualifiedName~AppNoteRoutingTests"`
Expected: PASS — 4 facts plus 5 theory cases (one per kind).

- [ ] **Step 6: Commit**

```bash
git add AIOrchestratorCoreLib/Channels/StatusLog/AppNoteKinds.cs AIOrchestratorCoreLib/Channels/StatusLog/AppNote_Writer.cs AIOrchestratorCoreLib.Tests/Channels/AppNoteRoutingTests.cs
git commit -F /tmp/cm.txt   # "feat(channels): one router decides whether a bookkeeping note goes to the channel or the log"
```

---

## Task 5: `turn_ended` is routed — and the decider that reads it follows

**Files:**
- Modify: `AIOrchestratorCoreLib/Running/PrintTurnDispatcher/PrintTurnDispatcherModel.cs` (`Append_TurnEnded`, ~line 2092)
- Modify: `AIOrchestratorCoreLib/Running/StallAlert_Decider.cs` (`Has_AlreadyReachedOwner`)
- Test: `AIOrchestratorCoreLib.Tests/Bridge/NothingMovedBecomesInvisibleTests.cs`

**Interfaces:**
- Consumes: `AppNote_Writer` (Task 4).
- Produces: nothing new. This is the first routed site and it is `turn_ended` because it is the largest single family and the only one with an in-app reader.

**Why the decider must move with it:** `StallAlert_Decider.Has_AlreadyReachedOwner` walks the channel backwards looking for `turn_ended <member> turn <n>` and `turn stalled <member> turn <n>` records, to decide whether a repeat stall alert has already reached the owner's phone. **If `turn_ended` leaves the channel and this keeps reading the channel, the decider stops seeing half its evidence and a stall alert is repeated on the owner's phone.** That is brief constraint 5 — an app entry that is the only trace of an event cannot vanish without something replacing it — and it is why the two changes are one task.

- [ ] **Step 1: Write the failing test**

```csharp
// AIOrchestratorCoreLib.Tests/Bridge/NothingMovedBecomesInvisibleTests.cs
using AIOrchestratorCoreLib.Channels;
using AIOrchestratorCoreLib.Channels.StatusLog;
using AIOrchestratorCoreLib.Running;
using Xunit;

namespace AIOrchestratorCoreLib.Tests.Bridge;

/// <summary>
/// AN APP ENTRY THAT IS THE ONLY TRACE OF AN EVENT MAY NOT VANISH WITHOUT A REPLACEMENT. Five things
/// in this codebase read the app's own entries back out of a channel — <c>StallAlert_Decider</c>,
/// <c>PrintTurn_Trigger.Is_AgentNote</c>, <c>Nudge_Decider</c>, <c>MemberState_Resolver</c> and
/// <c>MirrorText_Formatter</c>. This class is where each one is checked against the kinds plan 02
/// moves, so that "it is in the log now" is a fact about a reader rather than a hope.
/// </summary>
public class NothingMovedBecomesInvisibleTests
{
    const string CHANNEL =
        "## [1] FROM supervisor — 2026-09-15 10:00 — brief\n\ndo X\n" +
        "## [2] FROM app — 2026-09-15 10:05 — turn stalled imp-1 turn 4 — no reply\n\nit went quiet\n";

    /// <summary>
    /// The stall decider's evidence is split across two files the moment turn_ended is routed. Read
    /// only the channel and it sees the stall alert with no ending after it, concludes the turn is
    /// still stalled, and sends the owner the same alert again — a waterfall on their phone, which is
    /// the thing this system exists to prevent (CLAUDE.md decision 14).
    /// </summary>
    [Fact]
    public void AStallAlertIsNotRepeated_WhenTheTurnEndedRecordIsInTheLogRatherThanTheChannel()
    {
        var log = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName() + ".jsonl");

        StatusLog_Store.Append(log, $"{PrintTurn_Words.TURN_ENDED_SUBJECT} imp-1 turn 4 — success", "request_id: r/imp-1/4", new DateTime(2026, 9, 15, 10, 6, 0, DateTimeKind.Local));

        var channelEntries = ChannelEntry_Parser.Parse_All(CHANNEL);

        // Without the log, the alert for turn 4 looks unanswered and would be re-sent.
        Assert.True(StallAlert_Decider.Has_AlreadyReachedOwner(channelEntries, [], "imp-1", 4));

        // With it, the turn is over and the decider says so — the same answer it gave when both
        // records sat in one file.
        Assert.False(StallAlert_Decider.Has_AlreadyReachedOwner(channelEntries, StatusLog_Store.Read_Entries(log), "imp-1", 5));

        File.Delete(log);
    }
}
```

> **Read `StallAlert_Decider.Has_AlreadyReachedOwner` before writing this test**: its exact semantics for `turnNumber` (an alert for a DIFFERENT turn number means the numbering moved on) decide which of the two assertions above is `True` and which is `False`. Adjust the two turn numbers to match what the method actually promises, and say in the commit which way round it came out. Do not adjust the method to match the test.

- [ ] **Step 2: Run it and watch it fail**

Run: `~/.dotnet/dotnet test AIOrchestratorCoreLib.Tests/AIOrchestratorCoreLib.Tests.csproj --filter "FullyQualifiedName~NothingMovedBecomesInvisibleTests"`
Expected: compile error — `Has_AlreadyReachedOwner` takes three arguments, not four.

- [ ] **Step 3: Give the decider the second file**

In `StallAlert_Decider.cs`, change the signature and merge the two lists at the top of the method:

```csharp
    /// <summary>
    /// …existing summary…
    ///
    /// <para>
    /// TWO FILES SINCE 2026-09-15. <c>turn_ended</c> is routed to the session's
    /// <see cref="Channels.StatusLog.StatusLog_Store"/> when its role's bookkeeping sink is the log,
    /// so the evidence this walk needs is split: the stall alerts that reached the owner are in the
    /// channel (they are owner-facing and never move), and the endings that answer them may be in the
    /// log. Reading only the channel would see an alert with no ending after it and send the owner
    /// the same alert again. Both lists are passed rather than one merged list built at the call
    /// site, so the ORDER is settled here — the log's records and the channel's entries are each in
    /// their own file's order and the walk needs them interleaved by time.
    /// </para>
    /// </summary>
    public static bool Has_AlreadyReachedOwner(
        IReadOnlyList<IChannelEntry> channelEntries,
        IReadOnlyList<IChannelEntry> statusLogEntries,
        string memberId,
        int turnNumber)
    {
        var entries = Interleave_ByStamp(channelEntries, statusLogEntries);

        // …the existing backward walk, over `entries` instead of `channelEntries`…
    }

    /// <summary>
    /// Both lists in one, ordered by the stamp the APP wrote — which, unlike an agent's, can be
    /// trusted (CLAUDE.md decision 12; <c>ChannelAppender</c> and <c>StatusLog_Store</c> both write
    /// it themselves). An entry whose stamp cannot be parsed keeps its position relative to its own
    /// file rather than being dropped: this walk answers "has the owner already been told", and
    /// dropping evidence would answer no and text them again.
    /// </summary>
    static IReadOnlyList<IChannelEntry> Interleave_ByStamp(IReadOnlyList<IChannelEntry> channelEntries, IReadOnlyList<IChannelEntry> statusLogEntries)
    {
        if (statusLogEntries.Count == 0)
            return channelEntries;

        return
        [
            .. channelEntries
                .Select((entry, position) => (entry, file: 0, position))
                .Concat(statusLogEntries.Select((entry, position) => (entry, file: 1, position)))
                .OrderBy(item => Stamp_OrMax(item.entry))
                .ThenBy(item => item.file)
                .ThenBy(item => item.position)
                .Select(item => item.entry)
        ];
    }

    static DateTime Stamp_OrMax(IChannelEntry entry)
    {
        return DateTime.TryParseExact(entry.DateText, "yyyy-MM-dd HH:mm", System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out var stamp)
            ? stamp
            : DateTime.MaxValue;
    }
```

Then fix the one production call site — `PrintTurnDispatcherModel.Resolve_StallAlertAudience` — to pass the log:

```csharp
        var audience = StallAlert_Decider.Resolve_Audience(
            roleAudience,
            ChannelHistory_Cache.Read_Entries(state.ChannelFilePath),
            StatusLog_Store.Read_Entries(StatusLog_Store.Get_File(_paths, state.Role, state.OrchId, state.MemberId)),
            state.MemberId,
            state.NextTurnNumber);
```

`Resolve_Audience` forwards to `Has_AlreadyReachedOwner`; give it the same extra parameter.

- [ ] **Step 4: Route `Append_TurnEnded`**

In `PrintTurnDispatcherModel.Append_TurnEnded`, replace the `ChannelAppender.Append_AppEntry(...)` call with:

```csharp
        if (!AppNote_Writer.Write(
                state.ChannelFilePath,
                StatusLog_Store.Get_File(_paths, state.Role, state.OrchId, state.MemberId),
                configs.Get_ForRole(state.Role),
                AppNoteKinds.TurnEnded,
                AppEntryAudiences.Agent,
                $"{TURN_ENDED_SUBJECT} {state.MemberId} turn {requestId[(requestId.LastIndexOf('/') + 1)..]} — {outcome}",
                body,
                DateTime.Now))
        {
            _log.Log_Warning(state.OrchId, $"turn_ended for {requestId} could not be written (channel locked, or the status log is unwritable) — the turn itself is recorded in the state file");
        }
```

`Append_TurnEnded` does not currently take an `IRunnerConfigs`. Thread it from the caller — `grep -n "Append_TurnEnded(" AIOrchestratorCoreLib/Running/PrintTurnDispatcher/PrintTurnDispatcherModel.cs` — rather than reading the configs again inside the method: the dispatcher already resolves them once per tick and a second read could answer differently mid-tick.

- [ ] **Step 5: Run the tests and watch them pass**

Run: `~/.dotnet/dotnet test AIOrchestratorCoreLib.Tests/AIOrchestratorCoreLib.Tests.csproj --filter "FullyQualifiedName~NothingMovedBecomesInvisibleTests|FullyQualifiedName~StallAlert"`
Expected: PASS — the new case plus `StallAlertDeciderTests` unchanged (its existing cases pass `[]` for the log).

- [ ] **Step 6: Run the dispatcher's own suite — this is the task most likely to break something**

Run: `~/.dotnet/dotnet test AIOrchestratorCoreLib.Tests/AIOrchestratorCoreLib.Tests.csproj --filter "FullyQualifiedName~PrintTurn|FullyQualifiedName~Dispatcher|FullyQualifiedName~TurnEnded"`
Expected: PASS. Any test that asserts a `turn_ended` entry appears in a channel is asserting the DEFAULT sink and must still pass; if one fails, the default is wrong, not the test.

- [ ] **Step 7: Commit**

```bash
git add AIOrchestratorCoreLib/Running/PrintTurnDispatcher/PrintTurnDispatcherModel.cs AIOrchestratorCoreLib/Running/StallAlert_Decider.cs AIOrchestratorCoreLib.Tests/Bridge/NothingMovedBecomesInvisibleTests.cs
git commit -F /tmp/cm.txt   # "feat(running): turn_ended is routed, and the stall decider reads both files so an alert is not repeated"
```

---

## Task 6: The rest of the turn machinery

**Files:**
- Modify: `AIOrchestratorCoreLib/Running/PrintTurnDispatcher/PrintTurnDispatcherModel.cs` — the seven remaining app-entry sites (~850, 1171, 1591, 1752, 1865, 1954, 2034)
- Test: `AIOrchestratorCoreLib.Tests/Channels/AppNoteRoutingTests.cs` (one added case)

**Interfaces:** consumes `AppNote_Writer`; produces nothing new.

**The audience does the work.** Five of these seven resolve their audience at runtime (`Stall_Audience` / `Resolve_StallAlertAudience`): `Owner` for a supervisor, a solo and the general supervisor; `Agent` for a member. Passing that value straight into `AppNote_Writer` means the supervisor's stall alert stays in the channel and keeps reaching the phone, while a member's identical alert moves — with no new branch anywhere.

- [ ] **Step 1: Write the failing test**

Add to `AppNoteRoutingTests`:

```csharp
    /// <summary>
    /// THE SAME EVENT, TWO DESTINATIONS, DECIDED BY WHO IT IS FOR. A stall on a member is bookkeeping
    /// its supervisor reads at its next turn; the identical stall on the SUPERVISOR is the owner's
    /// only warning that their orchestration has stopped, and <c>Stall_Audience</c> already says so.
    /// Routing on the audience is what makes plan 02 unable to take anything off the owner's phone.
    /// </summary>
    [Fact]
    public void AStallOnAMemberMoves_TheSameStallOnASupervisorDoesNot()
    {
        using var members = new Files();
        using var supervisors = new Files();

        AppNote_Writer.Write(members.Channel, members.Log, Role(BookkeepingSinks.Log), AppNoteKinds.TurnMachinery,
            AppEntryAudiences.Agent, "turn stalled imp-1 turn 9", "no reply", NOW);

        AppNote_Writer.Write(supervisors.Channel, supervisors.Log, Role(BookkeepingSinks.Log), AppNoteKinds.TurnMachinery,
            AppEntryAudiences.Owner, "turn stalled supervisor turn 9", "no reply", NOW);

        Assert.Single(members.ChannelEntries());
        Assert.Single(StatusLog_Store.Read_Entries(members.Log));

        Assert.Equal(2, supervisors.ChannelEntries().Count);
        Assert.Empty(StatusLog_Store.Read_Entries(supervisors.Log));
    }
```

- [ ] **Step 2: Run it and watch it pass already**

Run: `~/.dotnet/dotnet test AIOrchestratorCoreLib.Tests/AIOrchestratorCoreLib.Tests.csproj --filter "FullyQualifiedName~AppNoteRoutingTests"`
Expected: PASS. **This test passes before the production change and that is intended** — it pins the routing rule, not the call sites. The call sites are pinned by Task 1's census plus the scan in Step 4 below, which is the honest instrument for "every site was converted".

- [ ] **Step 3: Route the seven sites**

For each of the seven, replace `ChannelAppender.Append_AppEntry(` with `AppNote_Writer.Write(` and insert three arguments before the audience: the status-log path, the role config, and `AppNoteKinds.TurnMachinery`. The audience argument, the subject, the body and the `DateTime.Now` are unchanged, and so is every surrounding `if (!…)` / `var appended = …` and its log line.

The status-log path is the same expression at all seven, so hoist it into a private helper beside `Stall_Audience`:

```csharp
    /// <summary>
    /// This session's status log — where its bookkeeping goes when its role's sink is the log. Not
    /// cached: a session's role, orchestration and member id are fixed for its life, so the path is a
    /// pure function of them, and a field would be one more thing to keep in step with a respawn.
    /// </summary>
    string Status_LogFile(IPrintSessionState state)
    {
        return StatusLog_Store.Get_File(_paths, state.Role, state.OrchId, state.MemberId);
    }
```

**Do not route `Write_Reply_Async`'s member entries or `Write_SupersededFinals_Async`'s filings** — those are `Append_SessionEntry`, written under the SESSION's author word, and they are the conversation. Only the seven `Append_AppEntry` sites move.

- [ ] **Step 4: Prove every site was converted**

Run: `grep -n "ChannelAppender.Append_AppEntry(" AIOrchestratorCoreLib/Running/PrintTurnDispatcher/PrintTurnDispatcherModel.cs`
Expected: **no output.** All eight of this file's app-entry sites (Task 5's plus these seven) now go through the router.

- [ ] **Step 5: Run the dispatcher's suite**

Run: `~/.dotnet/dotnet test AIOrchestratorCoreLib.Tests/AIOrchestratorCoreLib.Tests.csproj --filter "FullyQualifiedName~PrintTurn|FullyQualifiedName~Dispatcher|FullyQualifiedName~Stall|FullyQualifiedName~Superseded|FullyQualifiedName~UsageLimit"`
Expected: PASS, unchanged — every one of these tests runs under the default sink.

- [ ] **Step 6: Commit**

```bash
git add AIOrchestratorCoreLib/Running/PrintTurnDispatcher/PrintTurnDispatcherModel.cs AIOrchestratorCoreLib.Tests/Channels/AppNoteRoutingTests.cs
git commit -F /tmp/cm.txt   # "feat(running): the dispatcher's turn machinery routes through the note writer, owner-facing alerts unmoved"
```

---

## Task 7: The ledger advisories

**Files:**
- Modify: `AIOrchestratorCoreLib/Bridge/BridgeEngine/BridgeEngineModel.cs` — `Check_LedgerHealth_Async` (~3548), `Report_LedgerShape` (~3628), `Report_StaleInProgress` (~3715)
- Test: `AIOrchestratorCoreLib.Tests/Bridge/NothingMovedBecomesInvisibleTests.cs` (one added case)

**Interfaces:** consumes `AppNote_Writer`; produces nothing new.

**The shape of the problem:** these three go through `Append_SupervisorAttention_UnlessMeeting`, the choke point that carries the meeting screen, the pause screen and `Raise_OrchestrationActivity`. **None of those may be skipped**, so the routing goes INSIDE the choke point and not around it: an overload that takes an `AppNoteKinds?` and routes only when it is non-null.

This is also where decision 15 is already satisfied and stays satisfied — the PLAN.md shape complaint was moved off Telegram in 2026-08-10 precisely because the owner cannot act on it, and it is `AppEntryAudiences.Agent` for that reason.

- [ ] **Step 1: Write the failing test**

Add to `NothingMovedBecomesInvisibleTests`:

```csharp
    /// <summary>
    /// THE CHOKE POINT'S SCREENS ARE NOT BYPASSED BY ROUTING. Every piece of supervisor-facing
    /// attention traffic passes through <c>Append_SupervisorAttention_UnlessMeeting</c>, which
    /// refuses during a meeting and while an orchestration is paused — *"miss one and dormancy is a
    /// word"* (CLAUDE.md's PAUSE decision). A routed advisory that reached the status log while the
    /// owner was at the terminal, or while the orchestration was asleep, would be exactly the missed
    /// one: the note is still there at the session's next turn and the pause meant nothing.
    /// </summary>
    [Fact]
    public void TheSupervisorAttentionChokePoint_ScreensBeforeItRoutes()
    {
        var body = Extract_Method("bool Append_SupervisorAttention_UnlessMeeting");

        var meeting = body.IndexOf("Suppresses_SupervisorAttention", StringComparison.Ordinal);
        var paused = body.IndexOf("Is_Paused(orchId)", StringComparison.Ordinal);
        var route = body.IndexOf("AppNote_Writer.Write", StringComparison.Ordinal);

        Assert.True(meeting >= 0 && paused >= 0, "this scan is reading a choke point it does not understand");
        Assert.True(route >= 0, "the choke point no longer routes — the ledger advisories are back in the channel unconditionally");

        Assert.True(meeting < route, "a routed advisory is written during a meeting");
        Assert.True(paused < route, "a routed advisory is written to a paused orchestration");
    }
```

> `Extract_Method` already exists in `PauseGatesEveryWakerScanTests`. Move it to `SourceTree` (Task 1) and have both classes call it, rather than copying it — decision 12.

- [ ] **Step 2: Run it and watch it fail**

Run: `~/.dotnet/dotnet test AIOrchestratorCoreLib.Tests/AIOrchestratorCoreLib.Tests.csproj --filter "FullyQualifiedName~NothingMovedBecomesInvisibleTests"`
Expected: FAIL — `AppNote_Writer.Write` does not appear in the choke point.

- [ ] **Step 3: Route inside the choke point**

In `BridgeEngineModel.cs`, add the optional kind to the choke point and to `Append_AppEntry_Safe`'s caller path:

```csharp
    bool Append_SupervisorAttention_UnlessMeeting(
        string orchId,
        string subject,
        string body,
        OwnerPresenceModes presence,
        Channels.AppEntryAudiences audience = Channels.AppEntryAudiences.Agent,
        Channels.StatusLog.AppNoteKinds? routedKind = null)
    {
        if (OwnerPresence_Policy.Suppresses_SupervisorAttention(presence))
            return false;

        if (Is_Paused(orchId))
            return false;

        // ROUTED OR NOT, THE SCREENS ABOVE HAVE ALREADY RUN. A kind is supplied only by the sites the
        // 2026-09-15 plan 02 classified as bookkeeping; everything else that comes through here is
        // the conversation and keeps the channel unconditionally.
        var landed = routedKind == null
            ? Append_AppEntry_Safe(_paths.Get_OwnerChannelFile(orchId), audience, subject, body, DateTime.Now)
            : Route_SupervisorNote(orchId, routedKind.Value, audience, subject, body);

        if (!landed)
            return false;

        Raise_OrchestrationActivity(orchId);
        return true;
    }

    /// <summary>
    /// A supervisor-facing note through the router. The supervisor's own channel is the orchestration's
    /// owner channel and its status log is the one beside its state file, which is why the role is
    /// named here rather than inferred: a member's advisory never comes through this choke point.
    /// </summary>
    bool Route_SupervisorNote(string orchId, Channels.StatusLog.AppNoteKinds kind, Channels.AppEntryAudiences audience, string subject, string body)
    {
        try
        {
            return Channels.StatusLog.AppNote_Writer.Write(
                _paths.Get_OwnerChannelFile(orchId),
                Channels.StatusLog.StatusLog_Store.Get_File(_paths, SessionRoles.Supervisor, orchId, SessionRoles.Supervisor.ToString().ToLowerInvariant()),
                _runnerConfigs.Get_ForRole(SessionRoles.Supervisor),
                kind,
                audience,
                subject,
                body,
                DateTime.Now);
        }
        catch (Exception exception)
        {
            // Decision 21: name WHICH operation failed. This is the same contract Append_AppEntry_Safe
            // states — a throw here would take down the whole mirror tick, not one advisory.
            _log.Log_Warning(orchId, $"Routing the '{subject}' note FAILED and it is lost — {exception.GetType().Name}: {exception.Message}");
            return false;
        }
    }
```

> **Two things to verify before writing this, on the file itself:**
> 1. `_runnerConfigs` — the engine may read its runner configs through a provider rather than a field. `grep -n "IRunnerConfigs\|RunnerConfigs" AIOrchestratorCoreLib/Bridge/BridgeEngine/BridgeEngineModel.cs` and use whatever is already there; do not add a second source of the same answer.
> 2. The supervisor's member id. `StatusLog_Store.Get_File` forwards to `PrintSessionState_Store.Get_StateFile`, whose `Supervisor` branch **ignores `memberId`** and returns `<orch>/.supervisor.print-session.json`. Pass whatever the engine's other calls to `Get_StateFile` pass for a supervisor and keep it identical, so the log sits beside the state file rather than in a second place.

Then add `routedKind:` to the three ledger sites:

```csharp
            if (ledgerOutcome.ShouldAppendAlert
                && Append_SupervisorAttention_UnlessMeeting(
                    session.OrchId,
                    "PLAN.md is behind your verdicts",
                    "You accepted implementer work without updating the task ledger, so the owner's progress bar is now wrong. Update PLAN.md before your next turn ends — the turn-end hook will block until you do.",
                    presence,
                    routedKind: Channels.StatusLog.AppNoteKinds.LedgerAdvisory))
```

and likewise at `Report_LedgerShape` and `Report_StaleInProgress`. **Every one of the three records a memo on the strength of the return** (`_ledgerBehindReportedOrchIds`, `_reportedLedgerShapeByOrchId`, `_reportedStaleInProgress`), and the router preserves the `bool` contract, so none of those lines changes.

- [ ] **Step 4: Run the tests and watch them pass**

Run: `~/.dotnet/dotnet test AIOrchestratorCoreLib.Tests/AIOrchestratorCoreLib.Tests.csproj --filter "FullyQualifiedName~NothingMovedBecomesInvisibleTests|FullyQualifiedName~PauseGatesEveryWakerScanTests|FullyQualifiedName~Ledger"`
Expected: PASS — the new case, the pause register unchanged, and the ledger tests unchanged.

- [ ] **Step 5: Commit**

```bash
git add AIOrchestratorCoreLib/Bridge/BridgeEngine/BridgeEngineModel.cs AIOrchestratorCoreLib.Tests/Bridge/NothingMovedBecomesInvisibleTests.cs
git commit -F /tmp/cm.txt   # "feat(bridge): the three ledger advisories route through the choke point, below its meeting and pause screens"
```

---

## Task 8: The orphan report — and the respawn note that does not exist

**Files:**
- Modify: `AIOrchestratorCoreLib/Bridge/BridgeEngine/BridgeEngineModel.cs` — `Nudge_IdleImplementers_Async` (~3008)
- Modify: `AIOrchestratorCoreLib/Status/Nudge_Wording.cs` — one docstring
- Test: `AIOrchestratorCoreLib.Tests/Bridge/NothingMovedBecomesInvisibleTests.cs` (one added case)

**Interfaces:** consumes the choke point's `routedKind` from Task 7.

**The finding this task records:** the spec's step 3 names "orphan and respawn notes". **There is no respawn note.** `Recover_OrphanedImplementer_Async` — the only writer of `Nudge_Wording.RESPAWN_SUBJECT` — was deleted, and `BridgeEngineModel.cs:3468` records why: it was narrowed five times, each narrowing reacting to a false positive, with no case in the repo of it rescuing a stuck member. `RESPAWN_SUBJECT` survives so `Nudge_Wording.Is_WakeSubject` still recognises entries already sitting in members' channels. It moves nowhere and is not deleted.

**And the nudge does not move.** `Nudge_Implementer_Async` (~3443) writes the entry whose non-answer starts the orphan clock, and `Nudge_Decider` (line 397) deliberately counts agent-tagged app entries for exactly that reason. This task moves the REPORT about an orphan and leaves the nudge that produces one where it is.

- [ ] **Step 1: Write the failing test**

Add to `NothingMovedBecomesInvisibleTests`:

```csharp
    /// <summary>
    /// THE NUDGE STAYS AND THE REPORT MOVES, and the asymmetry is the whole of the care here.
    /// <c>Nudge_Decider</c>'s walk skips OWNER-facing app entries and stops at an agent-tagged one,
    /// because *"orphan recovery is the only proof a monitor is dead and can only run on a member
    /// that has already been nudged, so the app's own nudge must go on counting"*. Its case
    /// <c>AnAppEntryStillCountsAsInbound_BecauseEscalationDependsOnIt</c> was left as a tripwire for
    /// precisely this change; this is the tripwire read out loud.
    /// </summary>
    [Fact]
    public void TheMemberNudgeIsNotRouted_BecauseTheOrphanEscalationCountsIt()
    {
        var body = Extract_Method("async Task<bool> Nudge_Implementer_Async");

        Assert.Contains("ChannelAppender.Append_AppEntry", body, StringComparison.Ordinal);
        Assert.DoesNotContain("AppNote_Writer", body, StringComparison.Ordinal);
    }

    /// <summary>There is no respawn note to move: nothing has written RESPAWN_SUBJECT since Recover_OrphanedImplementer_Async was deleted.</summary>
    [Fact]
    public void NothingWritesARespawnNote()
    {
        var writers = 0;

        foreach (var file in SourceTree.EnumerateCSharp("AIOrchestratorCoreLib"))
        {
            foreach (var line in File.ReadLines(file))
            {
                if (!line.Contains("RESPAWN_SUBJECT", StringComparison.Ordinal))
                    continue;

                if (line.Contains("Append_", StringComparison.Ordinal) || line.Contains("AppNote_Writer", StringComparison.Ordinal))
                    writers++;
            }
        }

        Assert.Equal(0, writers);
    }
```

- [ ] **Step 2: Run it and watch it fail**

Run: `~/.dotnet/dotnet test AIOrchestratorCoreLib.Tests/AIOrchestratorCoreLib.Tests.csproj --filter "FullyQualifiedName~NothingMovedBecomesInvisibleTests"`
Expected: the two new cases PASS immediately (nothing has been changed yet) and Step 3 must keep them passing. **If `TheMemberNudgeIsNotRouted…` fails after Step 3, a nudge was routed by mistake and the orphan escalation is broken.**

- [ ] **Step 3: Route the orphan report**

```csharp
                var report = OrphanEscalation_Decider.Describe_Report(member.MemberId, ORPHAN_CONFIRM_MINUTES);

                Append_SupervisorAttention_UnlessMeeting(
                    session.OrchId,
                    report.Subject,
                    report.Body,
                    Resolve_Presence(session.OrchId),
                    routedKind: Channels.StatusLog.AppNoteKinds.OrphanReport);
```

- [ ] **Step 4: Correct `Nudge_Wording.RESPAWN_SUBJECT`'s docstring**

It still says *"the subject of the entry the app appends when it respawns an orphaned member"*, present tense, and nothing does. Replace the first sentence with:

```csharp
    /// <summary>
    /// The subject of the entry the app USED TO append when it respawned an orphaned member.
    /// <c>Recover_OrphanedImplementer_Async</c> is gone (see the note where it lived in
    /// <c>BridgeEngineModel</c>) and NOTHING WRITES THIS ANY MORE. It stays because
    /// <see cref="Is_WakeSubject"/> has to keep recognising the entries already sitting in members'
    /// channels from before that change — a recogniser without a writer is correct here, and a
    /// recogniser that RE-TYPES the text it recognises would not be (decision 12).
    /// </summary>
```

- [ ] **Step 5: Run the tests and watch them pass**

Run: `~/.dotnet/dotnet test AIOrchestratorCoreLib.Tests/AIOrchestratorCoreLib.Tests.csproj --filter "FullyQualifiedName~NothingMovedBecomesInvisibleTests|FullyQualifiedName~Nudge|FullyQualifiedName~Orphan"`
Expected: PASS, including `AnAppEntryStillCountsAsInbound_BecauseEscalationDependsOnIt`.

- [ ] **Step 6: Commit**

```bash
git add AIOrchestratorCoreLib/Bridge/BridgeEngine/BridgeEngineModel.cs AIOrchestratorCoreLib/Status/Nudge_Wording.cs AIOrchestratorCoreLib.Tests/Bridge/NothingMovedBecomesInvisibleTests.cs
git commit -F /tmp/cm.txt   # "feat(bridge): the orphan report routes, the nudge that produces one does not, and respawn has had no writer for a week"
```

---

## Task 9: The contract and question coaching

**Files:**
- Modify: `AIOrchestratorCoreLib/Bridge/BridgeEngine/BridgeEngineModel.cs` — `Coach_OnContractFaults` (~13084), `Refuse_Question` (~13112), `Supersede_OlderQuestions_Async` (~13284), `Handle_ReaskOfADecidedQuestion` (~13339), `Handle_RepeatedQuestion` (~13363)
- Test: `AIOrchestratorCoreLib.Tests/Bridge/NothingMovedBecomesInvisibleTests.cs` (one added case)

**Interfaces:** consumes `AppNote_Writer`.

**These five write to `channel.FilePath`, not to a resolved owner channel**, and the channel they write to may be a member's spoke or the general channel as well as an orchestration's owner channel. So they route directly rather than through the choke point, and the role and member id come from the channel.

**The one that matters most is `Refuse_Question` (13112): "your question was NOT sent to the owner — it is incomplete".** It is the only trace that a question died. It must be in the pack (Task 11), and Task 11 is the gate on shipping this task's sink change to anybody.

- [ ] **Step 1: Write the failing test**

Add to `NothingMovedBecomesInvisibleTests`:

```csharp
    /// <summary>
    /// THE FIVE COACHING NOTICES ROUTE, AND THE DEAD QUESTION IS WHY THE PACK SECTION IS NOT OPTIONAL.
    /// "your question was NOT sent to the owner — it is incomplete" is the ONLY trace that a question
    /// died: the owner never saw it, the log line is the operator's not the agent's, and the session
    /// believes it is waiting for an answer that nobody will ever give. A routed note that the pack
    /// does not carry converts a visible refusal into a silent hang.
    /// </summary>
    [Fact]
    public void EveryQuestionCoachingSiteRoutes_AndNoneOfThemStillCallsTheAppenderDirectly()
    {
        foreach (var method in new[]
        {
            "void Coach_OnContractFaults",
            "void Refuse_Question",
            "async Task Supersede_OlderQuestions_Async",
            "void Handle_ReaskOfADecidedQuestion",
            "void Handle_RepeatedQuestion",
        })
        {
            var body = Extract_Method(method);

            Assert.Contains("AppNote_Writer.Write", body, StringComparison.Ordinal);
            Assert.DoesNotContain("ChannelAppender.Append_AppEntry", body, StringComparison.Ordinal);
        }
    }
```

> The five signature marks above are as read on 2026-09-15; `grep -n "void Refuse_Question\|void Coach_OnContractFaults\|Supersede_OlderQuestions_Async\|Handle_ReaskOfADecidedQuestion\|Handle_RepeatedQuestion" AIOrchestratorCoreLib/Bridge/BridgeEngine/BridgeEngineModel.cs` and use what the file says. Decision 20: a scan that cannot find its method must fail, and `Extract_Method` throws when it cannot.

- [ ] **Step 2: Run it and watch it fail**

Run: `~/.dotnet/dotnet test AIOrchestratorCoreLib.Tests/AIOrchestratorCoreLib.Tests.csproj --filter "FullyQualifiedName~NothingMovedBecomesInvisibleTests"`
Expected: FAIL on the first method — `AppNote_Writer.Write` is not there.

- [ ] **Step 3: A helper for a discovered channel, then the five edits**

```csharp
    /// <summary>
    /// A bookkeeping note on a channel the mirror discovered, rather than on a channel resolved from
    /// a role. The role and the member the log belongs to come from the CHANNEL — an owner channel is
    /// the supervisor's (or a solo's, which shares it), and a spoke is its member's — because the note
    /// must land in the log the session that reads this channel will be handed.
    ///
    /// <para>
    /// A SOLO SHARES THE OWNER CHANNEL (MemberChannel_Locator), so an owner channel resolves to the
    /// supervisor's log even in a basic orchestration, where the supervisor's state file IS the solo's
    /// counterpart. That is the same conflation TurnSources_Resolver makes deliberately, and it is
    /// right for the same reason: one file, one session reading it, one log.
    /// </para>
    /// </summary>
    bool Route_ChannelNote(IDiscoveredChannel channel, Channels.StatusLog.AppNoteKinds kind, string subject, string body)
    {
        var role = channel.IsOwnerChannel ? SessionRoles.Supervisor : SessionRoles.Implementer;
        var memberId = channel.IsOwnerChannel ? SessionRoles.Supervisor.ToString().ToLowerInvariant() : channel.SpokeName;

        try
        {
            return Channels.StatusLog.AppNote_Writer.Write(
                channel.FilePath,
                Channels.StatusLog.StatusLog_Store.Get_File(_paths, role, channel.OrchId, memberId),
                _runnerConfigs.Get_ForRole(role),
                kind,
                Channels.AppEntryAudiences.Agent,
                subject,
                body,
                DateTime.Now);
        }
        catch (Exception exception)
        {
            _log.Log_Warning(channel.OrchId, $"Routing the '{subject}' note FAILED and it is lost — {exception.GetType().Name}: {exception.Message}");
            return false;
        }
    }
```

Then at each of the five, replace

```csharp
        ChannelAppender.Append_AppEntry(
            channel.FilePath,
            AppEntryAudiences.Agent,
            "<subject>",
            "<body>",
            DateTime.Now);
```

with

```csharp
        Route_ChannelNote(channel, Channels.StatusLog.AppNoteKinds.ContractCoaching, "<subject>", "<body>");
```

keeping every surrounding statement — the `_log.Log_Info` lines above them, and `Handle_RepeatedQuestion`'s `Raise_AwaitingAnswerFlag` below it — exactly as they are.

> **Verify `IDiscoveredChannel` has `SpokeName` and `OrchId`** — `MirrorText_Formatter.Format_Parts` uses both, so it does; confirm the property names on the interface rather than from that call.

- [ ] **Step 4: Run the tests and watch them pass**

Run: `~/.dotnet/dotnet test AIOrchestratorCoreLib.Tests/AIOrchestratorCoreLib.Tests.csproj --filter "FullyQualifiedName~NothingMovedBecomesInvisibleTests|FullyQualifiedName~Question|FullyQualifiedName~Contract"`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add AIOrchestratorCoreLib/Bridge/BridgeEngine/BridgeEngineModel.cs AIOrchestratorCoreLib.Tests/Bridge/NothingMovedBecomesInvisibleTests.cs
git commit -F /tmp/cm.txt   # "feat(bridge): the message-contract and question-dedup coaching route to the status log"
```

---

## Task 10: The notes still ride the next turn — read from the log

**Files:**
- Modify: `AIOrchestratorCoreLib/Running/PrintTurn_Trigger.cs` (`Select_AgentNotes`)
- Create: `AIOrchestratorCoreLib/Running/StatusNotes/StatusNotes_Bookkeeper.cs`
- Modify: `AIOrchestratorCoreLib/Running/WakeDecision/WakeDecision_Resolver.cs` (`With_AgentNotes`)
- Modify: `AIOrchestratorCoreLib/Running/SessionCursors/SessionCursors_Bookkeeper.cs` (`Advance`)
- Test: `AIOrchestratorCoreLib.Tests/Running/NotesRideFromTheStatusLogTests.cs`

**Interfaces:**
- Consumes: `StatusLog_Store` (Task 2).
- Produces: nothing a caller outside `Running/` sees. This is the task that answers the brief's question — *what happens to the riding-notes mechanism when the notes are no longer in the channel*.

**The answer, stated plainly:** the mechanism is unchanged and gains a second source. `Select_AgentNotes`'s rule (agent-tagged, not `turn_ended`, within 2 h, newest 5, not already delivered) stays where it is and is applied ONCE over the channel's entries and the log's entries together. The log is deliberately **not** made an `ITurnSource`: a source is addressable by the session (`PrintTurnPrompt_Builder.Describe_Contract` lists them, `Write_Reply_Async` checks replies against them), and `.status` is not a channel anybody may write to. It gets a cursor in `state.Cursors` and nothing else.

**Why it cannot start a turn:** it never reaches `Select_Pending`. `Is_Inbound` is false for `ChannelAuthors.App` for every role, so even if the log's entries were passed to the pending selector they would be filtered out. Notes are added by `With_AgentNotes` **after every wake-up rule has decided** — the existing comment there says so and it stays true.

- [ ] **Step 1: Write the failing test**

```csharp
// AIOrchestratorCoreLib.Tests/Running/NotesRideFromTheStatusLogTests.cs
using AIOrchestratorCoreLib.Channels;
using AIOrchestratorCoreLib.Channels.StatusLog;
using AIOrchestratorCoreLib.Running;
using AIOrchestratorCoreLib.Running.TurnCursor;
using Xunit;

namespace AIOrchestratorCoreLib.Tests.Running;

/// <summary>
/// THE RIDING NOTES SURVIVE THE MOVE. Before 2026-09-15 an app note was an entry in the session's own
/// channel and rode the next turn from there; plan 02 routes the bookkeeping to
/// <c>status.jsonl</c>, and the mechanism that carries it must follow — or every note plan 02 moves
/// becomes a note nothing delivers, which is a far worse failure than a noisy channel.
///
/// <para>
/// ONE RULE, TWO SOURCES. The window, the cap and the delivered-test stay in
/// <c>Select_AgentNotes</c>; what changes is that it is asked over both files' entries at once, so
/// the cap is applied to the merged set and not twice (CLAUDE.md decision 12).
/// </para>
/// </summary>
public class NotesRideFromTheStatusLogTests
{
    static readonly DateTime NOW = new(2026, 9, 15, 16, 0, 0, DateTimeKind.Local);

    const string CHANNEL =
        "## [1] FROM owner — 2026-09-15 15:38 — via Telegram\n\nfai tutto\n" +
        "## [2] FROM app — 2026-09-15 15:40 — [agent] an old-style note still in the channel\n\nnot migrated, still delivered\n";

    static bool NeverDelivered(Channels.ChannelEntry.IChannelEntry entry) => false;

    [Fact]
    public void ANoteInTheLogRidesExactlyAsOneInTheChannelDid()
    {
        var log = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName() + ".jsonl");

        StatusLog_Store.Append(log, "PLAN.md is behind your verdicts", "update it", NOW.AddMinutes(-19));
        StatusLog_Store.Append(log, $"{PrintTurn_Words.TURN_ENDED_SUBJECT} sup turn 3 — success", "r/sup/3", NOW.AddMinutes(-18));

        var merged = ChannelEntry_Parser.Parse_All(CHANNEL).Concat(StatusLog_Store.Read_Entries(log)).ToList();

        var notes = PrintTurn_Trigger.Select_AgentNotes(merged, NeverDelivered, NOW);

        // The channel's un-migrated note AND the log's advisory ride. turn_ended never does — it says
        // what the session already knows, and a turn started by the record of the previous turn would
        // be a loop with one member in it.
        Assert.Equal(
            ["[agent] an old-style note still in the channel", "[agent] PLAN.md is behind your verdicts"],
            notes.Select(note => note.Subject));

        File.Delete(log);
    }

    /// <summary>
    /// THE CAP IS APPLIED ONCE, OVER BOTH FILES. Asking each source for its newest five and
    /// concatenating gives ten, and ten notes at the head of a prompt is the growing-boot this whole
    /// series exists to shrink.
    /// </summary>
    [Fact]
    public void AtMostFiveNotesRide_AcrossBothFilesTogether()
    {
        var log = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName() + ".jsonl");

        for (var index = 1; index <= 6; index++)
            StatusLog_Store.Append(log, $"log note {index}", "body", NOW.AddMinutes(-30 + index));

        var channelText = string.Concat(Enumerable.Range(1, 6).Select(index =>
            $"## [{index}] FROM app — 2026-09-15 15:{index:00} — [agent] channel note {index}\n\nbody\n"));

        var merged = ChannelEntry_Parser.Parse_All(channelText).Concat(StatusLog_Store.Read_Entries(log)).ToList();

        Assert.Equal(PrintTurn_Trigger.MAXIMUM_AGENT_NOTES, PrintTurn_Trigger.Select_AgentNotes(merged, NeverDelivered, NOW).Count);

        File.Delete(log);
    }

    /// <summary>
    /// DELIVERED ONCE, NEVER AGAIN — and the record of that is the same identity cursor the channel
    /// uses, because the log's records are channel entries and <c>ChannelEntry_Digest</c> identifies
    /// them the same way. Nothing new was invented to remember a note.
    /// </summary>
    [Fact]
    public void ADeliveredLogNoteDoesNotRideTwice()
    {
        var log = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName() + ".jsonl");

        StatusLog_Store.Append(log, "your question was NOT sent to the owner", "it is incomplete", NOW.AddMinutes(-5));

        var entries = StatusLog_Store.Read_Entries(log);
        var delivered = entries.Select(ChannelEntry_Digest.Compute).ToHashSet();

        Assert.Single(PrintTurn_Trigger.Select_AgentNotes(entries, NeverDelivered, NOW));
        Assert.Empty(PrintTurn_Trigger.Select_AgentNotes(entries, entry => delivered.Contains(ChannelEntry_Digest.Compute(entry)), NOW));

        File.Delete(log);
    }
}
```

- [ ] **Step 2: Run it and watch it fail**

Run: `~/.dotnet/dotnet test AIOrchestratorCoreLib.Tests/AIOrchestratorCoreLib.Tests.csproj --filter "FullyQualifiedName~NotesRideFromTheStatusLogTests"`
Expected: compile error — `Select_AgentNotes` takes an `ITurnCursor`, not a predicate.

- [ ] **Step 3: Change `Select_AgentNotes` to take the delivered-test**

```csharp
    /// <summary>
    /// The app notes this session has not been shown, to ride the turn about to start — never to
    /// start one. Only notes stamped within <see cref="AGENT_NOTE_WINDOW"/>, and only the newest
    /// <see cref="MAXIMUM_AGENT_NOTES"/>.
    ///
    /// <para>
    /// TWO FILES SINCE 2026-09-15, ONE RULE. A note used to be an entry in the session's own channel
    /// and nothing else; plan 02 routes the bookkeeping kinds to
    /// <see cref="Channels.StatusLog.StatusLog_Store"/>, whose records ARE channel entries, and the
    /// caller hands both lists here together. The cap and the window therefore apply to the merged
    /// set, which is the point: five notes total, not five per file.
    /// </para>
    /// <para>
    /// THE DELIVERED TEST IS A PREDICATE RATHER THAN A CURSOR, and that is the only reason the
    /// signature changed. There are two cursors now — the session's own channel and
    /// <see cref="Channels.StatusLog.StatusLog_Store.CURSOR_KEY"/> — and an entry is already
    /// delivered if EITHER says so. Passing one cursor would have forced this method to know which
    /// list an entry came from, which is exactly the knowledge it must not need.
    /// </para>
    /// </summary>
    public static IReadOnlyList<IChannelEntry> Select_AgentNotes(
        IReadOnlyList<IChannelEntry> entries,
        Func<IChannelEntry, bool> alreadyDelivered,
        DateTime nowLocal)
    {
        List<IChannelEntry> notes = [];

        foreach (var entry in entries)
        {
            if (!Is_AgentNote(entry))
                continue;

            if (alreadyDelivered(entry))
                continue;

            // The app writes this stamp itself (ChannelAppender, StatusLog_Store), so unlike an
            // agent's it can be read. One that cannot be parsed is not trusted to be recent.
            if (!DateTime.TryParseExact(entry.DateText, "yyyy-MM-dd HH:mm", System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out var stampedLocal))
                continue;

            if (nowLocal - stampedLocal > AGENT_NOTE_WINDOW)
                continue;

            notes.Add(entry);
        }

        return notes.Count <= MAXIMUM_AGENT_NOTES ? notes : [.. notes.Skip(notes.Count - MAXIMUM_AGENT_NOTES)];
    }
```

Update the existing call in `WakeDecision_Resolver` and the four cases in `PrintTurnTriggerTests` to pass `entry => cursor.Delivered.Contains(ChannelEntry_Digest.Compute(entry))`.

- [ ] **Step 4: Write the bookkeeper**

```csharp
// AIOrchestratorCoreLib/Running/StatusNotes/StatusNotes_Bookkeeper.cs
using AIOrchestratorCoreLib.Channels;
using AIOrchestratorCoreLib.Channels.ChannelEntry;
using AIOrchestratorCoreLib.Channels.StatusLog;
using AIOrchestratorCoreLib.Running.PrintSessionState;
using AIOrchestratorCoreLib.Running.TurnCursor;
using AIOrchestratorCoreLib.SupervisionPaths;

namespace AIOrchestratorCoreLib.Running.StatusNotes;

/// <summary>
/// THE STATUS LOG'S SIDE OF THE NOTE BOOKKEEPING — its entries, its cursor, and the advance that
/// records what rode a turn.
///
/// <para>
/// IT IS NOT AN <see cref="TurnSource.ITurnSource"/> AND MUST NOT BECOME ONE. A source is ADDRESSABLE:
/// <c>PrintTurnPrompt_Builder.Describe_Contract</c> lists them to the session as the channels it may
/// reply to, and <c>Write_Reply_Async</c> checks a reply's address against them. Nobody may reply to
/// the status log, and listing it would put a fake channel in front of every session for nothing. It
/// gets a CURSOR, under <see cref="StatusLog_Store.CURSOR_KEY"/>, which is all the delivery record it
/// needs and which persists for free in the state file's existing <c>cursors</c> array.
/// </para>
/// <para>
/// AND IT CANNOT START A TURN. <see cref="PrintTurn_Trigger.Is_Inbound"/> is false for
/// <see cref="ChannelAuthors.App"/> for every role, so nothing here is ever pending; the notes are
/// added by <c>WakeDecision_Resolver.With_AgentNotes</c> AFTER every wake-up rule has decided, which
/// is where that guarantee already lived and where it stays.
/// </para>
/// </summary>
public static class StatusNotes_Bookkeeper
{
    static readonly StringComparer SOURCE_KEYS = StringComparer.OrdinalIgnoreCase;

    public static IReadOnlyList<IChannelEntry> Read_Entries(ISupervisionPaths paths, IPrintSessionState state)
    {
        return StatusLog_Store.Read_Entries(StatusLog_Store.Get_File(paths, state.Role, state.OrchId, state.MemberId));
    }

    public static ITurnCursor? Find_Cursor_OrNull(IPrintSessionState state)
    {
        return state.Cursors.FirstOrDefault(cursor => SOURCE_KEYS.Equals(cursor.SourceKey, StatusLog_Store.CURSOR_KEY));
    }

    /// <summary>
    /// Whether this entry has already been handed to the session, asked of the log's cursor. A session
    /// with no such cursor yet has been handed nothing from it — which is the right answer for a
    /// session that existed before the log did, and the reason this is not a baseline: baselining
    /// would absorb as history the very notes the first turn after the upgrade should carry.
    /// </summary>
    public static bool Is_Delivered(IPrintSessionState state, IChannelEntry entry)
    {
        return Find_Cursor_OrNull(state)?.Delivered.Contains(ChannelEntry_Digest.Compute(entry)) == true;
    }

    /// <summary>
    /// The log's cursor after <paramref name="handed"/> rode a turn, pruned to the records still in the
    /// file — the same prune <see cref="TurnCursor_Factory.CreateFrom_Delivered"/> performs for a
    /// channel, and it absorbs the log's trimming exactly as it absorbs compaction (decision 13).
    /// </summary>
    public static ITurnCursor Advance(
        ISupervisionPaths paths,
        IPrintSessionState state,
        IReadOnlyList<IChannelEntry> handed)
    {
        var logFile = StatusLog_Store.Get_File(paths, state.Role, state.OrchId, state.MemberId);
        var live = StatusLog_Store.Read_Entries(logFile);

        var cursor = Find_Cursor_OrNull(state)
            ?? TurnCursor_Factory.Create(StatusLog_Store.CURSOR_KEY, logFile, 0, new HashSet<string>());

        return TurnCursor_Factory.CreateFrom_Delivered(cursor, state.Role, live, handed);
    }
}
```

- [ ] **Step 5: Merge the two sources in `With_AgentNotes`**

```csharp
    static IReadOnlyList<PendingEntry> With_AgentNotes(ISupervisionPaths paths, IPrintSessionState state, IReadOnlyList<ITurnSource> sources, IReadOnlyList<PendingEntry> ordered, DateTime nowLocal)
    {
        if (ordered.Count == 0)
            return ordered;

        var own = sources.FirstOrDefault(source => string.Equals(source.ChannelFilePath, state.ChannelFilePath, StringComparison.OrdinalIgnoreCase));

        if (own == null)
            return ordered;

        var ownCursor = state.Cursors.FirstOrDefault(candidate => SOURCE_KEYS.Equals(candidate.SourceKey, own.Key));

        // BOTH FILES, ONE RULE, ONE CAP. The channel still holds every note written before this
        // machine's bookkeeping was routed, and nothing migrates them — so for the whole of the
        // transition the two lists are read together and the newest five of the pair ride.
        var channelEntries = ChannelHistory_Cache.Read_Entries(own.ChannelFilePath);
        var logEntries = StatusNotes_Bookkeeper.Read_Entries(paths, state);

        var notes = PrintTurn_Trigger.Select_AgentNotes(
            [.. channelEntries, .. logEntries],
            entry => ownCursor?.Delivered.Contains(ChannelEntry_Digest.Compute(entry)) == true
                || StatusNotes_Bookkeeper.Is_Delivered(state, entry),
            nowLocal);

        if (notes.Count == 0)
            return ordered;

        // The notes are labelled under the session's OWN channel, as they always were: the prompt
        // groups traffic by source and the session has never been told the status log exists.
        return [.. notes.Select(note => new PendingEntry(own, note)), .. ordered];
    }
```

`With_AgentNotes` now needs `ISupervisionPaths`. `Resolve_OrNull` and `Decide_OrNull` already receive what they need to reach it — `grep -n "ISupervisionPaths" AIOrchestratorCoreLib/Running/WakeDecision/WakeDecision_Resolver.cs` and thread it from whichever entry point has it; **do not construct a second `SupervisionPathsModel`**.

- [ ] **Step 6: Advance the log's cursor with the rest**

In `SessionCursors_Bookkeeper.Advance`, after the `foreach (var source in sources)` loop:

```csharp
        // THE LOG'S CURSOR, ADVANCED WITH THE SAME SET. A note that rode this turn is delivered
        // exactly as a channel entry is, and it must be recorded in the SAME write — the dispatcher
        // folds this whole list into one state-file write, and a note recorded in a second write
        // could survive a crash that lost the turn, or the reverse.
        //
        // WHICH OF THE PENDING ENTRIES CAME FROM THE LOG IS ASKED OF THE LOG, not remembered: the
        // notes are labelled under the session's own source (With_AgentNotes) so that the prompt
        // reads right, and re-deriving here is one read of a small file against carrying a flag
        // through five signatures.
        var logDigests = StatusNotes_Bookkeeper.Read_Entries(paths, state).Select(ChannelEntry_Digest.Compute).ToHashSet();
        var handedFromLog = pending.Where(item => logDigests.Contains(ChannelEntry_Digest.Compute(item.Entry))).Select(item => item.Entry).ToList();

        advanced.Add(StatusNotes_Bookkeeper.Advance(paths, state, handedFromLog));

        // A cursor for a key no source resolved is never dropped (see Read_AndPersist's own note);
        // the log's key is never a source, so it is added here and only here.
```

`Advance` gains an `ISupervisionPaths paths` first parameter. Its two callers are `PrintTurnDispatcherModel.Advance_Cursors` (which has `_paths`) and the wake-ticket sweep in `BridgeEngineModel` (which has `_paths`). **Also make sure `Read_AndPersist`'s "a cursor is never dropped for a source that did not resolve" loop does not now add a SECOND `.status` cursor** — it iterates `state.Cursors` and keeps any whose key matched no source, which `.status` never does. Guard it:

```csharp
        foreach (var cursor in state.Cursors)
        {
            if (SOURCE_KEYS.Equals(cursor.SourceKey, StatusLog_Store.CURSOR_KEY))
                continue;   // not a source; owned by StatusNotes_Bookkeeper and re-added by Advance

            if (!sources.Any(source => SOURCE_KEYS.Equals(source.Key, cursor.SourceKey)))
                cursors.Add(cursor);
        }
```

**and then re-add it unchanged** at the end of that same list, so a read that persists does not drop it:

```csharp
        var statusCursor = StatusNotes_Bookkeeper.Find_Cursor_OrNull(state);

        if (statusCursor != null)
            cursors.Add(statusCursor);
```

- [ ] **Step 7: Run the tests and watch them pass**

Run: `~/.dotnet/dotnet test AIOrchestratorCoreLib.Tests/AIOrchestratorCoreLib.Tests.csproj --filter "FullyQualifiedName~NotesRideFromTheStatusLogTests|FullyQualifiedName~PrintTurnTriggerTests|FullyQualifiedName~AgentNotesRideTheNextTurnTests|FullyQualifiedName~SessionCursors|FullyQualifiedName~WakeDecision"`
Expected: PASS. `AgentNotesRideTheNextTurnTests` is the existing end-to-end oracle and must be green with no edits to its assertions — it runs under the default sink, where notes are still channel entries.

- [ ] **Step 8: Commit**

```bash
git add AIOrchestratorCoreLib/Running/PrintTurn_Trigger.cs AIOrchestratorCoreLib/Running/StatusNotes/ AIOrchestratorCoreLib/Running/WakeDecision/WakeDecision_Resolver.cs AIOrchestratorCoreLib/Running/SessionCursors/SessionCursors_Bookkeeper.cs AIOrchestratorCoreLib.Tests/Running/
git commit -F /tmp/cm.txt   # "feat(running): the app's notes ride the next turn from the status log as well as the channel, one rule and one cap"
```

---

## Task 11: The pack carries what is still standing

**Files:**
- Modify: `AIOrchestratorCoreLib/Running/StatePack/StatePackInputs.cs`, `StatePackInputs_Reader.cs`, `StatePack_Builder.cs`
- Test: `AIOrchestratorCoreLib.Tests/Running/StatePackCarriesStandingNotesTests.cs`

**Interfaces:**
- Consumes: `StatusNotes_Bookkeeper` (Task 10).
- Produces: `StatePackInputs.StandingNotes` and the pack's `## What the app has told you` section.

**Why the pack AND the riding notes, and not one of them:** the riding notes are the app's newest words to a session that is ALREADY running; the pack is what a FRESH session reads instead of its channel, and a fresh session has no cursor of its own to have missed anything. Under `resume = fresh` — which the spec's step 6 wants for the supervisor — the riding notes alone would deliver a ledger advisory once, to a session that then forgets it. The pack section is what makes the advisory still true tomorrow.

**The cap is the same five.** A section that grows is the boot this whole series shrinks.

- [ ] **Step 1: Write the failing test**

```csharp
// AIOrchestratorCoreLib.Tests/Running/StatePackCarriesStandingNotesTests.cs
using AIOrchestratorCoreLib.Channels;
using AIOrchestratorCoreLib.Channels.StatusLog;
using AIOrchestratorCoreLib.Running;
using AIOrchestratorCoreLib.Running.StatePack;
using Xunit;

namespace AIOrchestratorCoreLib.Tests.Running;

/// <summary>
/// A FRESH SESSION IS TOLD WHAT THE APP TOLD ITS PREDECESSOR. The riding notes reach a session that is
/// already running and has a cursor; the pack is what a session reads when it has neither. Without
/// this section, routing the bookkeeping would mean a supervisor on <c>resume = fresh</c> never
/// learning that its ledger is behind, that its question was refused for being incomplete, or that a
/// member has gone deaf — three things whose ONLY trace was the app entry plan 02 moved.
/// </summary>
public class StatePackCarriesStandingNotesTests
{
    static readonly DateTime NOW = new(2026, 9, 15, 16, 0, 0, DateTimeKind.Local);

    [Fact]
    public void TheStandingNotesAppearUnderTheirOwnHeading_NewestLast()
    {
        var log = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName() + ".jsonl");

        StatusLog_Store.Append(log, "PLAN.md is behind your verdicts", "Update PLAN.md.", NOW.AddMinutes(-20));
        StatusLog_Store.Append(log, "your question was NOT sent to the owner — it is incomplete", "Ask again with every line present.", NOW.AddMinutes(-4));

        var text = StatePack_Builder.Build(Inputs(StatusLog_Store.Read_Entries(log)));

        Assert.Contains(StatePack_Builder.STANDING_NOTES_HEADING, text, StringComparison.Ordinal);
        Assert.True(
            text.IndexOf("PLAN.md is behind your verdicts", StringComparison.Ordinal)
            < text.IndexOf("your question was NOT sent", StringComparison.Ordinal),
            "the notes are out of order — the newest must read last, as the channel's do");

        File.Delete(log);
    }

    /// <summary>
    /// turn_ended NEVER APPEARS. It is the largest family in the log by far — it is written once per
    /// turn of every session — and it says what the session already knows. A pack that carried it
    /// would put this series' own saving straight back into the boot it was taken out of.
    /// </summary>
    [Fact]
    public void TheTurnRecordsAreNotInThePack()
    {
        var log = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName() + ".jsonl");

        for (var turn = 1; turn <= 20; turn++)
            StatusLog_Store.Append(log, $"{PrintTurn_Words.TURN_ENDED_SUBJECT} imp-1 turn {turn} — success", $"request_id: r/imp-1/{turn}", NOW.AddMinutes(-turn));

        var text = StatePack_Builder.Build(Inputs(StatusLog_Store.Read_Entries(log)));

        Assert.DoesNotContain(PrintTurn_Words.TURN_ENDED_SUBJECT, text, StringComparison.Ordinal);
        Assert.DoesNotContain(StatePack_Builder.STANDING_NOTES_HEADING, text, StringComparison.Ordinal);

        File.Delete(log);
    }

    [Fact]
    public void ASessionWithNoNotes_GetsNoSection()
    {
        Assert.DoesNotContain(StatePack_Builder.STANDING_NOTES_HEADING, StatePack_Builder.Build(Inputs([])), StringComparison.Ordinal);
    }

    static StatePackInputs Inputs(IReadOnlyList<Channels.ChannelEntry.IChannelEntry> standingNotes)
    {
        return new StatePackInputs(
            orchId: "repo-1", memberId: "imp-1", role: SessionRoles.Implementer, requestId: "repo-1/imp-1/7",
            pending: [], sources: [], brief: null, lastOwnEntry: null, ledgerLines: [], planText: null,
            gitLines: [], ownerTail: [], unavailable: [], progressNote: null, standingNotes: standingNotes);
    }
}
```

> The constructor argument list above is as read on 2026-09-15. Read `StatePackInputs.cs` and append `standingNotes` as the LAST parameter with a default of `null` mapped to `[]`, so no existing construction changes; adjust this test to whatever the file actually takes before running it.

- [ ] **Step 2: Run it and watch it fail**

Run: `~/.dotnet/dotnet test AIOrchestratorCoreLib.Tests/AIOrchestratorCoreLib.Tests.csproj --filter "FullyQualifiedName~StatePackCarriesStandingNotesTests"`
Expected: compile error — `StandingNotes` and `STANDING_NOTES_HEADING` do not exist.

- [ ] **Step 3: Add the input**

In `StatePackInputs.cs`, append the parameter and the property:

```csharp
    /// <summary>
    /// WHAT THE APP HAS TOLD THIS SESSION AND NOBODY HAS ACTED ON — the ledger advisory, the orphan
    /// report, the refused question. Read from <see cref="Channels.StatusLog.StatusLog_Store"/>, which
    /// is where plan 02 routed the bookkeeping that used to sit in the channel and be re-read at every
    /// boot.
    ///
    /// <para>
    /// The turn RECORDS are excluded (<c>turn_ended</c>): they are the largest family in the log and
    /// they say what the session already knows. The exclusion is
    /// <see cref="PrintTurn_Trigger.Is_AgentNote"/>'s, which already draws exactly this line and draws
    /// it for the riding notes too — one rule, two readers (decision 12).
    /// </para>
    /// </summary>
    public IReadOnlyList<IChannelEntry> StandingNotes { get; } = standingNotes ?? [];
```

- [ ] **Step 4: Render it**

In `StatePack_Builder.cs`:

```csharp
    public const int STANDING_NOTES_CAP = 4_000;

    public const string STANDING_NOTES_HEADING = "## What the app has told you (not the owner's words — act on them, do not answer them)";
```

and, immediately **before** the `PENDING_HEADING` block — so the trigger stays last, as the type header requires:

```csharp
        if (inputs.StandingNotes.Count > 0)
        {
            var notes = string.Join("\n\n", inputs.StandingNotes.Select(note => note.RawText.Trim()));

            Append_Block(text, STANDING_NOTES_HEADING, notes, STANDING_NOTES_CAP, Channels.StatusLog.StatusLog_Store.FILE_NAME, keepTail: true);
        }
```

`keepTail: true` for the same reason the progress note uses it: the newest notes are the ones still standing, and a cap that dropped them would leave the session with the oldest.

- [ ] **Step 5: Fill it in the reader**

In `StatePackInputs_Reader.Read`, beside the other reads:

```csharp
        // AT MOST THE NEWEST FIVE, the same cap the riding notes use, and through the same predicate:
        // a pack that grew a line per turn would be the growing boot this series exists to shrink.
        var standingNotes = StatusNotes_Bookkeeper.Read_Entries(paths, state)
            .Where(PrintTurn_Trigger.Is_AgentNote)
            .TakeLast(PrintTurn_Trigger.MAXIMUM_AGENT_NOTES)
            .ToList();
```

and pass `standingNotes: standingNotes` into the `StatePackInputs` construction.

**A failure to read the log is not a section in `Unavailable`.** An absent log means a session that has been told nothing, which is the ordinary case and not a gap; `StatusLog_Store.Read_Entries` already answers `[]` for both. Naming it in `Unavailable` would put "the app's notes could not be read" in front of every session on every machine that has never set the key.

- [ ] **Step 6: Run the tests and watch them pass**

Run: `~/.dotnet/dotnet test AIOrchestratorCoreLib.Tests/AIOrchestratorCoreLib.Tests.csproj --filter "FullyQualifiedName~StatePack"`
Expected: PASS — the three new cases and every existing `StatePack` test unchanged.

- [ ] **Step 7: Say it once in the kit**

In each of `kit/skills/{supervisor,implementer,reviewer,solo,general-supervisor}/SKILL.md`, in the section that already tells the session to read its pack, add exactly one sentence:

> A `## What the app has told you` section in the pack is the APP speaking to you, not the owner — act on it, never answer it as though the owner had written it.

**One sentence, one wording, five files.** It restates `PrintTurnPrompt_Builder.AGENT_NOTES_LINE`, which already exists for the riding notes; if the two ever need to differ, the prompt's is the one that governs a running turn.

> **`kit/skills/supervisor/SKILL.md` may be being edited in parallel.** Check `git status` before touching it, and if it is dirty from other work, add the sentence in a separate commit after that work lands rather than resolving a conflict inside a file you did not write.

- [ ] **Step 8: Commit**

```bash
git add AIOrchestratorCoreLib/Running/StatePack/ AIOrchestratorCoreLib.Tests/Running/StatePackCarriesStandingNotesTests.cs kit/skills/
git commit -F /tmp/cm.txt   # "feat(running): the state pack carries the app's standing notes, so a fresh session is not told nothing"
```

---

## Task 12: The registers, the measurement, and the full suite

**Files:**
- Modify: `AIOrchestratorCoreLib.Tests/Bridge/PauseGatesEveryWakerScanTests.cs`
- Modify: `AIOrchestratorCoreLib.Tests/Channels/AppAuthoredWritesCensusTests.cs` (the count, if the routing changed it)
- Modify: `tools/wake-baseline/baseline.py`
- Test: `tools/wake-baseline/test_baseline.py`

**Interfaces:** consumes everything above. Produces the number that proves the plan.

- [ ] **Step 1: Add the router's row to the pause register**

```csharp
        // THE NOTE ROUTER (plan 02, 2026-09-15). It writes to a channel on its default branch, so it
        // is a waker like any other — and the two engine methods that reach it are the ones below.
        // `Route_SupervisorNote` sits inside `Append_SupervisorAttention_UnlessMeeting`, which already
        // has its own row; `Route_ChannelNote` does not, and it writes to a MEMBER's spoke, which the
        // choke point never touches.
        { "bool Route_ChannelNote", "AppNote_Writer.Write" },
```

The scan asserts `session.Paused` appears in the body. `Route_ChannelNote` does not have a session — so either give it the screen:

```csharp
        if (_store.Get_Session_OrNull(channel.OrchId) is { } session && session.Paused)
            return false;
```

or, if the five coaching sites are already only reachable from a path that screened the pause, say so in the row's anchor and add the screen anyway. **Add it.** The decision's sentence is "miss one and dormancy is a word", and a contract-coaching note landing in a paused orchestration's member channel is a poke at a session the owner put to sleep.

- [ ] **Step 2: Run the register**

Run: `~/.dotnet/dotnet test AIOrchestratorCoreLib.Tests/AIOrchestratorCoreLib.Tests.csproj --filter "FullyQualifiedName~PauseGatesEveryWakerScanTests"`
Expected: PASS, one more theory case than before.

- [ ] **Step 3: Teach the baseline script what the routing saves**

`tools/wake-baseline/baseline.py` already counts entries by author across live files and archives. Add one figure — the app entries that plan 02 would take out of a boot read — and a test for it:

```python
# tools/wake-baseline/test_baseline.py  (add)
def test_counts_the_bookkeeping_a_boot_read_no_longer_carries():
    with tempfile.TemporaryDirectory() as d:
        root = pathlib.Path(d)
        ch = root / "repo-1"; ch.mkdir()
        (ch / "owner-channel.md").write_text(
            "## [1] FROM app — 2026-09-01 10:00 — [agent] turn_ended sup turn 1 — success\nbody\n\n"
            "## [2] FROM app — 2026-09-01 10:01 — [agent] PLAN.md is behind your verdicts\nbody\n\n"
            "## [3] FROM app — 2026-09-01 10:02 — orchestration 'repo-1' started\nbody\n\n"
            "## [4] FROM app — 2026-09-01 10:03 — [agent] unread traffic — you have not answered\nbody\n")
        out = subprocess.run(
            [sys.executable, "tools/wake-baseline/baseline.py", "--root", str(root), "--format", "json"],
            capture_output=True, text=True, check=True).stdout
        got = json.loads(out)
        # Owner-facing app entries are never routed; nor is a nudge (the orphan escalation counts it).
        assert got["bookkeeping_out_of_the_boot_read"] == 2
```

```python
# tools/wake-baseline/baseline.py  (add to collect())
# THE SUBJECTS PLAN 02 ROUTES, matched on the agent tag plus a family word. It is a HEURISTIC over a
# file and says so: the app's routing decision is made in C# at the call site by AppNoteKinds, and
# nothing in a channel file records which site wrote an entry. It is good enough for a BEFORE figure
# and must never be used to decide anything at run time.
ROUTED_MARKERS = ("turn_ended", "turn stalled", "turn waiting on a usage limit",
                  "turns killed at the deadline", "an earlier final message was superseded",
                  "reply not addressable", "PLAN.md", "message contract", "your question",
                  "your earlier open question", "you already asked this", "this question is already open",
                  "has gone deaf", "new traffic is waiting on a usage limit")
...
    routed = 0
...
            if author == "app" and h["subject"].lstrip().lower().startswith("[agent]"):
                if any(marker.lower() in h["subject"].lower() for marker in ROUTED_MARKERS):
                    routed += 1
...
    return {..., "bookkeeping_out_of_the_boot_read": routed}
```

Run: `python3 -m pytest tools/wake-baseline/test_baseline.py -q`
Expected: PASS, 3 tests.

- [ ] **Step 4: Re-run the whole suite — the dispatching session, not a sub-agent**

Run: `~/.dotnet/dotnet test AIOrchestratorCoreLib.Tests/AIOrchestratorCoreLib.Tests.csproj`
Expected: **3 955 + the new cases passed, 10 skipped, 0 failed**, ~3 min 20 s. A red in `ClosingTurnReviewFixTests`, `TolerantFileReaderTests` or `EffortDialOnABridgeDrivenSupervisorTests` is re-run alone 3× before it is a finding; anything else is yours.

- [ ] **Step 5: Re-run the census and reconcile**

Run: `~/.dotnet/dotnet test AIOrchestratorCoreLib.Tests/AIOrchestratorCoreLib.Tests.csproj --filter "FullyQualifiedName~AppAuthoredWritesCensusTests"`

17 of the 77 sites now call `AppNote_Writer.Write` instead of an appender, so the raw count has changed. **Update the constants and write the new breakdown into the report**: how many sites call the router, how many still call an appender directly, and that the two add to 77 plus the helper bodies. If they do not add up, a site was converted twice or a new one was added; find it before committing.

- [ ] **Step 6: Commit**

```bash
git add AIOrchestratorCoreLib.Tests/Bridge/PauseGatesEveryWakerScanTests.cs AIOrchestratorCoreLib.Tests/Channels/AppAuthoredWritesCensusTests.cs AIOrchestratorCoreLib/Bridge/BridgeEngine/BridgeEngineModel.cs tools/wake-baseline/
git commit -F /tmp/cm.txt   # "test(bridge): the router is on the pause register and the baseline counts what leaves the boot read"
```

---

## Acceptance for the whole plan

**Done when all of these are true and the report says so with the command that proved each:**

1. `~/.dotnet/dotnet test AIOrchestratorCoreLib.Tests/AIOrchestratorCoreLib.Tests.csproj` exits 0 with no red outside the three named flakes.
2. `grep -rn --include='*.cs' "ChannelAppender.Append_AppEntry(" AIOrchestratorCoreLib/Running/PrintTurnDispatcher/` returns nothing: all eight dispatcher sites go through the router.
3. `AppAuthoredWritesCensusTests` passes at the new count, and the report states the new split.
4. With `{"runners":{"supervisor":{"runner":"print","bookkeeping":"log"}}}`, a supervisor's `owner-channel.md` gains **no** `[agent]`-tagged app entry for the routed kinds over a full turn, and `.supervisor.status.jsonl` gains them instead.
5. With the same config, the supervisor's next turn prompt still carries the notes (`AGENT_NOTES_LINE` present) and its pack carries `## What the app has told you`.
6. With the default config, a byte-for-byte comparison of a channel after a turn is **identical** to one taken before this plan. This is the compatibility claim and it is the one worth measuring rather than asserting.
7. `AnAppEntryStillCountsAsInbound_BecauseEscalationDependsOnIt` is green, and `TheMemberNudgeIsNotRouted…` says why.

**The AFTER measurement cannot be taken on this machine** and the report must say so rather than estimate it, exactly as plan 01's did. It needs `bookkeeping: log` set for one role on a machine that runs orchestrations, two days, and `python3 tools/wake-baseline/baseline.py --root ~/.claude/supervision` re-run.

---

## Open questions for the owner

1. **`STATUS` stays in the channel — is that acceptable?** The spec names it as one of the five kinds that move. It is the away digest, its audience is `Owner`, it is mirrored to the phone (`MirrorText_Formatter` has a named exception for it so the BODY is sent rather than the subject), and `Post_StatusEntry`'s docstring says it rides the channel precisely so Do-Not-Disturb can queue and collapse it. Moving it means reimplementing Normal/Deferred/Silenced outside the mirror. **Recommendation: leave it.** Its cost under `wake = ticket` is zero — `Is_Inbound` excludes the app — and its cost under `wake = watcher` is one wake per away slot per orchestration, which is small and which step 2 of the series removes anyway. *This plan assumes the answer is "leave it" and does not move it.*

2. **Do the 14 agent-facing REQUEST CONFIRMATIONS stay in the channel?** Sites like "promotion REFUSED — file your HANDOVER entry first" and "your parked request cannot be put to the owner yet" are the app's answer to something the agent asked for, and CLAUDE.md decision 7 calls that confirmation a first-class channel entry that wakes the requester. This plan treats them as conversation and leaves them. If the owner wants them routed too, they are one more `AppNoteKinds` value and 14 edits — **but under `wake = watcher` a routed confirmation would no longer wake the requester**, which is a change to decision 7 and not a refactor.

3. **Should the log be mirrored into the app's live view?** The WPF panel and the Telegram mirror both read channels. Nothing reads the status log today except the sessions themselves, so a supervisor's ledger advisory becomes invisible to a human watching the app. This plan does not address it; `orchestrator.log.jsonl` still carries the operator's copy of each of these events, which is where the app already looks.

4. **How long should a note stand in the pack?** Task 11 caps the section at the newest five notes with no age limit, while the riding notes have a 2 h window. A ledger advisory from three days ago is probably stale; a refused question is not. **The plan ships the simple rule and flags this**: if the owner wants an age limit, it belongs in `StatePackInputs_Reader`, one line, and it must NOT be a second copy of `AGENT_NOTE_WINDOW`.

## PARKED (decision 22 — written down, outside the denominator)

- `Refuse_Question`'s "your question was NOT sent to the owner" is currently the ONLY signal that a question died: nothing raises the awaiting-answer flag down, nothing times it out, and the session waits. Making the refusal a first-class state rather than a note is a separate concern and belongs to whoever owns the question lifecycle.
- `MirrorText_Formatter.Is_AgentCoaching` is a second copy of the audience decision, matching a claim's WORDING; its own comment says it "stays only until every writer is tagged". Every writer IS tagged now. Deleting it is one commit and is not this plan's.
- `StatusLog_Store.Append` re-reads the file to compute its index on every append. That is one read per bookkeeping line, which is cheap against the channel append it replaces (which takes a cross-process lock and reads the whole channel), but it is not free. A cached counter is an optimisation, not a fix.

## What is deliberately NOT in this plan

- **No deletion.** The channel-writing branch of every routed site is still there and is still the default. The deletion — if it ever happens — belongs after the same two-day soak the spec demands for the watcher, and it is not obviously worth doing at all: the branch IS the compatibility guarantee.
- **No migration of the entries already in the channels.** They stay, they are still read, and Task 10's merged read is what makes that true for the whole transition.
- **No change to `channel-append.sh`, `ChannelWrite_Lock`, `Channel_Compactor` or the entry format.**
- **No change to Telegram, to the delivery modes, or to the question hold.**
- **No change to what wakes anybody.** Every entry this plan moves was already excluded from `Is_Inbound`; the measured figure is that zero turns in a nine-day window were started by an app entry. This plan shrinks the BOOT READ, not the wake count — the wake count is plan 01's saving and the spec's step 2.
