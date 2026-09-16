# One Wake Model — Plan 03: the routed report

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** A fix round stops costing the supervisor a wake-up to act as a postman. Its round-one
verdict may carry a **re-review contract**; when the named implementer files the fix report that
contract asks for, **the app** relays the delta and the supervisor's own re-review brief into the
reviewer's channel, and holds the fix report so the supervisor is woken ONCE — at the re-verdict —
instead of twice.

**Architecture:** Two new marker words in the channel grammar (`REROUTE:` for the supervisor,
`FIXED:` for the implementer), a small durable contract file per orchestration, one new per-tick
sweep in `BridgeEngineModel` that writes the relay entry, and exactly TWO surgical changes in
`PrintTurn_Trigger`: an ENTRY-level `Is_Inbound` overload that admits one kind of app entry for one
role, and one more exclusion in `Is_AgentNote`. The supervisor's side of the saving is a hold — the
fix report **rides** the supervisor's next turn and never **starts** one — which is the same rule
app notes already live by, implemented in the one place the wake decision now lives
(`WakeDecision_Resolver.Decide_OrNull`, plan 01).

**Tech Stack:** C# / .NET 10 (the lib is platform-neutral), xUnit, bash + JSON (kit grammar and
skills).

**Spec:** `docs/superpowers/specs/2026-09-15-one-wake-model-design.md`, step 4.
**Depends on:** plan 01 (`2026-09-15-one-wake-model-01-ticket-and-decider.md`) — shipped,
report at `2026-09-15-one-wake-model-01-report.md`. Nothing here depends on plan 02.

---

## Global Constraints

- **THE MEMBERS NEVER WRITE TO EACH OTHER. NOT ONCE, NOT ANYWHERE IN THIS PLAN.** Hub-and-spoke
  (CLAUDE.md decision 4) is the architecture, and the relay entry is written by the APP into the
  reviewer's own channel, signed `app`, exactly like every other app entry. No new channel, no new
  edge, no member handed another member's path. If a task in this plan seems to want a member to
  append somewhere that is not its own channel, the task is wrong.
- **Review independence is the product and is untouched.** The reviewer keeps no write tools, no
  stake and no interest; its findings remain INPUT to the supervisor's verdict and never a verdict.
  The relay carries the supervisor's own words and the implementer's own words, verbatim — the app
  composes no judgement of any kind. (`kit/skills/reviewer/SKILL.md`, "Governance — you have no
  stake".)
- **Coding patterns:** `AIOrchestratorCoreLib` is STRICT — interface + `Model` + `_Factory` triples,
  immutable types, no mutable public state. Follow `Running/WakeDecision/` and
  `Running/WakeTicket/`, written by plan 01, as the house style for everything new here.
- **No channel-format change.** `ChannelAppender`, `kit/bin/channel-append.sh`, the lock and
  `Channel_Compactor` are untouched. Two marker WORDS are added to the grammar data file, which is
  what that file is for.
- **Never a second copy** (CLAUDE.md decision 12). Marker words come from `ChannelGrammar`; marker
  MATCHING comes from `MemberState_Resolver.Contains_Marker`; the member's channel path comes from
  `MemberChannel_Locator`; the entry identity comes from `ChannelEntry_Digest`. If you find yourself
  writing a regex for a marker, or a second path builder, stop.
- **Never assert on a state with two routes to it** (decision 20). Every "nothing happened" assertion
  in this plan is asserted beside a live control in the same run — the shape `WakeTicketSweepTests`
  already uses.
- **`PauseGatesEveryWakerScanTests` is a register.** Anything that writes into a channel from the
  engine tick must be gated on `session.Paused` AND must appear in that test's `TheWakers` table.
  This plan adds one sweep; Task 8 adds its row. Miss it and the suite goes red, which is the point.
- **Branch:** `feat/one-wake-model`, worktree `../AIOrchestrator-relations`. Stage explicit paths,
  never `git add -A`. Multi-line messages via `git commit -F <tempfile>`. Commits in English,
  `type(scope): a descriptive clause`.
- **`dotnet` is NOT on PATH on the owner's Mac.** It lives at `~/.dotnet/dotnet` (SDK 10.0.401).
  Start every shell with `export PATH="$HOME/.dotnet:$PATH"`.
- **Whoever runs this has 120 s per command.** Never run the whole suite in a step that does not say
  to. Every step below names a `--filter`. The full run is ~3 min 20 s and must be started in the
  background or split; the last measured full run on this branch was **3 955 passed / 10 skipped /
  0 failed**.
- **Three tests flake under parallel load** and are not yours: `ClosingTurnReviewFixTests`,
  `TolerantFileReaderTests`, `EffortDialOnABridgeDrivenSupervisorTests`. Each is green 3/3 in
  isolation. Any OTHER red is yours.
- **The supervisor's skill is contested, and is being SPLIT while this plan is written.** Parallel
  work on this branch is moving sections of `kit/skills/supervisor/SKILL.md` into
  `kit/skills/supervisor/reference/*.md` — `### Briefing a reviewer` was already in
  `reference/reviews.md` on 2026-09-15. Task 10 touches the supervisor LAST, greps for the heading
  first, and writes into whichever file holds it. Never resolve a conflict by overwriting somebody
  else's section, and never leave the rule in two files.

---

## What this buys, counted honestly

The spec says a fix round costs "four supervisor wake-ups". Counted the way the app counts a wake —
one turn started — a round from the round-one findings to the final verdict is **three**:

| | today | with a contract |
|---|---|---|
| the reviewer's findings arrive | wake 1 — verdict + fix brief to `imp-1` | wake 1 — verdict + fix brief + `REROUTE:` |
| `imp-1` files the fix report | **wake 2 — reads it, writes a re-review brief to `rev-1`** | **no wake — the app relays, the report is held** |
| `rev-1` files the re-review | wake 3 — final verdict | wake 2 — final verdict, carrying the held fix report |

**One supervisor wake-up removed per fix round.** `[measured, VPS, 6–9 Sep]` a supervisor wake-up is
~1 M input tokens (2.5 calls × ~398 k mean context); `[derived]` a round with two fix iterations
therefore gives back ~2 M tokens of pure postman work. The review-economy figures the skills already
carry — `fincanva-3`'s five rounds at **$49** with findings 9 → 11 → 10 → 8 → 7, `fincanva-5`'s
**$18.77** fifth pass for one LOW and **$25.69** for a late deep pass that blocked nothing, against
**$1–4** for a re-review scoped to the delta — are the reviewer-side argument for scoping a
re-review, which is already skill policy; this plan is the supervisor-side half of the same round.

**And the saving is gated on `wake: ticket`.** Under `runners.<role>.wake = watcher` — still the
shipped default — the supervisor's bash monitor fingerprints its channels and wakes on the fix report
whatever the app decides, so on that machine this plan buys the reviewer a correct, scoped brief and
the supervisor a turn it does not have to compose, but NOT the wake-up. Say so in the report; do not
quote a saving the owner's machine is not yet collecting.

---

## The five questions the brief asked, answered

1. **Who decides the fix report "satisfies" the contract?** Not a textual comparison — the app never
   reads prose for meaning. The implementer DECLARES it, with one grammar marker: `FIXED: <commit>`.
   The app checks four mechanical facts: the entry is on that implementer's own channel, authored by
   a member, it appears after the declaration in file order, and it carries exactly one `FIXED:`
   marker whose argument is a commit-shaped token different from the contract's base. Matching uses
   `MemberState_Resolver.Contains_Marker`, the repo's one matcher, which already excludes a QUOTED
   marker from a DECLARED one. **The app never decides the fix is good** — only that the report the
   supervisor said to wait for has arrived and names a delta. (Tasks 1, 5.)
2. **If the implementer does not name the commit?** Nothing is routed, and that is a designed
   fail-open: the fix report stays ordinary traffic and wakes the supervisor exactly as it does
   today. The app writes one line to `orchestrator.log.jsonl` naming WHICH predicate failed
   (decision 21: "could not extract a commit from the FIXED: line" is actionable, "reroute failed"
   is the silence again). Two `FIXED:` lines is the same answer: an ambiguity is never guessed.
   (Task 5.)
3. **Can the supervisor turn it off for a round?** It is OPT-IN — no `REROUTE:` line, no routing, so
   "off" is the default and costs nothing. To retract one already declared, the supervisor writes
   `REROUTE: cancel` in that implementer's channel; a second `REROUTE:` declaration on the same
   channel supersedes the first. (Task 4.)
4. **Who sees the relay?** The reviewer, and nobody else. The entry is written with
   `AppEntryAudiences.Agent`, so it is never mirrored to Telegram — CLAUDE.md decision 15: a relay
   between two of the owner's sessions is not something the owner can act on. It is visible in the
   channel, in `orchestrator.log.jsonl`, and in the round's outcome, which is the thing the owner
   actually reads. (Tasks 6, 8.)
5. **What if the reviewer never answers?** Two ways out, both the app's. If the reviewer's session
   closes, the contract closes with it on the next tick. Otherwise the contract expires after
   `RerouteContract_Policy.REVIEW_CAP` (90 minutes): the hold is released, so the fix report wakes
   the supervisor on the ordinary digest, and ONE agent-audience attention entry says the re-review
   never came back. Expiry degrades exactly to today's behaviour — the supervisor is back in the
   loop with everything still in its channels. (Task 9.)

**Open for the owner, decided provisionally here rather than silently:**

- **90 minutes** for `REVIEW_CAP` is a judgement, not a measurement. A re-review is `quick` by
  construction ($1–4, minutes), so 90 is generous; the cost of it being wrong is one extra supervisor
  wake, in the direction of MORE supervision, never less. It ships as a constant, not a settings-
  catalogue row — making every new dial data is right in general (CLAUDE.md's model/effort ruling)
  and is parked here under decision 22, because nothing in the owner's request asked for it.
- **Whether the relay may carry the implementer's report body at all.** It does, verbatim and capped
  (Task 6). The alternative — commits and findings only — keeps the reviewer maximally blind but
  makes it re-derive from the diff what the implementer already wrote down. The reviewer's own skill
  says each earlier finding gets one verdict WITH ITS EVIDENCE, and the implementer's claim is what
  that verdict is against. If the owner wants a blinder re-review, the cap becomes zero and one test
  changes.

---

## File Structure

**Created:**

| file | responsibility |
|---|---|
| `AIOrchestratorCoreLib/Channels/RoutedReport_Tag.cs` | the `[routed]` subject tag: the ONE place it is written and read |
| `AIOrchestratorCoreLib/Reviewing/IRerouteContract.cs` | the contract: who, to whom, from which commit, in which state |
| `AIOrchestratorCoreLib/Reviewing/RerouteContractModel.cs` | immutable carrier |
| `AIOrchestratorCoreLib/Reviewing/RerouteContract_Factory.cs` | construction + `CreateFrom_Routed` |
| `AIOrchestratorCoreLib/Reviewing/RerouteContract_Store.cs` | `<orch folder>/reroute.json`: path, read, write, remove |
| `AIOrchestratorCoreLib/Reviewing/RerouteContract_Policy.cs` | `REVIEW_CAP`, and the words the log and the entries carry |
| `AIOrchestratorCoreLib/Reviewing/RerouteContract_Parser.cs` | reads a `REROUTE:` declaration out of a supervisor entry |
| `AIOrchestratorCoreLib/Reviewing/FixReport_Matcher.cs` | does this entry satisfy that contract — and if not, WHICH predicate failed |
| `AIOrchestratorCoreLib/Reviewing/RoutedReport_Composer.cs` | the relay entry's subject and body: copies, never composes |
| `AIOrchestratorCoreLib/Reviewing/RoutedHold_Policy.cs` | which pending identities ride the supervisor's next turn instead of starting one |

**Modified:**

| file | change |
|---|---|
| `kit/grammar/channel-grammar.json` | `+ "reroute": "REROUTE:"`, `+ "fixed": "FIXED:"` |
| `AIOrchestratorCoreLib/Channels/ChannelGrammar.cs` | `+ REROUTE`, `+ FIXED` properties |
| `AIOrchestratorCoreLib/Running/PrintTurn_Trigger.cs` | `+ Is_Inbound(role, entry)` overload; `Is_AgentNote` excludes a routed entry; `Select_Pending` uses the overload |
| `AIOrchestratorCoreLib/Running/TurnCursor/TurnCursor_Factory.cs` | its three `Is_Inbound(role, entry.Author)` sites use the entry overload |
| `AIOrchestratorCoreLib/Running/WakeDecision/WakeDecision_Resolver.cs` | `Decide_OrNull` takes `ridingOnlyIdentities`; `Resolve_OrNull` resolves it |
| `AIOrchestratorCoreLib/Running/PrintTurnDispatcher/PrintTurnDispatcherModel.cs` | passes `ridingOnlyIdentities` |
| `AIOrchestratorCoreLib/Bridge/BridgeEngine/BridgeEngineModel.cs` | `+ Sweep_RoutedReports_Async`, called from the tick; passes `ridingOnlyIdentities` in its own sweep |
| `AIOrchestratorCoreLib.Tests/Bridge/PauseGatesEveryWakerScanTests.cs` | `+` the new sweep's row |
| `AIOrchestratorCoreLib.Tests/Kit/RoleCommandMarkerTests.cs` | `+` the two new markers to `TAUGHT_MARKERS` |
| `kit/skills/reviewer/SKILL.md` | a routed re-review brief is a brief; what it carries |
| `kit/skills/implementer/SKILL.md` | a brief carrying `REROUTE:` is answered with `FIXED: <commit>` |
| the supervisor's reviewer-briefing section | how to declare the contract, and that you then do NOT brief the reviewer yourself. **Locate it first**: on 2026-09-15 `### Briefing a reviewer` moved out of `kit/skills/supervisor/SKILL.md` into `kit/skills/supervisor/reference/reviews.md` under parallel work on this branch |

---

## Task 1: Two marker words, in the one place a marker word exists

**Files:**
- Modify: `kit/grammar/channel-grammar.json`
- Modify: `AIOrchestratorCoreLib/Channels/ChannelGrammar.cs`
- Test: `AIOrchestratorCoreLib.Tests/Channels/RerouteGrammarTests.cs`

**Interfaces:**
- Produces: `ChannelGrammar.REROUTE` (`"REROUTE:"`), `ChannelGrammar.FIXED` (`"FIXED:"`). Tasks 4, 5
  and 10 read them; nothing anywhere else may spell either word.

**Why the grammar and not a C# constant:** the writer is a session using `kit/bin/channel-append.sh`
(bash, reading this file with `jq`) and the recogniser is .NET. `ChannelGrammarTests` fails if the
embedded copy and the kit copy differ by a byte — that test is the whole reason this is a data file.

**Why the shipped grammar is `REROUTE: <reviewer> from <commit>` and not the spec's sketch
`REROUTE: rev-1 on FIXED from <commit>`:** the sketch nests one marker word inside another marker's
argument. This repo draws a hard line between DECLARING a marker and DISCUSSING one
(`MemberState_Resolver.Contains_Marker` excludes quotation; `AppEntryAudience_Tag.Is_AgentTagged` is
a prefix test "so an entry that merely discusses the tag does not become invisible"), and a
supervisor line containing the word `FIXED` would be a declaration of the implementer's marker by
the wrong author. Two words, two authors, one each.

- [ ] **Step 1: Write the failing test**

```csharp
// AIOrchestratorCoreLib.Tests/Channels/RerouteGrammarTests.cs
using AIOrchestratorCoreLib.Channels;
using Xunit;

namespace AIOrchestratorCoreLib.Tests.Channels;

/// <summary>
/// THE TWO WORDS OF A RE-REVIEW CONTRACT, in the one place a marker word exists. `REROUTE:` is the
/// SUPERVISOR's — "when imp-1 reports a fix, the app relays it to rev-1 instead of waking me" — and
/// `FIXED:` is the IMPLEMENTER's answer, the commit that closes the round. One word per author, and
/// neither nested inside the other: a supervisor line carrying the word FIXED would be a declaration
/// of a marker by the wrong session, which is the distinction Contains_Marker exists to keep.
/// </summary>
public class RerouteGrammarTests
{
    [Fact]
    public void BothWordsAreInTheGrammarAndCarryTheirColon()
    {
        Assert.Equal("REROUTE:", ChannelGrammar.REROUTE);
        Assert.Equal("FIXED:", ChannelGrammar.FIXED);
    }

    /// <summary>
    /// AND THE GRAMMAR'S OWN LIST KNOWS THEM. All_Markers is what RoleCommandMarkerTests walks to
    /// decide whether a phrase a skill teaches is real vocabulary; a marker added as a property but
    /// not to the data file would be invisible to it, and to `channel-append.sh` as well.
    /// </summary>
    [Fact]
    public void BothWordsAreInTheGrammarsOwnMarkerList()
    {
        Assert.Contains(ChannelGrammar.REROUTE, ChannelGrammar.All_Markers);
        Assert.Contains(ChannelGrammar.FIXED, ChannelGrammar.All_Markers);
    }
}
```

- [ ] **Step 2: Run it and watch it fail**

```bash
export PATH="$HOME/.dotnet:$PATH"
dotnet test AIOrchestratorCoreLib.Tests/AIOrchestratorCoreLib.Tests.csproj --filter "FullyQualifiedName~RerouteGrammarTests"
```
Expected: compile error — `ChannelGrammar.REROUTE` does not exist.

- [ ] **Step 3: Add the words to the data file**

In `kit/grammar/channel-grammar.json`, inside `"markers"`, beside `"state"`:

```json
    "reroute": "REROUTE:",
    "fixed": "FIXED:",
```

- [ ] **Step 4: Add the two properties**

In `ChannelGrammar.cs`, beside `STATE`:

```csharp
    /// <summary>
    /// THE SUPERVISOR'S RE-REVIEW CONTRACT: `REROUTE: &lt;reviewer&gt; from &lt;commit&gt;`, written at the end
    /// of a fix brief. It means "when that implementer declares FIXED, hand the delta and everything
    /// I wrote below this line to that reviewer, and do not wake me for it". Read by
    /// <c>Reviewing.RerouteContract_Parser</c>.
    /// </summary>
    public static string REROUTE => Marker("reroute");

    /// <summary>
    /// THE IMPLEMENTER'S ANSWER TO ONE: `FIXED: &lt;commit&gt;`, the head of the delta a re-review reads.
    /// It is what makes "does this report satisfy the contract" a mechanical question rather than a
    /// reading of prose — see <c>Reviewing.FixReport_Matcher</c>.
    /// </summary>
    public static string FIXED => Marker("fixed");
```

- [ ] **Step 5: Run the new test AND the grammar's own**

```bash
dotnet test AIOrchestratorCoreLib.Tests/AIOrchestratorCoreLib.Tests.csproj \
  --filter "FullyQualifiedName~RerouteGrammarTests|FullyQualifiedName~ChannelGrammarTests"
```
Expected: PASS. `ChannelGrammarTests.TheEmbeddedGrammarIsTheKitsGrammar` is the one that proves you
edited the file the app embeds and not a stale copy — if it is red, the build did not pick up the
resource; rebuild before believing anything else.

- [ ] **Step 6: Commit**

```bash
git add kit/grammar/channel-grammar.json AIOrchestratorCoreLib/Channels/ChannelGrammar.cs AIOrchestratorCoreLib.Tests/Channels/RerouteGrammarTests.cs
git commit -F /tmp/cm.txt   # "feat(channels): REROUTE: and FIXED: — the two words a re-review contract is written in"
```

---

## Task 2: `Is_Inbound` learns ONE case, and nothing else moves

**Files:**
- Create: `AIOrchestratorCoreLib/Channels/RoutedReport_Tag.cs`
- Modify: `AIOrchestratorCoreLib/Running/PrintTurn_Trigger.cs`
- Modify: `AIOrchestratorCoreLib/Running/TurnCursor/TurnCursor_Factory.cs`
- Test: `AIOrchestratorCoreLib.Tests/Running/RoutedReportIsInboundForAReviewerTests.cs`
- Modify: `AIOrchestratorCoreLib.Tests/Running/WakeUpPolicyTests.cs` (strengthen, do not weaken)

**Interfaces:**
- Produces: `RoutedReport_Tag.ROUTED_TAG`, `.Apply(subject)`, `.Is_Routed(subject)`;
  `PrintTurn_Trigger.Is_Inbound(SessionRoles role, IChannelEntry entry)`.
- The author-only `Is_Inbound(SessionRoles, ChannelAuthors)` **keeps its exact current behaviour and
  signature.**

**THIS IS THE MOST DELICATE CHANGE IN THE PLAN. Read this before writing a line.**

Today `Is_Inbound` takes an AUTHOR, and `WakeUpPolicyTests.TheAppsOwnEntries_AreInboundForNobody`
asserts, for every role, that an app entry is inbound for nobody — with a docstring saying it exists
"so a later change to `Is_Inbound` cannot quietly make app traffic a reason to wake the most
expensive role in the system". That test is right and stays green, untouched, because **the
author-only overload does not change.** One app entry in a hundred thousand is now inbound, and it is
distinguishable only by its SUBJECT, so the new case lives on an ENTRY-level overload:

```
Is_Inbound(role, author)   ← unchanged. Still false for App, for every role.
Is_Inbound(role, entry)    ← the author test, OR: this is a routed report and I am a reviewer.
```

Four screens, all of which must hold at once, and none of which is widened by this task:

1. `entry.Author == ChannelAuthors.App` — only the app writes app entries.
2. `RoutedReport_Tag.Is_Routed(entry.Subject)` — a PREFIX test, not a `Contains`, for the reason
   `AppEntryAudience_Tag.Is_AgentTagged` gives in its own docstring: an entry that merely discusses
   the tag must not acquire its powers.
3. `role == SessionRoles.Reviewer` — a supervisor, an implementer, a solo, a communicator and the
   general supervisor are unaffected. The supervisor especially: it is the role this whole series is
   trying to wake LESS.
4. Nothing else about `ChannelAuthors.App` changes. `turn_ended`, `STATUS`, ledger advisories,
   orphan and respawn notes stay inbound for nobody.

**And the second change, which is not optional.** `Is_AgentNote` is true for an app entry that is
agent-tagged and is not a `turn_ended` record — and a routed report IS agent-tagged, because
`AppEntryAudiences.Agent` is what keeps it off the owner's phone (decision 15). Left alone, a routed
report would be BOTH an agent note (rides a turn, never starts one) and inbound (starts one), which
is two answers to one question in the same expression. It is excluded there, one clause beside the
`turn_ended` exclusion that is already in that line for exactly the same reason.

- [ ] **Step 1: Write the failing test**

```csharp
// AIOrchestratorCoreLib.Tests/Running/RoutedReportIsInboundForAReviewerTests.cs
using AIOrchestratorCoreLib.Channels;
using AIOrchestratorCoreLib.Channels.ChannelEntry;
using AIOrchestratorCoreLib.Running;
using Xunit;

namespace AIOrchestratorCoreLib.Tests.Running;

/// <summary>
/// THE ONE APP ENTRY THAT WAKES ANYBODY, AND THE FOUR SCREENS THAT KEEP IT ALONE. The app relays a
/// fix report into a reviewer's channel so a fix round does not cost the supervisor a wake-up to act
/// as a postman (2026-09-15 one-wake-model spec, step 4). Everything else the app writes still wakes
/// nobody — <see cref="WakeUpPolicyTests.TheAppsOwnEntries_AreInboundForNobody"/> is the guard on
/// the author-only overload and it is deliberately NOT relaxed: this is a second, narrower question,
/// asked of the whole entry.
/// </summary>
public class RoutedReportIsInboundForAReviewerTests
{
    static IChannelEntry Entry(ChannelAuthors author, string subject)
    {
        return ChannelEntry_Parser.Parse_All(
            $"## [1] FROM {ChannelAuthor_Words.Get_Word(author)} — 2026-09-15 10:00 — {subject}\nbody\n")[0];
    }

    static string Routed(string subject)
    {
        return AppEntryAudience_Tag.Apply(RoutedReport_Tag.Apply(subject), AppEntryAudiences.Agent);
    }

    [Fact]
    public void ARoutedReportIsInboundForAReviewer()
    {
        Assert.True(PrintTurn_Trigger.Is_Inbound(SessionRoles.Reviewer, Entry(ChannelAuthors.App, Routed("re-review — imp-1's fix"))));
    }

    /// <summary>
    /// AND FOR NOBODY ELSE. The supervisor is the role this series exists to wake less; an
    /// implementer would be answering a brief addressed to a reviewer.
    /// </summary>
    [Theory]
    [InlineData(SessionRoles.Supervisor)]
    [InlineData(SessionRoles.Implementer)]
    [InlineData(SessionRoles.Solo)]
    [InlineData(SessionRoles.General)]
    [InlineData(SessionRoles.Communicator)]
    public void ARoutedReportIsInboundForNoOtherRole(SessionRoles role)
    {
        Assert.False(PrintTurn_Trigger.Is_Inbound(role, Entry(ChannelAuthors.App, Routed("re-review — imp-1's fix"))));
    }

    /// <summary>
    /// THE APP'S ORDINARY BOOKKEEPING IS UNMOVED, asked through the NEW overload — the old one is
    /// pinned next door, and pinning only that would leave the widened door untested.
    /// </summary>
    [Theory]
    [InlineData("[agent] turn_ended imp-1 turn 4 — ok")]
    [InlineData("[agent] PLAN.md is behind your verdicts")]
    [InlineData("STATUS — 3 of 7 lines closed")]
    public void AnOrdinaryAppEntryIsStillInboundForNobody(string subject)
    {
        foreach (var role in SessionRole_Names.ALL)
            Assert.False(PrintTurn_Trigger.Is_Inbound(role, Entry(ChannelAuthors.App, subject)), $"'{subject}' woke {role}");
    }

    /// <summary>
    /// A MEMBER CANNOT TALK ITS WAY INTO ANOTHER MEMBER'S TURN. The tag means something only where
    /// the APP put it: the author screen is first, so a supervisor whose brief quotes the tag — which
    /// is exactly what a brief explaining this mechanism would do — relays nothing.
    /// </summary>
    [Fact]
    public void TheTagOnANonAppEntryMeansNothing()
    {
        Assert.False(PrintTurn_Trigger.Is_Inbound(SessionRoles.Reviewer, Entry(ChannelAuthors.Supervisor, Routed("re-review — imp-1's fix"))));
        Assert.False(PrintTurn_Trigger.Is_Inbound(SessionRoles.Reviewer, Entry(ChannelAuthors.Implementer, Routed("re-review — imp-1's fix"))));
    }

    /// <summary>
    /// AND ONLY AT THE FRONT. A prefix test, like the audience tag's, so an entry that DISCUSSES the
    /// tag does not acquire its powers.
    /// </summary>
    [Fact]
    public void TheTagIsReadAtTheFrontOfTheSubjectAndNowhereElse()
    {
        Assert.False(PrintTurn_Trigger.Is_Inbound(SessionRoles.Reviewer, Entry(ChannelAuthors.App, "[agent] a note about the [routed] tag")));
    }

    /// <summary>
    /// IT IS INBOUND, WHICH MEANS IT IS NOT A RIDING NOTE. Both were true of it before the exclusion
    /// in Is_AgentNote — two answers in one expression, which is how an entry gets delivered twice or
    /// not at all.
    /// </summary>
    [Fact]
    public void ARoutedReportIsNotAnAgentNote()
    {
        Assert.False(PrintTurn_Trigger.Is_AgentNote(Entry(ChannelAuthors.App, Routed("re-review — imp-1's fix"))));
        Assert.True(PrintTurn_Trigger.Is_AgentNote(Entry(ChannelAuthors.App, "[agent] PLAN.md is behind your verdicts")));
    }

    /// <summary>
    /// AND IT IS PENDING FOR A REVIEWER THAT HAS NOT SEEN IT — the property every screen above exists
    /// to serve. Without this the whole task is a predicate nobody calls.
    /// </summary>
    [Fact]
    public void ARoutedReportIsPendingForAReviewerThatHasNotSeenIt()
    {
        var entries = ChannelEntry_Parser.Parse_All(
            $"## [1] FROM app — 2026-09-15 10:00 — {Routed("re-review — imp-1's fix")}\nthe delta and the findings\n");

        var source = TurnSource_Factory.Create_Spoke("rev-1", "/tmp/channel.md");

        Assert.Single(PrintTurn_Trigger.Select_Pending(SessionRoles.Reviewer, entries, TurnCursor_Factory.Create_Empty(source)));
    }
}
```

> **Note for the implementer:** use whatever `ChannelEntry_Parser` entry point the existing tests use
> (`PrintTurnTriggerTests` builds its fixtures — read it first and follow it exactly rather than the
> shape sketched here), and the same `TurnSource_Factory` / `TurnCursor_Factory` calls
> `TurnCursorTests` uses. Adapt the fixture helpers; do not adapt the assertions.

- [ ] **Step 2: Run it and watch it fail**

```bash
dotnet test AIOrchestratorCoreLib.Tests/AIOrchestratorCoreLib.Tests.csproj --filter "FullyQualifiedName~RoutedReportIsInboundForAReviewerTests"
```
Expected: compile error — `RoutedReport_Tag` does not exist.

- [ ] **Step 3: Write the tag**

```csharp
// AIOrchestratorCoreLib/Channels/RoutedReport_Tag.cs
namespace AIOrchestratorCoreLib.Channels;

/// <summary>
/// THE MARK THAT MAKES ONE APP ENTRY A BRIEF. The app relays an implementer's fix report into a
/// reviewer's channel so that a fix round does not cost the supervisor a wake-up as a postman
/// (2026-09-15 one-wake-model spec, step 4). That entry is written by the app, like every other app
/// entry, and is the only one in the system that a session is expected to ANSWER — so it has to be
/// distinguishable from the app's bookkeeping by something that survives the round trip through the
/// channel file, which is the subject and nothing else (the reasoning is
/// <see cref="AppEntryAudience_Tag"/>'s, one word over).
///
/// <para>
/// IT DOES NOT REPLACE THE AUDIENCE TAG, IT SITS INSIDE IT. A routed report is agent-facing — the
/// owner cannot act on one session's brief to another (CLAUDE.md decision 15) — so the subject
/// carries both, audience outermost: <c>[agent] [routed] re-review — imp-1's fix</c>. That is why
/// <see cref="Is_Routed"/> steps over a leading audience tag before it looks.
/// </para>
/// <para>
/// A PREFIX TEST, NEVER A CONTAINS. The tag means something only where the app put it. A supervisor
/// brief explaining this mechanism, or a reviewer quoting its own brief back, must not become a
/// relay by mentioning the word — the same distinction the marker vocabulary draws between a
/// declaration and a discussion of one.
/// </para>
/// <para>
/// AND THE AUTHOR SCREEN IS STILL FIRST. <c>PrintTurn_Trigger.Is_Inbound</c> asks for
/// <see cref="ChannelAuthors.App"/> before it asks this, so the tag confers nothing on an entry a
/// session wrote. Members never write into each other's channels, and nothing here is a way to.
/// </para>
/// </summary>
public static class RoutedReport_Tag
{
    public const string ROUTED_TAG = "[routed]";

    /// <summary>The subject as the relay writes it. Pass the result to <c>ChannelAppender.Append_AppEntry</c>, which applies the audience tag around it.</summary>
    public static string Apply(string subject)
    {
        return $"{ROUTED_TAG} {subject}";
    }

    /// <summary>
    /// Whether a subject read back from a file is a routed report. The audience tag is stepped over
    /// first because the writer applies it outermost; nothing else is tolerated in front.
    /// </summary>
    public static bool Is_Routed(string subject)
    {
        var trimmed = subject.TrimStart();

        if (AppEntryAudience_Tag.Is_AgentTagged(trimmed))
            trimmed = trimmed[trimmed.IndexOf(AppEntryAudience_Tag.AGENT_TAG, StringComparison.OrdinalIgnoreCase)..][AppEntryAudience_Tag.AGENT_TAG.Length..].TrimStart();

        return trimmed.StartsWith(ROUTED_TAG, StringComparison.OrdinalIgnoreCase);
    }
}
```

- [ ] **Step 4: Add the overload and the exclusion**

In `PrintTurn_Trigger.cs`:

```csharp
    /// <summary>
    /// WHETHER THIS WHOLE ENTRY IS INBOUND — the author's answer, plus the one exception that cannot
    /// be read from an author: a ROUTED REPORT (<see cref="Channels.RoutedReport_Tag"/>), which the
    /// app writes into a reviewer's channel when an implementer files the fix a re-review contract
    /// was waiting for.
    ///
    /// <para>
    /// A SECOND OVERLOAD RATHER THAN A WIDER FIRST ONE, deliberately. The author-only test is the one
    /// <see cref="PendingTraffic.WakeUp_Policy"/>'s docstring quotes, and
    /// <c>WakeUpPolicyTests.TheAppsOwnEntries_AreInboundForNobody</c> pins it for every role so that
    /// "a later change cannot quietly make app traffic a reason to wake the most expensive role in
    /// the system". That guard is still exactly true. What changed is that ONE app entry in a
    /// reviewer's channel is now a brief, and it is recognisable only by its subject.
    /// </para>
    /// <para>
    /// AND THE HUB-AND-SPOKE TOPOLOGY IS UNTOUCHED (CLAUDE.md decision 4). The relay is written by
    /// the APP into the reviewer's OWN channel. No member reads another member's file, and the author
    /// screen below means a member cannot forge one by writing the tag itself.
    /// </para>
    /// </summary>
    public static bool Is_Inbound(SessionRoles role, IChannelEntry entry)
    {
        return Is_Inbound(role, entry.Author) || Is_RoutedReport(role, entry);
    }

    /// <summary>
    /// The four screens, all required: written by the app, tagged by the relay at the FRONT of the
    /// subject, and read by a REVIEWER. Nothing else about <see cref="ChannelAuthors.App"/> moves.
    /// </summary>
    static bool Is_RoutedReport(SessionRoles role, IChannelEntry entry)
    {
        return role == SessionRoles.Reviewer
            && entry.Author == ChannelAuthors.App
            && RoutedReport_Tag.Is_Routed(entry.Subject);
    }
```

`Select_Pending` changes one line — `if (!Is_Inbound(role, entry))`.

`Is_AgentNote` gains one clause, beside the `turn_ended` one and for the same reason:

```csharp
    public static bool Is_AgentNote(IChannelEntry entry)
    {
        return entry.Author == ChannelAuthors.App
            && AppEntryAudience_Tag.Is_AgentTagged(entry.Subject)
            && !entry.Subject.Contains(PrintTurn_Words.TURN_ENDED_SUBJECT, StringComparison.Ordinal)
            // A ROUTED REPORT IS A BRIEF, NOT A NOTE. It is agent-tagged because the owner cannot act
            // on it (decision 15), and without this clause it would be a riding note AND inbound at
            // once — two answers to "does this start a turn" in one expression.
            && !RoutedReport_Tag.Is_Routed(entry.Subject);
    }
```

- [ ] **Step 5: Move the three cursor call sites onto the overload**

In `TurnCursor_Factory.cs` all three uses of `PrintTurn_Trigger.Is_Inbound(role, entry.Author)`
become `PrintTurn_Trigger.Is_Inbound(role, entry)`:

- `Create_Baseline` — a routed entry already in a channel at first sight is HISTORY like any other
  inbound entry. Left on the author test, a reviewer registered after one was written would be handed
  it again as new.
- `CreateFrom_Delivered`'s `stillLive` loop — a delivered routed entry must survive the prune, or it
  rides again next turn and the turn after (the comment above that loop says exactly this about app
  notes).
- `CreateFrom_Delivered`'s high-water line — a routed report IS traffic, so it moves the mark the
  archive-gap warning reads.

Confirm with `grep -n "Is_Inbound(role, entry.Author)" AIOrchestratorCoreLib/Running/TurnCursor/TurnCursor_Factory.cs`
→ expected: no matches.

- [ ] **Step 6: Strengthen the existing guard rather than leaving it half-true**

`WakeUpPolicyTests.TheAppsOwnEntries_AreInboundForNobody` stays exactly as it is. Add ONE case
beside it, in the same file, so the file's own claim stays honest now that a second door exists:

```csharp
    /// <summary>
    /// AND THE DOOR OPENED IN 2026-09-15's STEP 4 IS ONE ENTRY WIDE. The entry-level overload admits
    /// a routed report for a reviewer and nothing else; this asserts the negative half from THIS
    /// file, because the guard above now describes only one of the two overloads.
    /// </summary>
    [Fact]
    public void TheAppsOwnEntries_AreInboundForNobody_ThroughTheEntryOverloadToo()
    {
        var bookkeeping = Entry("app", "[agent] turn_ended imp-1 turn 4 — ok");

        foreach (var role in SessionRole_Names.ALL)
            Assert.False(PrintTurn_Trigger.Is_Inbound(role, bookkeeping), $"an app entry is inbound for {role}");
    }
```

- [ ] **Step 7: Run the new tests, then every oracle that reads this predicate**

```bash
dotnet test AIOrchestratorCoreLib.Tests/AIOrchestratorCoreLib.Tests.csproj \
  --filter "FullyQualifiedName~RoutedReportIsInboundForAReviewerTests|FullyQualifiedName~PrintTurnTriggerTests|FullyQualifiedName~TurnCursorTests|FullyQualifiedName~WakeUpPolicyTests"
```
Expected: PASS, **with no edit to `PrintTurnTriggerTests` or `TurnCursorTests`.** If either needed
one, the overload changed behaviour for an existing case and the change is wrong.

Then the digest and notes oracles, which are what a mistake here would silently move:

```bash
dotnet test AIOrchestratorCoreLib.Tests/AIOrchestratorCoreLib.Tests.csproj \
  --filter "FullyQualifiedName~AgentNotesRideTheNextTurn|FullyQualifiedName~MemberTrafficRidesOneDigestedTurn|FullyQualifiedName~WakeDecisionResolver"
```

- [ ] **Step 8: Commit**

```bash
git add AIOrchestratorCoreLib/Channels/RoutedReport_Tag.cs AIOrchestratorCoreLib/Running/PrintTurn_Trigger.cs AIOrchestratorCoreLib/Running/TurnCursor/TurnCursor_Factory.cs AIOrchestratorCoreLib.Tests/Running/
git commit -F /tmp/cm.txt   # "feat(running): one app entry is a brief — a routed report is inbound for the reviewer it names"
```

---

## Task 3: The contract, and where it lives

**Files:**
- Create: `AIOrchestratorCoreLib/Reviewing/{IRerouteContract,RerouteContractModel,RerouteContract_Factory,RerouteContract_Store,RerouteContract_Policy}.cs`
- Test: `AIOrchestratorCoreLib.Tests/Reviewing/RerouteContractStoreTests.cs`

**Interfaces:**
- Produces:

```csharp
public enum RerouteStates { Declared, Routed }

public interface IRerouteContract
{
    string Id { get; }                  // the identity of the supervisor entry that declared it
    string OrchId { get; }
    string ImplementerId { get; }       // whose report satisfies it
    string ReviewerId { get; }          // who receives the relay
    string BaseCommit { get; }          // the last reviewed commit — the LEFT side of the delta
    string Brief { get; }               // the supervisor's own words, verbatim, from the REROUTE line down
    DateTime DeclaredUtc { get; }
    RerouteStates State { get; }
    string? ReportIdentity { get; }     // ChannelEntry_Digest of the fix report, once routed
    string? HeadCommit { get; }         // the RIGHT side of the delta, once routed
    DateTime? RoutedUtc { get; }
}

public static class RerouteContract_Store
{
    public static string Get_File(ISupervisionPaths paths, string orchId);
    public static IReadOnlyList<IRerouteContract> Read_Open(ISupervisionPaths paths, string orchId);
    public static void Write_Open(ISupervisionPaths paths, string orchId, IReadOnlyList<IRerouteContract> contracts);
}
```

Tasks 5, 7, 8 and 9 read and write through these.

**Why a file and not the channels.** "Have I already routed this?" derived from the channels is
CLAUDE.md decision 13's exact trap: `Channel_Compactor` archives from the front, so any answer read
off a live file is not stable over time. The contract is small, app-written, app-read, and lives
beside `session.json` at `<orch folder>/reroute.json`. **Absent is the common case and reads as
none** — no orchestration that never declares one pays anything for this feature.

**Why closing removes rather than marks.** The file is a working set, not an audit trail; the audit
trail is `orchestrator.log.jsonl`, which every close writes one line to (decision 21's rule about
naming what happened rather than going silent). A contract that stays in the file for ever is a hold
that stays armed for ever.

**At most ONE open contract per implementer.** A second declaration on the same implementer's channel
supersedes the first, with a log line. This is what keeps "which contract does this report satisfy"
from ever being a search.

- [ ] **Step 1: Write the failing test**

```csharp
// AIOrchestratorCoreLib.Tests/Reviewing/RerouteContractStoreTests.cs
using AIOrchestratorCoreLib.Reviewing;
using AIOrchestratorCoreLib.SupervisionPaths;
using AIOrchestratorCoreLib.Tests.TestSupport;
using Xunit;

namespace AIOrchestratorCoreLib.Tests.Reviewing;

/// <summary>
/// THE CONTRACT OUTLIVES THE PROCESS, because the round does: the supervisor declares it in one turn
/// and the implementer files its fix minutes or hours later, across app restarts. And ABSENT IS THE
/// COMMON CASE — an orchestration that never declares one must pay nothing for this feature, which
/// is why every read of a missing file is an empty list and never an error.
/// </summary>
public class RerouteContractStoreTests : IDisposable
{
    readonly string _root = Path.Combine(Path.GetTempPath(), $"aiorch-reroute-{Guid.NewGuid():N}");
    readonly ISupervisionPaths _paths;

    public RerouteContractStoreTests()
    {
        Directory.CreateDirectory(_root);
        _paths = SupervisionPaths_Factory.Create(_root);
    }

    public void Dispose()
    {
        TempTree.Delete_BestEffort(_root);
        GC.SuppressFinalize(this);
    }

    static IRerouteContract Declared(string id = "c1")
    {
        return RerouteContract_Factory.Create_Declared(
            id: id, orchId: "repo-1", implementerId: "imp-1", reviewerId: "rev-1",
            baseCommit: "abc1234", brief: "F1 and F3 only. The delta is the fix, not the branch.",
            declaredUtc: new DateTime(2026, 9, 15, 10, 0, 0, DateTimeKind.Utc));
    }

    [Fact]
    public void AMissingFileReadsAsNoContracts()
    {
        Assert.Empty(RerouteContract_Store.Read_Open(_paths, "repo-1"));
    }

    [Fact]
    public void ACorruptFileReadsAsNoContractsRatherThanThrowing()
    {
        Directory.CreateDirectory(_paths.Get_OrchestrationFolder("repo-1"));
        File.WriteAllText(RerouteContract_Store.Get_File(_paths, "repo-1"), "{not json");

        Assert.Empty(RerouteContract_Store.Read_Open(_paths, "repo-1"));
    }

    [Fact]
    public void ADeclaredContractRoundTripsWithTheSupervisorsWordsIntact()
    {
        RerouteContract_Store.Write_Open(_paths, "repo-1", [Declared()]);

        var read = Assert.Single(RerouteContract_Store.Read_Open(_paths, "repo-1"));

        Assert.Equal("imp-1", read.ImplementerId);
        Assert.Equal("rev-1", read.ReviewerId);
        Assert.Equal("abc1234", read.BaseCommit);
        Assert.Equal("F1 and F3 only. The delta is the fix, not the branch.", read.Brief);
        Assert.Equal(RerouteStates.Declared, read.State);
        Assert.Null(read.ReportIdentity);
    }

    [Fact]
    public void ARoutedContractCarriesTheReportItWasSatisfiedBy()
    {
        var routed = RerouteContract_Factory.CreateFrom_Routed(
            Declared(), reportIdentity: "9f2a1c", headCommit: "def5678",
            routedUtc: new DateTime(2026, 9, 15, 10, 30, 0, DateTimeKind.Utc));

        RerouteContract_Store.Write_Open(_paths, "repo-1", [routed]);
        var read = Assert.Single(RerouteContract_Store.Read_Open(_paths, "repo-1"));

        Assert.Equal(RerouteStates.Routed, read.State);
        Assert.Equal("9f2a1c", read.ReportIdentity);
        Assert.Equal("def5678", read.HeadCommit);
        Assert.Equal("abc1234", read.BaseCommit);
    }

    /// <summary>
    /// CLOSING IS REMOVING. A contract left in the file is a hold left armed, and the hold is what
    /// keeps a supervisor from being woken — the one failure mode of this feature that is silent.
    /// </summary>
    [Fact]
    public void WritingAShorterListRemovesTheRest()
    {
        RerouteContract_Store.Write_Open(_paths, "repo-1", [Declared("c1"), Declared("c2")]);
        RerouteContract_Store.Write_Open(_paths, "repo-1", [Declared("c2")]);

        Assert.Equal("c2", Assert.Single(RerouteContract_Store.Read_Open(_paths, "repo-1")).Id);
    }
}
```

- [ ] **Step 2: Run it and watch it fail**

```bash
dotnet test AIOrchestratorCoreLib.Tests/AIOrchestratorCoreLib.Tests.csproj --filter "FullyQualifiedName~RerouteContractStoreTests"
```
Expected: compile error — the namespace does not exist.

- [ ] **Step 3: Write the triple, the store and the policy**

Follow `Running/WakeTicket/` exactly: `System.Text.Json`, write to `<file>.tmp` then
`File.Move(tmp, file, overwrite: true)`, `Read_*` returning empty on a missing file, a
`JsonException` or an `IOException`. `RerouteContract_Factory` exposes `Create_Declared(...)` and
`CreateFrom_Routed(IRerouteContract declared, string reportIdentity, string headCommit, DateTime routedUtc)`
— which copies every field and is the ONLY way a contract reaches `Routed`, so the base commit and
the supervisor's words cannot be lost on the way.

`RerouteContract_Policy` holds the numbers and the words, so no other file spells them:

```csharp
namespace AIOrchestratorCoreLib.Reviewing;

/// <summary>
/// THE DIALS AND THE WORDS OF A RE-REVIEW CONTRACT, in one place so no sweep spells them.
/// </summary>
public static class RerouteContract_Policy
{
    /// <summary>
    /// HOW LONG A ROUTED CONTRACT HOLDS THE SUPERVISOR OUT OF THE LOOP. Past this the hold is
    /// released, the fix report wakes the supervisor on the ordinary digest, and one entry says the
    /// re-review never came back.
    ///
    /// <para>
    /// NINETY MINUTES IS A JUDGEMENT AND NOT A MEASUREMENT. A re-review is `quick` by construction —
    /// the delta is small, and the reviewer's own skill costs it at $1–4 — so this is generous by a
    /// wide margin. What matters is the DIRECTION of being wrong: expiring early costs one supervisor
    /// wake-up and restores exactly today's behaviour, while never expiring would leave a round
    /// silently unowned, which is the one failure this feature can produce that nobody would see.
    /// </para>
    /// <para>
    /// A CONSTANT AND NOT A SETTINGS-CATALOGUE ROW. Both of the owner's own dials are data
    /// (CLAUDE.md's model/effort ruling) and this one arguably should be too; nothing in the request
    /// asked for it, so it is PARKED rather than built (decision 22).
    /// </para>
    /// </summary>
    public static readonly TimeSpan REVIEW_CAP = TimeSpan.FromMinutes(90);

    public const string CANCEL_WORD = "cancel";
}
```

- [ ] **Step 4: Run the test and watch it pass**

```bash
dotnet test AIOrchestratorCoreLib.Tests/AIOrchestratorCoreLib.Tests.csproj --filter "FullyQualifiedName~RerouteContractStoreTests"
```
Expected: PASS, 5 tests.

- [ ] **Step 5: Commit**

```bash
git add AIOrchestratorCoreLib/Reviewing/ AIOrchestratorCoreLib.Tests/Reviewing/
git commit -F /tmp/cm.txt   # "feat(reviewing): the re-review contract, and the file it survives a restart in"
```

---

## Task 4: Reading a contract out of the supervisor's own words

**Files:**
- Create: `AIOrchestratorCoreLib/Reviewing/RerouteContract_Parser.cs`
- Test: `AIOrchestratorCoreLib.Tests/Reviewing/RerouteContractParserTests.cs`

**Interfaces:**
- Produces:

```csharp
public enum RerouteDeclarations { None, Contract, Cancel }

public readonly record struct RerouteDeclaration(RerouteDeclarations Kind, string ReviewerId, string BaseCommit, string Brief);

public static class RerouteContract_Parser
{
    public static RerouteDeclaration Read(IChannelEntry entry);
}
```

**The shape, and why it is this shape:**

```
REROUTE: rev-1 from abc1234
Check F1 and F3 only. F2 was accepted as stated.
The delta is the fix — do not re-read the branch.
```

- The declaration must be written by the SUPERVISOR (the caller screens the author; the parser
  refuses nothing about authors, it is a text reader).
- **Everything below the `REROUTE:` line, to the end of the body, is the brief — verbatim.** It is not
  parsed, not summarised, not reordered. The supervisor decides exactly what the reviewer reads;
  the app carries it. That single rule removes every prose-parsing question from this plan and is
  what makes "the app never composes a brief" literally true.
- The marker is matched with `MemberState_Resolver.Contains_Marker(entry, ChannelGrammar.REROUTE)` —
  the repo's one matcher, which already handles decoration, whole-token matching and the
  quotation exclusion. Only the ARGUMENT is parsed here, and with a strict regex.
- `REROUTE: cancel` retracts. Anything else after the marker that does not parse is `None`, plus a
  log line at the caller naming the failure — never a contract built from a guess.

- [ ] **Step 1: Write the failing test**

```csharp
// AIOrchestratorCoreLib.Tests/Reviewing/RerouteContractParserTests.cs
using AIOrchestratorCoreLib.Channels;
using AIOrchestratorCoreLib.Reviewing;
using Xunit;

namespace AIOrchestratorCoreLib.Tests.Reviewing;

/// <summary>
/// THE APP READS TWO FIELDS AND COPIES THE REST. A contract's argument — which reviewer, from which
/// commit — is machine-readable on purpose; everything under it is the SUPERVISOR's brief and is
/// carried verbatim, because a re-review's scope is the supervisor's decision and the app has no
/// opinion it is entitled to.
/// </summary>
public class RerouteContractParserTests
{
    static IChannelEntry Entry(string subject, string body)
    {
        return ChannelEntry_Parser.Parse_All($"## [7] FROM supervisor — 2026-09-15 10:00 — {subject}\n{body}\n")[0];
    }

    [Fact]
    public void ADeclarationNamesTheReviewerAndTheBaseCommit()
    {
        var read = RerouteContract_Parser.Read(Entry(
            "VERDICT — F1 and F3 must be fixed",
            "Fix F1 and F3 on the branch.\n\nREROUTE: rev-1 from abc1234\nCheck F1 and F3 only. F2 was accepted."));

        Assert.Equal(RerouteDeclarations.Contract, read.Kind);
        Assert.Equal("rev-1", read.ReviewerId);
        Assert.Equal("abc1234", read.BaseCommit);
    }

    /// <summary>
    /// THE BRIEF IS EVERYTHING BELOW THE LINE, VERBATIM — the rule that keeps the app out of the
    /// business of deciding what a re-review covers.
    /// </summary>
    [Fact]
    public void TheBriefIsEverythingUnderTheDeclaration()
    {
        var read = RerouteContract_Parser.Read(Entry(
            "VERDICT",
            "Fix F1.\n\nREROUTE: rev-1 from abc1234\nCheck F1 only.\nF2 stands as a stated limitation."));

        Assert.Equal("Check F1 only.\nF2 stands as a stated limitation.", read.Brief);
    }

    [Fact]
    public void AnEntryWithNoDeclarationIsNone()
    {
        Assert.Equal(RerouteDeclarations.None, RerouteContract_Parser.Read(Entry("VERDICT", "Fix F1 and F3.")).Kind);
    }

    [Fact]
    public void TheSupervisorCanRetractOne()
    {
        Assert.Equal(RerouteDeclarations.Cancel, RerouteContract_Parser.Read(Entry("VERDICT", "REROUTE: cancel")).Kind);
    }

    /// <summary>
    /// A MALFORMED ARGUMENT IS NOT A CONTRACT. Half a declaration read as a whole one would route a
    /// round to a reviewer that does not exist, or diff from a commit nobody named; the honest answer
    /// is None, and the caller says which predicate failed.
    /// </summary>
    [Theory]
    [InlineData("REROUTE: rev-1")]
    [InlineData("REROUTE: from abc1234")]
    [InlineData("REROUTE: rev-1 from HEAD")]
    [InlineData("REROUTE: rev-1 from zzzz")]
    [InlineData("REROUTE:")]
    public void AMalformedDeclarationIsNoDeclaration(string line)
    {
        Assert.Equal(RerouteDeclarations.None, RerouteContract_Parser.Read(Entry("VERDICT", line)).Kind);
    }

    /// <summary>
    /// AND A QUOTED ONE DECLARES NOTHING. A supervisor explaining the mechanism in a brief — or an
    /// implementer's report quoting the brief back — writes these words constantly. The matcher this
    /// parser delegates to has excluded quotation since 2026-09-09, and this is that rule's third
    /// consumer rather than a fourth spelling of it (CLAUDE.md decision 12).
    /// </summary>
    [Fact]
    public void AQuotedDeclarationIsNotADeclaration()
    {
        Assert.Equal(RerouteDeclarations.None, RerouteContract_Parser.Read(Entry("VERDICT", "> REROUTE: rev-1 from abc1234")).Kind);
    }
}
```

> **Note for the implementer:** verify the exact quotation rule in
> `MemberState_Resolver.Contains_Marker` before writing the last case — match the fixture to what
> that method actually excludes, and if it excludes a different quotation shape, use that shape.
> Do NOT write a second matcher to make this test pass.

- [ ] **Step 2: Run it and watch it fail**

```bash
dotnet test AIOrchestratorCoreLib.Tests/AIOrchestratorCoreLib.Tests.csproj --filter "FullyQualifiedName~RerouteContractParserTests"
```
Expected: compile error.

- [ ] **Step 3: Write the parser**

Detect with `MemberState_Resolver.Contains_Marker(entry, ChannelGrammar.REROUTE)`; then scan the body
line by line for the first line whose trimmed form starts with `ChannelGrammar.REROUTE` (the shape
`QuestionDirectives_Parser` uses for `DEADLINE:` and `RISK:` — read it and follow it), parse the
remainder with

```csharp
    [GeneratedRegex(@"^(?<reviewer>[A-Za-z][A-Za-z0-9_-]{0,63})\s+from\s+(?<base>[0-9a-fA-F]{7,40})$", RegexOptions.ExplicitCapture)]
    private static partial Regex Argument();
```

and take the rest of the body, trimmed of leading and trailing blank lines and **not otherwise
touched**, as the brief. The commit is lower-cased on the way in — a sha is a sha whichever case it
was typed in, and the contract compares it later.

- [ ] **Step 4: Run the test and watch it pass**

```bash
dotnet test AIOrchestratorCoreLib.Tests/AIOrchestratorCoreLib.Tests.csproj --filter "FullyQualifiedName~RerouteContractParserTests"
```
Expected: PASS, 10 cases.

- [ ] **Step 5: Commit**

```bash
git add AIOrchestratorCoreLib/Reviewing/RerouteContract_Parser.cs AIOrchestratorCoreLib.Tests/Reviewing/RerouteContractParserTests.cs
git commit -F /tmp/cm.txt   # "feat(reviewing): read a re-review contract out of the supervisor's verdict, and copy its brief verbatim"
```

---

## Task 5: Does this report satisfy the contract — and if not, which predicate failed

**Files:**
- Create: `AIOrchestratorCoreLib/Reviewing/FixReport_Matcher.cs`
- Test: `AIOrchestratorCoreLib.Tests/Reviewing/FixReportMatcherTests.cs`

**Interfaces:**
- Produces:

```csharp
public readonly record struct FixReportMatch(bool Matched, string HeadCommit, string Refusal);

public static class FixReport_Matcher
{
    /// <param name="entriesInFileOrder">the implementer channel's entries, as read — file order, never [n]</param>
    public static FixReportMatch Find(IRerouteContract contract, IReadOnlyList<IChannelEntry> entriesInFileOrder, string declarationIdentity);
}
```

**The predicates, all required, and every refusal named:**

| predicate | why | refusal words |
|---|---|---|
| the entry is after the declaration in FILE ORDER | a report filed before the contract existed cannot satisfy it — and file order is trustworthy where `[n]` is not (decision 12) | `"the only FIXED: entry predates the contract"` |
| `ChannelAuthor_Kinds.Is_Member(entry.Author)` | the supervisor's own quotation of `FIXED:` in a brief is not the implementer reporting | `"no member entry declares FIXED:"` |
| `MemberState_Resolver.Contains_Marker(entry, ChannelGrammar.FIXED)` | one matcher, quotation already excluded | as above |
| exactly ONE such entry is undelivered-and-newest, with exactly ONE `FIXED:` line in it | an ambiguity is never guessed | `"two FIXED: lines in one report — the head commit is ambiguous"` |
| the argument is `[0-9a-f]{7,40}` | the delta needs two commits | `"the FIXED: line names no commit"` |
| it differs from `contract.BaseCommit` | a delta of nothing is not a re-review | `"the FIXED: commit is the base commit"` |

**Every refusal is a designed fail-open**, not an error: nothing is routed, the fix report stays
ordinary traffic, and the supervisor wakes on it exactly as it does today. The refusal string is what
Task 8 writes to `orchestrator.log.jsonl` — decision 21: a predicate that cannot be evaluated says
WHICH one, and "reroute failed" is the silence again.

- [ ] **Step 1: Write the failing test**

```csharp
// AIOrchestratorCoreLib.Tests/Reviewing/FixReportMatcherTests.cs
using AIOrchestratorCoreLib.Channels;
using AIOrchestratorCoreLib.Reviewing;
using Xunit;

namespace AIOrchestratorCoreLib.Tests.Reviewing;

/// <summary>
/// THE APP NEVER DECIDES THAT A FIX IS GOOD. It decides that the report the supervisor said to wait
/// for has arrived and names a delta — six mechanical facts, no reading of prose. Everything the
/// supervisor actually judges stays the supervisor's, at the re-verdict.
/// </summary>
public class FixReportMatcherTests
{
    const string DECLARATION = "## [7] FROM supervisor — 2026-09-15 10:00 — VERDICT\nFix F1.\n\nREROUTE: rev-1 from abc1234\nCheck F1 only.\n";

    static IRerouteContract Contract()
    {
        return RerouteContract_Factory.Create_Declared(
            id: "c1", orchId: "repo-1", implementerId: "imp-1", reviewerId: "rev-1",
            baseCommit: "abc1234", brief: "Check F1 only.", declaredUtc: DateTime.UtcNow);
    }

    static (IReadOnlyList<IChannelEntry> Entries, string DeclarationIdentity) Channel(params string[] after)
    {
        var entries = ChannelEntry_Parser.Parse_All(DECLARATION + string.Concat(after));

        return (entries, ChannelEntry_Digest.Compute(entries[0]));
    }

    [Fact]
    public void AReportDeclaringAFixedCommitSatisfiesTheContract()
    {
        var channel = Channel("## [8] FROM implementer — 2026-09-15 10:20 — F1 fixed\nFIXED: def5678\n214 tests green.\n");

        var match = FixReport_Matcher.Find(Contract(), channel.Entries, channel.DeclarationIdentity);

        Assert.True(match.Matched);
        Assert.Equal("def5678", match.HeadCommit);
    }

    [Fact]
    public void AReportWithNoFixedLineSatisfiesNothingAndSaysWhy()
    {
        var channel = Channel("## [8] FROM implementer — 2026-09-15 10:20 — F1 fixed\nCommitted def5678. 214 tests green.\n");

        var match = FixReport_Matcher.Find(Contract(), channel.Entries, channel.DeclarationIdentity);

        Assert.False(match.Matched);
        Assert.Contains("FIXED:", match.Refusal, StringComparison.Ordinal);
    }

    /// <summary>
    /// A REPORT FILED BEFORE THE CONTRACT EXISTED IS NOT AN ANSWER TO IT. Asked of FILE ORDER, never
    /// of the `[n]` in the header, which is agent-written and has duplicated in production
    /// (CLAUDE.md decision 12).
    /// </summary>
    [Fact]
    public void AnEarlierFixedEntryDoesNotSatisfyALaterContract()
    {
        var entries = ChannelEntry_Parser.Parse_All(
            "## [6] FROM implementer — 2026-09-15 09:00 — earlier work\nFIXED: 1111111\n" + DECLARATION);

        var match = FixReport_Matcher.Find(Contract(), entries, ChannelEntry_Digest.Compute(entries[1]));

        Assert.False(match.Matched);
    }

    [Fact]
    public void TheSupervisorsOwnQuotationOfTheMarkerIsNotAReport()
    {
        var channel = Channel("## [8] FROM supervisor — 2026-09-15 10:20 — reminder\nWhen you are done, end your report with FIXED: <commit>.\n");

        Assert.False(FixReport_Matcher.Find(Contract(), channel.Entries, channel.DeclarationIdentity).Matched);
    }

    [Fact]
    public void TwoFixedLinesInOneReportAreAmbiguousAndRouteNothing()
    {
        var channel = Channel("## [8] FROM implementer — 2026-09-15 10:20 — F1 fixed\nFIXED: def5678\nFIXED: 9999999\n");

        var match = FixReport_Matcher.Find(Contract(), channel.Entries, channel.DeclarationIdentity);

        Assert.False(match.Matched);
        Assert.Contains("ambiguous", match.Refusal, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void AFixedCommitEqualToTheBaseIsNoDelta()
    {
        var channel = Channel("## [8] FROM implementer — 2026-09-15 10:20 — nothing to do\nFIXED: abc1234\n");

        var match = FixReport_Matcher.Find(Contract(), channel.Entries, channel.DeclarationIdentity);

        Assert.False(match.Matched);
        Assert.Contains("base commit", match.Refusal, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// AND THE NEWEST ONE WINS. An implementer that reports twice — a partial fix, then the rest —
    /// has moved the head; re-reviewing the first would review a delta that no longer exists.
    /// </summary>
    [Fact]
    public void TheNewestFixedEntryIsTheOneRouted()
    {
        var channel = Channel(
            "## [8] FROM implementer — 2026-09-15 10:20 — half of F1\nFIXED: def5678\n",
            "## [9] FROM implementer — 2026-09-15 10:40 — the rest of F1\nFIXED: 0abcdef\n");

        Assert.Equal("0abcdef", FixReport_Matcher.Find(Contract(), channel.Entries, channel.DeclarationIdentity).HeadCommit);
    }
}
```

- [ ] **Step 2: Run it and watch it fail**

```bash
dotnet test AIOrchestratorCoreLib.Tests/AIOrchestratorCoreLib.Tests.csproj --filter "FullyQualifiedName~FixReportMatcherTests"
```
Expected: compile error.

- [ ] **Step 3: Write the matcher**

Walk `entriesInFileOrder`, remember the position of `declarationIdentity`, and consider only entries
after it. Return the LAST qualifying one. Every early return carries its refusal words; none of them
throws.

- [ ] **Step 4: Run the test and watch it pass**

```bash
dotnet test AIOrchestratorCoreLib.Tests/AIOrchestratorCoreLib.Tests.csproj --filter "FullyQualifiedName~FixReportMatcherTests"
```
Expected: PASS, 7 tests.

- [ ] **Step 5: Commit**

```bash
git add AIOrchestratorCoreLib/Reviewing/FixReport_Matcher.cs AIOrchestratorCoreLib.Tests/Reviewing/FixReportMatcherTests.cs
git commit -F /tmp/cm.txt   # "feat(reviewing): six mechanical facts decide that a fix report has arrived — never a reading of prose"
```

---

## Task 6: The relay entry — the app copies, it never composes

**Files:**
- Create: `AIOrchestratorCoreLib/Reviewing/RoutedReport_Composer.cs`
- Test: `AIOrchestratorCoreLib.Tests/Reviewing/RoutedReportComposerTests.cs`

**Interfaces:**
- Produces: `RoutedReport_Composer.Compose(IRerouteContract contract, IChannelEntry report)` →
  `(string Subject, string Body)`. The subject is passed through `RoutedReport_Tag.Apply`; the caller
  passes it to `ChannelAppender.Append_AppEntry` with `AppEntryAudiences.Agent`, which applies the
  audience tag around it.

**What the entry carries, and nothing else** — the reviewer's own skill fixes this list: *"Review the
DELTA and the earlier findings, nothing else. The delta is `git diff <last reviewed commit>..<new
commit>`; the brief names both — ask if it does not."*

1. the two commits, as a literal `git diff` command the reviewer can run;
2. the supervisor's brief, verbatim, from the contract;
3. the implementer's report, verbatim, capped;
4. one line saying where the entry came from and that the reviewer reports in its own channel as
   usual.

**Capped, and loudly.** The report body is cut at `MAXIMUM_REPORT_BYTES` (8 192) with an explicit
`— TRUNCATED, N bytes omitted —` marker. A silent truncation is a hole, and this repo's own history
of holes says they are worse than a refusal. A reviewer that needs the rest asks its supervisor,
which costs a wake-up and is correct: that is a question only the supervisor can answer.

**Nothing is summarised, re-ordered, or re-worded.** If a future change makes the app write a
sentence of its own about the work under review, that change has made the app a participant in the
review, and it is out of bounds.

- [ ] **Step 1: Write the failing test**

```csharp
// AIOrchestratorCoreLib.Tests/Reviewing/RoutedReportComposerTests.cs
using AIOrchestratorCoreLib.Channels;
using AIOrchestratorCoreLib.Reviewing;
using Xunit;

namespace AIOrchestratorCoreLib.Tests.Reviewing;

/// <summary>
/// THE RELAY CARRIES TWO THINGS AND A POINTER, because the reviewer's skill says a re-review reviews
/// exactly two things: the DELTA and the earlier findings. An entry carrying the branch, or the
/// original brief, or a fresh instruction of the app's own invention would re-open the whole-branch
/// re-read that cost `fincanva-3` $49 across five rounds whose finding count never fell.
/// </summary>
public class RoutedReportComposerTests
{
    static IRerouteContract Routed()
    {
        return RerouteContract_Factory.CreateFrom_Routed(
            RerouteContract_Factory.Create_Declared(
                id: "c1", orchId: "repo-1", implementerId: "imp-1", reviewerId: "rev-1",
                baseCommit: "abc1234", brief: "Check F1 and F3 only. F2 was accepted as stated.",
                declaredUtc: DateTime.UtcNow),
            reportIdentity: "9f2a1c", headCommit: "def5678", routedUtc: DateTime.UtcNow);
    }

    static IChannelEntry Report(string body)
    {
        return ChannelEntry_Parser.Parse_All($"## [8] FROM implementer — 2026-09-15 10:20 — F1 and F3 fixed\n{body}\n")[0];
    }

    [Fact]
    public void TheSubjectIsTaggedSoTheReviewerWakesOnIt()
    {
        var composed = RoutedReport_Composer.Compose(Routed(), Report("FIXED: def5678"));

        Assert.True(RoutedReport_Tag.Is_Routed(composed.Subject));
        Assert.Contains("imp-1", composed.Subject, StringComparison.Ordinal);
    }

    [Fact]
    public void TheBodyNamesTheDeltaAsACommandAndCarriesBothCommits()
    {
        var composed = RoutedReport_Composer.Compose(Routed(), Report("FIXED: def5678"));

        Assert.Contains("git diff abc1234..def5678", composed.Body, StringComparison.Ordinal);
    }

    [Fact]
    public void TheSupervisorsBriefIsCarriedVerbatim()
    {
        Assert.Contains("Check F1 and F3 only. F2 was accepted as stated.", RoutedReport_Composer.Compose(Routed(), Report("FIXED: def5678")).Body, StringComparison.Ordinal);
    }

    [Fact]
    public void TheImplementersReportIsCarriedVerbatim()
    {
        var composed = RoutedReport_Composer.Compose(Routed(), Report("FIXED: def5678\nF1: the guard now runs before the write.\nF3: covered by a new case."));

        Assert.Contains("F3: covered by a new case.", composed.Body, StringComparison.Ordinal);
        Assert.Contains("F1 and F3 fixed", composed.Body, StringComparison.Ordinal);
    }

    /// <summary>
    /// A LONG REPORT IS CUT, AND THE CUT IS ANNOUNCED. A silent truncation is a hole, and a reviewer
    /// reasoning from half a claim it believes is whole is the worst shape a wrong answer can take.
    /// </summary>
    [Fact]
    public void AnOverlongReportIsTruncatedWithTheOmissionStated()
    {
        var composed = RoutedReport_Composer.Compose(Routed(), Report(new string('x', RoutedReport_Composer.MAXIMUM_REPORT_BYTES + 500)));

        Assert.Contains("TRUNCATED", composed.Body, StringComparison.Ordinal);
        Assert.True(composed.Body.Length < RoutedReport_Composer.MAXIMUM_REPORT_BYTES + 2000);
    }

    /// <summary>
    /// AND THE BRANCH IS NOT IN IT. The one instruction this entry gives is the reviewer's own rule;
    /// nothing here re-opens the scope its skill spent five rounds of `fincanva-3` learning to close.
    /// </summary>
    [Fact]
    public void TheEntryNeverInvitesAFreshPassOverTheBranch()
    {
        var body = RoutedReport_Composer.Compose(Routed(), Report("FIXED: def5678")).Body;

        Assert.DoesNotContain("the branch", body, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("delta", body, StringComparison.OrdinalIgnoreCase);
    }
}
```

- [ ] **Step 2: Run it and watch it fail**

```bash
dotnet test AIOrchestratorCoreLib.Tests/AIOrchestratorCoreLib.Tests.csproj --filter "FullyQualifiedName~RoutedReportComposerTests"
```
Expected: compile error.

- [ ] **Step 3: Write the composer**

```csharp
    public const int MAXIMUM_REPORT_BYTES = 8192;

    public static (string Subject, string Body) Compose(IRerouteContract contract, IChannelEntry report)
    {
        var body =
            $"RE-REVIEW, routed by the app on the contract your supervisor declared. You have not been briefed by a member: members never write in each other's channels, and this entry is the app's.\n\n"
            + $"DELTA — review this and the findings below, nothing else:\n    git diff {contract.BaseCommit}..{contract.HeadCommit}\n\n"
            + $"YOUR SUPERVISOR'S BRIEF, verbatim:\n{contract.Brief}\n\n"
            + $"WHAT {contract.ImplementerId} REPORTED, verbatim:\n{report.Subject}\n{Cap(report.Body)}\n\n"
            + "Report in THIS channel as usual. Your findings are input to your supervisor's verdict, not a verdict.";

        return (RoutedReport_Tag.Apply($"re-review — {contract.ImplementerId}'s fix for {contract.ReviewerId}"), body);
    }
```

`Cap` returns the body unchanged under the limit, and otherwise the first `MAXIMUM_REPORT_BYTES`
characters followed by `\n— TRUNCATED, {n} characters omitted. Ask your supervisor if you need the rest. —`.

- [ ] **Step 4: Run the test and watch it pass, then commit**

```bash
dotnet test AIOrchestratorCoreLib.Tests/AIOrchestratorCoreLib.Tests.csproj --filter "FullyQualifiedName~RoutedReportComposerTests"
git add AIOrchestratorCoreLib/Reviewing/RoutedReport_Composer.cs AIOrchestratorCoreLib.Tests/Reviewing/RoutedReportComposerTests.cs
git commit -F /tmp/cm.txt   # "feat(reviewing): the relay entry — the delta, the supervisor's own words, and the implementer's own words"
```

---

## Task 7: The hold — a routed fix report RIDES the supervisor's next turn, it never starts one

**Files:**
- Create: `AIOrchestratorCoreLib/Reviewing/RoutedHold_Policy.cs`
- Modify: `AIOrchestratorCoreLib/Running/WakeDecision/WakeDecision_Resolver.cs`
- Modify: `AIOrchestratorCoreLib/Running/PrintTurnDispatcher/PrintTurnDispatcherModel.cs`
- Modify: `AIOrchestratorCoreLib/Bridge/BridgeEngine/BridgeEngineModel.cs` (`Sweep_WakeTickets_Async`'s `Decide_OrNull` call)
- Test: `AIOrchestratorCoreLib.Tests/Reviewing/RoutedReportRidesTheNextTurnTests.cs`

**Interfaces:**
- Produces: `RoutedHold_Policy.Resolve_RidingOnly(ISupervisionPaths paths, IPrintSessionState state)`
  → `IReadOnlyCollection<string>` of entry identities.
- Changes: `WakeDecision_Resolver.Decide_OrNull` gains `IReadOnlyCollection<string> ridingOnlyIdentities`
  **with no default value** — three call sites must pass it, and a default is how a caller silently
  skips a rule (plan 01 learned this on `CreateFrom_Existing_*`).

**THE WHOLE SAVING IS THIS TASK.** Everything before it routes a brief to the reviewer; without this
the supervisor still wakes on the fix report five minutes later and the round still costs three
turns.

**And it is the app-note rule again, applied to one member entry.** The system already has a category
of pending entry that is handed to a turn but never starts one — `Select_AgentNotes`, "the app's
notes ride the turn that is starting and never start one". A routed fix report is exactly that: the
supervisor WILL read it, on the turn the re-review verdict starts, together with the reviewer's
findings, which is the turn where it is worth reading. One rule, second application, no new concept.

**Three properties that make this safe, and each is a test below:**

- **Nothing is lost.** The entry stays PENDING — its cursor is not advanced, nothing is consumed.
  Whatever starts the next turn hands it over with everything else, because a turn takes every
  pending entry there is (`WakeUp_Policy`'s own "nothing is lost by being held").
- **The owner is never held.** The hold names ONE identity — the exact fix report — so an owner
  message, an escalation, or any other entry wakes the supervisor now and brings the held report
  with it.
- **Members are never held.** `Resolve_RidingOnly` returns empty for every role but Supervisor. The
  reviewer's brief and the implementer's next instruction are the WORK; the reason
  `QuestionHold_Policy` refuses to hold member channels applies here word for word.

**Where the code goes, and why it is not a second copy.** `Decide_OrNull` is the one home of the wake
decision after plan 01, and it already receives one caller-computed set of exactly this shape
(`firstContactSources`). The riding set is a second one. The dispatcher's own
`WakeUp_Policy.Contains_DigestableTraffic` call, which arms the digest clock, is **left alone on
purpose**: an armed clock on traffic that is being held costs nothing and is in the right direction —
when the reviewer's report finally lands, the window has long since elapsed, so the supervisor wakes
at once rather than waiting one more window for the entry it was already owed.

- [ ] **Step 1: Write the failing test**

```csharp
// AIOrchestratorCoreLib.Tests/Reviewing/RoutedReportRidesTheNextTurnTests.cs
using AIOrchestratorCoreLib.Channels;
using AIOrchestratorCoreLib.Reviewing;
using AIOrchestratorCoreLib.Running.PendingTraffic;
using AIOrchestratorCoreLib.Running.WakeDecision;
using Xunit;

namespace AIOrchestratorCoreLib.Tests.Reviewing;

/// <summary>
/// THE APP-NOTE RULE, APPLIED TO ONE MEMBER ENTRY. A fix report the app has already relayed to a
/// reviewer is not news the supervisor must act on now — it will read it beside the re-review
/// findings, on the turn where a verdict is possible. Until then it RIDES: pending, never consumed,
/// never a reason to spend ~1 M input tokens (measured, VPS 6–9 Sep) on a turn that can only say
/// "noted".
/// </summary>
public class RoutedReportRidesTheNextTurnTests
{
    static readonly DateTime T0 = new(2026, 9, 15, 10, 0, 0);

    [Fact]
    public void AHeldFixReportAloneStartsNoTurn()
    {
        var fixture = WakeFixture.ForSupervisor("repo-1").With_SeenMemberEntry("imp-1", "F1 fixed", body: "FIXED: def5678");

        Assert.Null(fixture.Decide(ridingOnly: [fixture.IdentityOfLastEntry], digestHeldSince: T0, nowLocal: T0.AddMinutes(30)));
    }

    /// <summary>
    /// AND WITHOUT THE HOLD IT WOULD HAVE — the live control for the case above, in the same fixture.
    /// A silence asserted alone has two routes to it (decision 20).
    /// </summary>
    [Fact]
    public void TheSameReportUnheldWakesTheSupervisorOnceTheDigestElapses()
    {
        var fixture = WakeFixture.ForSupervisor("repo-1").With_SeenMemberEntry("imp-1", "F1 fixed", body: "FIXED: def5678");

        Assert.NotNull(fixture.Decide(ridingOnly: [], digestHeldSince: T0, nowLocal: T0.AddMinutes(30)));
    }

    /// <summary>
    /// NOTHING IS LOST BY BEING HELD. The re-review lands, the turn starts for THAT, and the held
    /// report is handed over on the same turn — which is the whole point: one turn, both halves.
    /// </summary>
    [Fact]
    public void TheHeldReportRidesTheTurnTheReReviewStarts()
    {
        var fixture = WakeFixture.ForSupervisor("repo-1").With_SeenMemberEntry("imp-1", "F1 fixed", body: "FIXED: def5678");
        var held = fixture.IdentityOfLastEntry;

        fixture.With_SeenMemberEntry("rev-1", $"{MemberState_Resolver.QUESTION_MARKER} none — F1 verified FIXED", body: "one finding, LOW");

        var decision = fixture.Decide(ridingOnly: [held], digestHeldSince: T0, nowLocal: T0.AddMinutes(31));

        Assert.NotNull(decision);
        Assert.Contains(decision!.Pending, entry => ChannelEntry_Digest.Compute(entry.Entry) == held);
    }

    /// <summary>
    /// THE OWNER IS NEVER HELD BY THIS OR ANYTHING ELSE. One identity is named, and everything else
    /// on every channel keeps its own timing.
    /// </summary>
    [Fact]
    public void AnOwnerMessageWakesItImmediatelyAndBringsTheHeldReportAlong()
    {
        var fixture = WakeFixture.ForSupervisor("repo-1").With_SeenMemberEntry("imp-1", "F1 fixed", body: "FIXED: def5678");
        var held = fixture.IdentityOfLastEntry;

        fixture.With_OwnerEntry("where are we?");

        var decision = fixture.Decide(ridingOnly: [held], digestHeldSince: T0, nowLocal: T0.AddMinutes(1));

        Assert.NotNull(decision);
        Assert.Contains("the owner wrote", decision!.Reason, StringComparison.Ordinal);
        Assert.Contains(decision.Pending, entry => ChannelEntry_Digest.Compute(entry.Entry) == held);
    }

    /// <summary>
    /// A MEMBER IS NEVER HELD, whatever the contracts say — holding a member would stop the WORK,
    /// which is the objection QuestionHold_Policy's own comment raises against gating on a pending
    /// question, and it is just as fair here.
    /// </summary>
    [Fact]
    public void RidingOnlyIsEmptyForEveryRoleButTheSupervisor()
    {
        var paths = WakeFixture.PathsWithAnOpenRoutedContract("repo-1", reportIdentity: "9f2a1c");

        Assert.Empty(RoutedHold_Policy.Resolve_RidingOnly(paths, WakeFixture.StateFor(SessionRoles.Reviewer, "repo-1", "rev-1")));
        Assert.Empty(RoutedHold_Policy.Resolve_RidingOnly(paths, WakeFixture.StateFor(SessionRoles.Implementer, "repo-1", "imp-1")));
        Assert.Contains("9f2a1c", RoutedHold_Policy.Resolve_RidingOnly(paths, WakeFixture.StateFor(SessionRoles.Supervisor, "repo-1", "supervisor")));
    }

    /// <summary>
    /// AND A CONTRACT THAT IS ONLY DECLARED HOLDS NOTHING. Until the app has actually relayed the
    /// report, the supervisor is the only one who knows the round is open — holding then would hide
    /// a report nobody else has been told about.
    /// </summary>
    [Fact]
    public void ADeclaredButUnroutedContractHoldsNothing()
    {
        var paths = WakeFixture.PathsWithADeclaredContract("repo-1");

        Assert.Empty(RoutedHold_Policy.Resolve_RidingOnly(paths, WakeFixture.StateFor(SessionRoles.Supervisor, "repo-1", "supervisor")));
    }
}
```

> **Note for the implementer:** `WakeFixture` already exists — plan 01 Task 6 built it for
> `WakeDecisionResolverTests`. Read it and EXTEND it (a `Decide(ridingOnly, …)` entry point, a
> `body:` parameter on `With_SeenMemberEntry`, an `IdentityOfLastEntry`, and the two paths helpers).
> Do not write a second fixture.

- [ ] **Step 2: Run it and watch it fail**

```bash
dotnet test AIOrchestratorCoreLib.Tests/AIOrchestratorCoreLib.Tests.csproj --filter "FullyQualifiedName~RoutedReportRidesTheNextTurnTests"
```
Expected: compile error.

- [ ] **Step 3: Write the policy**

```csharp
// AIOrchestratorCoreLib/Reviewing/RoutedHold_Policy.cs

/// <summary>
/// THE PENDING ENTRIES THAT RIDE THE SUPERVISOR'S NEXT TURN INSTEAD OF STARTING ONE — exactly the
/// fix reports the app has already relayed to a reviewer (<see cref="RoutedReport_Composer"/>).
///
/// <para>
/// WHY THIS IS NOT A NEW IDEA. <c>PrintTurn_Trigger.Select_AgentNotes</c> already carries entries a
/// turn should READ but that must not BUY one; this is that rule applied to one member entry, for the
/// same reason — the supervisor will read the fix report on the turn where a verdict is possible,
/// which is the turn the re-review findings start. A turn spent in between can only say "noted", and
/// a supervisor wake-up is ~1 M input tokens (measured, VPS 6–9 Sep 2026).
/// </para>
/// <para>
/// NOTHING IS CONSUMED HERE. The identity is withheld from the WAKE-UP RULES and from nothing else:
/// the entry stays pending, its cursor does not move, and whatever starts the next turn hands it over
/// with everything else — "a turn takes every pending entry there is" is what makes holding safe, and
/// it is <see cref="PendingTraffic.WakeUp_Policy"/>'s own argument for the digest.
/// </para>
/// <para>
/// THE SUPERVISOR ONLY. A member's brief is the WORK, and holding work is not a saving — the same
/// objection <c>Bridge.QuestionHold_Policy</c> raises against holding member channels. And only a
/// ROUTED contract holds: one that is merely declared means the reviewer has not been told anything,
/// so the supervisor is still the only session that knows the round is open.
/// </para>
/// </summary>
public static class RoutedHold_Policy
{
    public static IReadOnlyCollection<string> Resolve_RidingOnly(ISupervisionPaths paths, IPrintSessionState state)
    {
        if (state.Role != SessionRoles.Supervisor)
            return [];

        HashSet<string> riding = [];

        foreach (var contract in RerouteContract_Store.Read_Open(paths, state.OrchId))
        {
            if (contract.State == RerouteStates.Routed && contract.ReportIdentity != null)
                riding.Add(contract.ReportIdentity);
        }

        return riding;
    }
}
```

- [ ] **Step 4: Split the pending set inside `Decide_OrNull`**

In `WakeDecision_Resolver.Decide_OrNull`, immediately after the existing empty-set guard:

```csharp
        // THE ENTRIES THAT RIDE RATHER THAN WAKE (RoutedHold_Policy). The wake-up rules are asked
        // about everything EXCEPT them; the turn, if one starts, is still handed the whole set.
        var wakers = ridingOnlyIdentities.Count == 0
            ? ordered
            : [.. ordered.Where(item => !ridingOnlyIdentities.Contains(ChannelEntry_Digest.Compute(item.Entry)))];

        // AND AN EMPTY WAKER SET IS "NOT YET", NEVER A BOOT TURN. WakeUp_Policy answers an empty
        // pending set with "boot turn" unconditionally — the trap the guard above already documents —
        // so a set whose every entry is riding must return here rather than be handed to it.
        if (wakers.Count == 0 && !Needs_BootTurn(state))
            return null;
```

`Resolve_WakeReason_OrNull` is then called with `wakers`, and `With_AgentNotes(state, sources, ordered, nowLocal)`
— the FULL set — is what the decision carries. That asymmetry is the feature; put a comment on it.

- [ ] **Step 5: Pass it at all three call sites**

```bash
grep -rn "Decide_OrNull(" --include="*.cs" AIOrchestratorCoreLib
```
Expected: three production call sites — `WakeDecision_Resolver.Resolve_OrNull`,
`PrintTurnDispatcherModel.Consider_Session`, `BridgeEngineModel.Sweep_WakeTickets_Async`. Each gets
`RoutedHold_Policy.Resolve_RidingOnly(_paths, state)` (the resolver uses its own `paths` parameter).
**No default value on the parameter**: a caller that forgets must not compile.

- [ ] **Step 6: Run the new tests, then every wake oracle**

```bash
dotnet test AIOrchestratorCoreLib.Tests/AIOrchestratorCoreLib.Tests.csproj --filter "FullyQualifiedName~RoutedReportRidesTheNextTurnTests"
dotnet test AIOrchestratorCoreLib.Tests/AIOrchestratorCoreLib.Tests.csproj \
  --filter "FullyQualifiedName~WakeDecisionResolver|FullyQualifiedName~WakeUpDigest|FullyQualifiedName~AgentNotesRideTheNextTurn|FullyQualifiedName~MemberTrafficRidesOneDigestedTurn|FullyQualifiedName~BootTurn|FullyQualifiedName~OwnerTrafficSkipsTheCoalesceWindow"
```
Expected: PASS, **with no edit to any of those existing files.** An orchestration with no contracts
gets an empty riding set, so every one of them must be byte-for-byte unaffected. If one needed an
edit, the split changed behaviour for traffic that has nothing to do with this feature.

- [ ] **Step 7: Commit**

```bash
git add AIOrchestratorCoreLib/Reviewing/RoutedHold_Policy.cs AIOrchestratorCoreLib/Running/WakeDecision/WakeDecision_Resolver.cs AIOrchestratorCoreLib/Running/PrintTurnDispatcher/PrintTurnDispatcherModel.cs AIOrchestratorCoreLib/Bridge/BridgeEngine/BridgeEngineModel.cs AIOrchestratorCoreLib.Tests/Reviewing/
git commit -F /tmp/cm.txt   # "feat(reviewing): a relayed fix report rides the supervisor's next turn instead of buying one"
```

---

## Task 8: The sweep — the app does the postman's round

**Files:**
- Modify: `AIOrchestratorCoreLib/Bridge/BridgeEngine/BridgeEngineModel.cs`
- Modify: `AIOrchestratorCoreLib.Tests/Bridge/PauseGatesEveryWakerScanTests.cs`
- Test: `AIOrchestratorCoreLib.Tests/Bridge/RoutedReportSweepTests.cs`

**Interfaces:**
- Consumes: everything built in Tasks 3–7, plus `MemberChannel_Locator.Get_ChannelFile`,
  `ChannelHistory_Cache.Read_Entries`, `ChannelAppender.Append_AppEntry`,
  `MemberKind_Ids.Resolve_Kind`.
- Produces: nothing new. This is where the pieces meet.

**The round, per tick, per open orchestration that HAS a contracts file** (absent ⇒ skipped before
anything is read — the common case costs one `File.Exists`):

1. **Paused? Skip.** `session.Paused` — read the sweep next door (`Sweep_WakeTickets_Async`) and copy
   its screen and its comment's reasoning. This sweep WRITES A CHANNEL ENTRY, so it is squarely in
   `PauseGatesEveryWakerScanTests`'s register.
2. **Declare:** read each implementer channel named by a pending declaration — that is, read the
   supervisor entries of every member channel — and for each `REROUTE:` declaration
   (`RerouteContract_Parser`) whose id is not already stored, open a contract. Validate:
   `MemberKind_Ids.Resolve_Kind(reviewerId) == MemberKinds.Reviewer`, the reviewer is an OPEN member
   of the roster, and it is not the implementer itself. A failure writes one log line naming the
   predicate and opens nothing. A second declaration on the same implementer supersedes the first.
3. **Route:** for each `Declared` contract, `FixReport_Matcher.Find`. On a match, compose
   (Task 6) and `ChannelAppender.Append_AppEntry(MemberChannel_Locator.Get_ChannelFile(_paths, orchId, reviewerId), AppEntryAudiences.Agent, subject, body, DateTime.Now)`.
   **Only if the append returns true** does the contract move to `Routed` — an entry that is not on
   disk must never arm a hold, or the supervisor is held out of a round nobody was told about. On a
   refusal, one log line with the refusal words, and the contract stays `Declared`.
4. **Close:** a contract whose reviewer has filed ANY entry of its own after the relay is done — the
   re-review has arrived, and it is inbound for the supervisor by the ordinary rules. Remove it, log
   it. (Task 9 adds the other two ways out.)

**Idempotence is by identity, never by count.** "Have I already routed this?" is
`contract.ReportIdentity == ChannelEntry_Digest.Compute(candidate)`, and "has the reviewer answered?"
is "an entry of the reviewer's own exists after the relay identity in file order" — never a count of
entries, which compaction breaks (CLAUDE.md decision 13).

- [ ] **Step 1: Write the failing test**

```csharp
// AIOrchestratorCoreLib.Tests/Bridge/RoutedReportSweepTests.cs
```

Build it on `WakeTicketSweepTests`'s construction, which is the closest sibling: a temp supervision
root, a real `BridgeEngine_Factory.Create_WithTelegramClient(..., telegramClient: null, BridgeTestTiming.Fast())`,
a `RecordingSpawner_Fake`, and `Run_Until_Async` polling for the effect. Cases:

- `ASupervisorContractPlusAFixReportPutsABriefInTheReviewersChannel` — the relay entry exists, is
  authored `app`, is `[agent]`-tagged and `[routed]`-tagged, and contains `git diff abc1234..def5678`.
- `TheReviewersBriefCarriesTheSupervisorsWordsAndTheImplementersWords` — both verbatim.
- `AFixReportWithNoFixedLineRoutesNothing_WhileTheOneBesideItDoes` — the live control, a second
  orchestration in the same ticks, so the silence pins something (decision 20).
- `ANonReviewerNamedInAContractRoutesNothing` — `REROUTE: imp-2 from abc1234`.
- `APausedOrchestrationRoutesNothing_WhileTheOneBesideItDoes` — the same control shape.
- `TheSameFixReportIsRoutedOnceAcrossManyTicks` — count the relay entries in the reviewer's channel
  after eight ticks: exactly one.
- `TheRelayIsNeverWrittenToTheOwnerChannelAndNeverTexted` — the owner channel is unchanged and the
  entry is `[agent]`-tagged (decision 15).

- [ ] **Step 2: Run it and watch it fail**

```bash
dotnet test AIOrchestratorCoreLib.Tests/AIOrchestratorCoreLib.Tests.csproj --filter "FullyQualifiedName~RoutedReportSweepTests"
```
Expected: FAIL — no relay is ever written.

- [ ] **Step 3: Write the sweep**

```csharp
    /// <summary>
    /// THE APP DOES THE POSTMAN'S ROUND SO THE SUPERVISOR DOES NOT HAVE TO. A fix round costs a
    /// supervisor wake-up to read a fix report and write a re-review brief that its own verdict
    /// already specified — ~1 M input tokens (measured, VPS 6–9 Sep 2026) to move words between two
    /// channels. When the round-one verdict carried a contract (<c>REROUTE:</c>), this writes that
    /// brief instead, and <see cref="Reviewing.RoutedHold_Policy"/> keeps the report from waking the
    /// supervisor until the re-review is back.
    ///
    /// <para>
    /// MEMBERS STILL NEVER WRITE TO EACH OTHER (CLAUDE.md decision 4). This is the APP appending to
    /// the reviewer's OWN channel, signed `app`, exactly like every other app entry; the hub gained a
    /// clerk, not a new edge. And review independence is untouched: the reviewer has no write tools,
    /// no stake, and its findings remain input to the supervisor's verdict.
    /// </para>
    /// <para>
    /// THE CONTRACT MOVES TO Routed ONLY IF THE APPEND SUCCEEDED. An entry that is not on disk that
    /// armed a hold would hold the supervisor out of a round nobody had been told about — silently,
    /// which is the one failure shape this feature can produce.
    /// </para>
    /// </summary>
    async Task Sweep_RoutedReports_Async(CancellationToken cancellationToken)
```

Call it from the tick immediately AFTER `Sweep_WakeTickets_Async` — it must see the contracts a
tick's routing opens before the next tick's wake decision reads them, and it must be above the DND
gate for the reason that sweep's comment gives: 🌙 pauses outbound Telegram, and a session's work is
not that.

- [ ] **Step 4: Add the row to the register**

In `PauseGatesEveryWakerScanTests.TheWakers`:

```csharp
        // THE ROUTED-REPORT SWEEP (one-wake-model step 4). It writes a BRIEF into a reviewer's
        // channel — a waker in the fullest sense, since that entry is what starts the reviewer's
        // turn. A paused orchestration that still hands out re-reviews is not dormant in any sense
        // the owner would recognise.
        { "async Task Sweep_RoutedReports_Async", "RoutedReport_Composer.Compose" },
```

- [ ] **Step 5: Run the new tests and the register**

```bash
dotnet test AIOrchestratorCoreLib.Tests/AIOrchestratorCoreLib.Tests.csproj \
  --filter "FullyQualifiedName~RoutedReportSweepTests|FullyQualifiedName~PauseGatesEveryWakerScanTests|FullyQualifiedName~APausedOrchestrationIsDormantTests"
```
Expected: PASS.

- [ ] **Step 6: Commit**

```bash
git add AIOrchestratorCoreLib/Bridge/BridgeEngine/BridgeEngineModel.cs AIOrchestratorCoreLib.Tests/Bridge/
git commit -F /tmp/cm.txt   # "feat(bridge): the app relays a fix report to the reviewer its supervisor named"
```

---

## Task 9: The ways out — a reviewer that never answers, and a reviewer that is gone

**Files:**
- Modify: `AIOrchestratorCoreLib/Bridge/BridgeEngine/BridgeEngineModel.cs` (`Sweep_RoutedReports_Async`)
- Test: `AIOrchestratorCoreLib.Tests/Bridge/RoutedReportCapTests.cs`

**The rule:** a `Routed` contract closes when any of these is true, and the FIRST two are the
ordinary path:

1. the reviewer has filed an entry of its own since the relay (Task 8) — the re-review is back;
2. the reviewer's member is closed, or the orchestration is closed — nobody is coming;
3. `DateTime.UtcNow - contract.RoutedUtc > RerouteContract_Policy.REVIEW_CAP` — the cap.

On (2) and (3) the contract is removed, which releases the hold: the fix report becomes ordinary
member traffic and wakes the supervisor on the ordinary digest, exactly as it would have before this
feature existed. On (3) ONE entry also goes to the supervisor's channel through
`Append_SupervisorAttention_UnlessMeeting` (agent audience — the owner cannot act on this, decision
15), saying which reviewer has not reported, for how long, and on which commits.

**One-shot, and spent only when the entry was written** — the discipline `BudgetAlert_Planner` already
holds, and its lesson: a token spent on an append that failed is an alert nobody ever gets. Removing
the contract IS the one-shot here, and it happens only after the append returns true or is refused by
a meeting/pause (in which case the contract is left for the next tick, because the report it holds is
still held and the supervisor still has not been told).

- [ ] **Step 1: Write the failing test**

```csharp
// AIOrchestratorCoreLib.Tests/Bridge/RoutedReportCapTests.cs
```

Three cases, all driven through the real tick as in Task 8:

- `AReReviewClosesTheContractAndReleasesTheHold` — after the reviewer files, the contracts file is
  empty and the supervisor's next wake carries both entries.
- `ACapExpiryReleasesTheHoldAndSaysSoOnce` — with `RoutedUtc` backdated past the cap, one supervisor
  attention entry appears whose subject names the reviewer, and exactly one after three more ticks.
- `AClosedReviewerClosesTheContractWithoutAnAlarm` — nobody is coming, the hold is released, and
  nothing is appended: the supervisor learns it from the fix report it is now handed.

> **Note for the implementer:** backdate through the CONTRACT FILE, not by sleeping — write a
> `Routed` contract with a `RoutedUtc` two hours old and run the tick. The clock seam the bridge
> harness offers is coarse, and a test that waits 90 minutes is not a test.

- [ ] **Step 2: Run it and watch it fail**
- [ ] **Step 3: Implement the three exits inside the sweep**
- [ ] **Step 4: Run the tests, plus the pause register again** (the alarm must sit BELOW the pause
      screen, the way `Raise_WakeStall_IfTicketWentUnanswered` does — there is already a test next
      door asserting exactly that shape for the ticket sweep; write its twin.)

```bash
dotnet test AIOrchestratorCoreLib.Tests/AIOrchestratorCoreLib.Tests.csproj \
  --filter "FullyQualifiedName~RoutedReportCapTests|FullyQualifiedName~PauseGatesEveryWakerScanTests"
```

- [ ] **Step 5: Commit**

```bash
git add AIOrchestratorCoreLib/Bridge/BridgeEngine/BridgeEngineModel.cs AIOrchestratorCoreLib.Tests/Bridge/RoutedReportCapTests.cs
git commit -F /tmp/cm.txt   # "feat(bridge): a re-review that never comes back gives the supervisor its round back, and says so once"
```

---

## Task 10: The kit — three sessions have to know this exists

**Files:**
- Modify: `kit/skills/implementer/SKILL.md`, `kit/skills/reviewer/SKILL.md`, and the supervisor's reviewer-briefing section (`kit/skills/supervisor/SKILL.md` OR `kit/skills/supervisor/reference/reviews.md` — grep for it, see Step 3)
- Modify: `AIOrchestratorCoreLib.Tests/Kit/RoleCommandMarkerTests.cs`

**Why the test file changes too:** `RoleCommandMarkerTests` walks both directions — a marker-looking
phrase a skill teaches must be real vocabulary, and a marker the code ACTS on must be taught
somewhere. `REROUTE:` and `FIXED:` are now both. Add them to `TAUGHT_MARKERS`, beside `HANDOVER`,
`STATUS` and `ANSWERED`, referencing `ChannelGrammar.REROUTE` / `ChannelGrammar.FIXED` — never a
literal.

**Order matters.** Do the implementer and the reviewer first; do the supervisor LAST, and re-read it
immediately before editing (Global Constraints: another agent is working in that file).

- [ ] **Step 1: The implementer — one rule, under `## Channel protocol`**

> **A brief that carries `REROUTE:` is answered with `FIXED: <commit>`.** Your supervisor has told
> the app to hand your fix straight to a reviewer instead of waking itself to do it. End that report
> with one line, `FIXED: <the commit the reviewer should diff to>`, and nothing routes without it —
> no line, no relay, and your supervisor picks the round up by hand as it always did. One `FIXED:`
> line per report: two make the head commit ambiguous and the app refuses rather than guess.

- [ ] **Step 2: The reviewer — a new subsection under `### A RE-REVIEW reviews the FIX`**

> **A re-review brief may arrive FROM the app.** An entry signed `app` and tagged `[routed]` in your
> channel is a re-review brief: your supervisor declared the contract in advance, and the app
> delivered the delta, your supervisor's own words, and the implementer's own report when the fix
> landed. It is not a member talking to you — members never write in each other's channels — and it
> changes nothing about how you work: the delta and the named findings, nothing else, and your report
> goes in your channel as always. If the implementer's report is marked TRUNCATED and you need the
> rest, ask your supervisor.

- [ ] **Step 3: The supervisor — FIND THE SECTION FIRST, then add a subsection under it**

```bash
grep -rn "Briefing a reviewer" kit/skills/supervisor/
```

At the time this plan was written that heading had just moved from `kit/skills/supervisor/SKILL.md`
into `kit/skills/supervisor/reference/reviews.md`, under parallel work on this branch. Write into
whichever file the grep names, never into both — a rule in two places is CLAUDE.md decision 12 in the
kit.

> **You can skip the postman round: `REROUTE: <reviewer> from <commit>`.** End a fix brief with that
> line and everything under it is the re-review brief the app will deliver to that reviewer the moment
> the implementer declares `FIXED: <commit>`. You are not woken for the fix report — you read it
> beside the re-review findings, on the turn where a verdict is possible. Rules: name a REVIEWER that
> exists; name the commit the reviewer last reviewed, because the delta is from it; write under the
> line exactly what that round covers — those words go to the reviewer verbatim; and **do not also
> brief the reviewer yourself**, or it gets the round twice. `REROUTE: cancel` retracts one. No
> `REROUTE:` line means nothing changes: the round works exactly as it always has.

- [ ] **Step 4: Run the kit guards**

```bash
dotnet test AIOrchestratorCoreLib.Tests/AIOrchestratorCoreLib.Tests.csproj --filter "FullyQualifiedName~Kit"
```
Expected: PASS, both directions of `RoleCommandMarkerTests` included. If it reports that a role
teaches a phrase the matcher does not act on, you spelled a marker by hand somewhere — fix the
spelling, do not widen `NOT_MARKERS`.

- [ ] **Step 5: Commit**

```bash
git add kit/skills/implementer/SKILL.md kit/skills/reviewer/SKILL.md kit/skills/supervisor/ AIOrchestratorCoreLib.Tests/Kit/RoleCommandMarkerTests.cs
git commit -F /tmp/cm.txt   # "docs(kit): the three sides of a re-review contract, taught where each session reads"
```

---

## Task 11: The acceptance — ONE supervisor wake where there were two

**Files:**
- Test: `AIOrchestratorCoreLib.Tests/Bridge/AFixRoundCostsOneSupervisorWakeTests.cs`

**This is the task that proves the plan.** Everything before it is machinery.

**The oracle, and why it is honest.** In `wake: ticket` mode the app writes the supervisor's wake-ups
as numbered files (`WakeTicket_Store`), so a supervisor's wake-ups are COUNTABLE from a test — the
ticket number is the count. Drive a whole fix round through the real engine tick and assert the
number moved **once**, and that the one turn it bought carries both the fix report and the re-review.

```
setup      terminal supervisor, wake: ticket, one implementer, one reviewer
round 1    rev-1 files findings                              → ticket n+1   (the supervisor's own turn)
           supervisor appends verdict + fix brief + REROUTE:  (written by the test, as the turn would)
step       imp-1 files "F1 fixed / FIXED: def5678"
assert     the reviewer's channel has the relay              ← the app did the postman's round
assert     the ticket number is STILL n+1                    ← the wake that used to happen, does not
step       rev-1 files the re-review
assert     the ticket number is n+2                          ← ONE wake, not two
assert     the state pack of that ticket holds BOTH entries  ← nothing was lost by being held
```

The last assertion is what stops this being a feature that saves tokens by dropping information: the
held report must be IN the turn that finally starts, and plan 01's Task 11 already makes the ticket
name a state pack whose contents can be read from disk.

- [ ] **Step 1: Write the test, watch it fail with the contract line removed, then pass with it**

Run it BOTH ways — with the `REROUTE:` line and without — in the same file. The without-case is the
live control: it must show **two** ticket increments. A saving asserted without its control is a
number with two routes to it (decision 20).

```bash
dotnet test AIOrchestratorCoreLib.Tests/AIOrchestratorCoreLib.Tests.csproj --filter "FullyQualifiedName~AFixRoundCostsOneSupervisorWakeTests"
```

- [ ] **Step 2: Run the whole suite, in the background (it is ~3 min 20 s and your command timeout is 120 s)**

```bash
export PATH="$HOME/.dotnet:$PATH"
dotnet test AIOrchestratorCoreLib.Tests/AIOrchestratorCoreLib.Tests.csproj > /tmp/suite.txt 2>&1 &
# then poll: tail -3 /tmp/suite.txt
```
Expected: 0 failed, apart from the three named flakes — and re-run any of those in isolation before
calling it a flake rather than yours.

- [ ] **Step 3: Commit**

```bash
git add AIOrchestratorCoreLib.Tests/Bridge/AFixRoundCostsOneSupervisorWakeTests.cs
git commit -F /tmp/cm.txt   # "test(bridge): a fix round with a contract costs one supervisor wake, and without one costs two"
```

---

## Task 12: The report

**Files:**
- Create: `docs/superpowers/plans/2026-09-15-one-wake-model-03-report.md`

- [ ] **Step 1: Write it**, in the shape of plan 01's report: which copy of each file was read (branch
  source, build output, installed kit — decision 18); the commands run and their output; the suite
  count; every red and whose it was; and, separately and plainly:

  - **what is measured** — the ticket count from Task 11, which is a test and not a live machine;
  - **what is NOT measured** — the live saving. It needs a machine that actually runs fix rounds with
    `wake: ticket` set. On `wake: watcher`, still the shipped default, the supervisor's monitor wakes
    it on the fix report whatever the app decides: the reviewer gets a correct scoped brief, the
    supervisor does not write it, and the wake-up is NOT saved. Say that in one sentence rather than
    quoting a saving nobody has collected;
  - **the kit is verified against the branch source and the kit guards, not against a restarted
    session** (decision 17), unless the owner has merged and reinstalled — in which case say which
    installed copy you read and what `grep -c "REROUTE" ~/.claude/plugins/cache/...` returned.

- [ ] **Step 2: Commit**

```bash
git add docs/superpowers/plans/2026-09-15-one-wake-model-03-report.md
git commit -F /tmp/cm.txt   # "docs(plans): the routed-report report — what was measured, what was not, and which copies were read"
```

---

## Acceptance for the whole plan

- `dotnet test` green apart from the three named flakes, each re-run green in isolation.
- The full fix round of Task 11 costs **one** supervisor wake with a contract and **two** without, in
  the same file, in the same run.
- **No edit to any existing wake oracle** — `WakeUpPolicyTests`, `PrintTurnTriggerTests`,
  `TurnCursorTests`, `WakeDecisionResolverTests`, the digest and agent-note tests. An orchestration
  with no contract behaves identically, which is the definition of this feature being additive.
- `PauseGatesEveryWakerScanTests` carries the new sweep's row and passes.
- `RoleCommandMarkerTests` passes in both directions with the two new markers.
- The relay entry is `[agent]`-tagged in every test that reads it: **nothing here reaches Telegram.**
- No member ever appends to a channel that is not its own, anywhere in the diff.

## What is deliberately NOT in this plan

- **Routing anything other than a fix report to a reviewer.** No relay of a reviewer's findings to an
  implementer, no implementer-to-implementer anything. The direction chosen is the one the spec names
  and the one where the supervisor's judgement is genuinely not needed in between, because the
  supervisor already stated the round's scope when it declared the contract. Every other direction
  requires a decision, and decisions are the supervisor's.
- **Making `REVIEW_CAP` a settings-catalogue row.** Right in principle, unasked for, parked
  (decision 22).
- **Automating the round's END.** The final verdict is the supervisor's, always. The app never
  accepts, never closes a ledger line, and never tells an implementer it is done.
- **Removing the supervisor from the first round.** The findings still wake it, and they must: that
  turn is where the round's scope is decided, and the contract is what it writes there.
- **A second `REROUTE` shape for a reviewer that needs a re-re-review.** A third round is exactly
  where the supervisor's own skill says its judgement is required ("Two rounds, then it is your
  call"), so the supervisor declares a new contract by hand or does not. The app proposes nothing.
- **Any change to the channel format, the append tool, the lock, the compactor, Telegram, the
  delivery modes, or the reviewer's independence.**
