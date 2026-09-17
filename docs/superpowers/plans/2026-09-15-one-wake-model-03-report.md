# One Wake Model — Plan 03 report: the routed report

**Date:** 2026-09-17 · **Branch:** `feat/one-wake-model` (worktree `../AIOrchestrator-relations`), tasks 1–10 at `371cdbb` · **Plan:** `2026-09-15-one-wake-model-03-routed-report.md` · **Spec:** `../specs/2026-09-15-one-wake-model-design.md`, step 4 · **Predecessors:** `2026-09-15-one-wake-model-01-report.md` (shipped), `2026-09-15-one-wake-model-02-report.md` (nothing here depends on it)

## Copies read

**Branch source, this worktree** (CLAUDE.md decision 18), for every finding below unless the line says
otherwise. The test runs are this session's own, on this machine, from this worktree's build output —
not taken from a sub-agent's report. **No running app on this machine**, so nothing here is a reading
of the fourth copy (decision 23). The kit was verified against `kit/` in this worktree and its three
bash benches; **not** against a restarted session, which under decision 17 is the weaker of the two
verifications and is stated rather than glossed — see the kit paragraph below for what `grep` on the
installed cache returned.

`dotnet` is at `~/.dotnet/dotnet` (SDK 10.0.401), not on PATH.

## What shipped

| task | commit | what |
|---|---|---|
| 1, 2 | `9b1ef91` | the two marker words in the grammar JSON, and `Is_Inbound` learns one case |
| 3, 4, 5 | `2476aa7` | the contract, its reader, and a judge that never judges a fix |
| 6 | `60185e3` | the relay entry — the app copies, it never composes |
| 10 | `a40c595` | the three sessions that must know the contract exists |
| 7 | `0eaae3e` | the hold: a routed fix report RIDES the supervisor's next turn |
| 8, 9 | `371cdbb` | the postman's round, and the three ways a routed round ends |
| 11, 12 | *this branch* | the acceptance test, and this report |

## Task 11 — the acceptance, and the count I measured

`AIOrchestratorCoreLib.Tests/Bridge/AFixRoundCostsOneSupervisorWakeTests.cs`, two cases that differ in
**one line of one channel entry** — the `REROUTE:` the supervisor's verdict carries — and in nothing
else. Both drive the whole round through the engine's real tick. The oracle is the wake-ticket number
on disk, which the app writes for its own reasons under `wake: ticket`.

| step | with the contract | without it (the live control) |
|---|---|---|
| `imp-1` files its round-one report | ticket 1 | ticket 1 |
| `rev-1` files findings | ticket 2 | ticket 2 |
| the supervisor's verdict + fix brief (`+ REROUTE:`) | — its own words | — its own words |
| `imp-1` files `FIXED: def5678` | **relay written to `rev-1`; still ticket 2** | **ticket 3** |
| `rev-1` files the re-review | **ticket 3**, its pack carrying BOTH entries | ticket 4 |
| **over the round (findings → re-verdict)** | **2 supervisor wake-ups** | **3** |

**The plan's count is confirmed, not corrected: three wake-ups a round today, two with a contract, one
removed.** Two details of the fixture are load-bearing rather than decoration, and both were measured
rather than assumed:

- **The silence is waited out PAST the digest window.** "Still ticket 2" has two routes to it — the
  hold, or a digest that has not elapsed — so the case waits the full window and some, and the control
  beside it takes its extra ticket inside exactly that wait. Decision 20.
- **The round-one report is part of the fixture.** A fix round always follows a report the supervisor
  has already been handed from that implementer's channel, and the digest's first-entry exemption turns
  on precisely that.

### Where the saving is NOT collected, measured on 2026-09-17

`Sweep_WakeTickets_Async` runs **above** `Sweep_RoutedReports_Async` in the tick. The engine's comment
there says the one-tick lag costs nothing "because member traffic waits out MemberDigestWindow before
it can wake anyone". **That is true only while the digest is holding the fix report**, and two
supported configurations break it. Both were measured by running the contract case with one thing
changed, and both gave **2 wake-ups where the assertion expects 1** — the relay is still written, but
the wake it is meant to spare has already happened:

| change | result |
|---|---|
| `printRunner.memberDigestMinutes: 0` (the digest turned off — a documented setting) | `Expected: 2 / Actual: 3` — the round costs what it costs without a contract |
| the round-one report removed, so the fix report is the FIRST entry the supervisor is handed from that channel (`WakeUp_Policy`'s first-entry exemption) | `Expected: 1 / Actual: 2` — same |

Neither is pinned as a test: a case asserting them would be pinning a defect as intended behaviour.
Both are written into the test's own docstrings and listed under Gaps. The second is not reachable by
an ordinary fix round — the supervisor has been handed that implementer's work report before it can
send it to review — but **the first is one line of `config.json` away**, and it is the one to fix if anyone
ever reports "the contract did nothing".

## The measurement, and what is NOT measured

**MEASURED:** the ticket count in the table above — a test, driving the real engine tick over real
files, on this machine.

**NOT MEASURED, and this is the plain sentence the plan asked for: nothing in this plan has been
observed in service.** No machine has yet run a fix round with `wake: ticket` **and** a real contract.
The saving is a count over the protocol, not an observation. Under `wake: watcher` — still the shipped
default — the supervisor's bash monitor fingerprints its channel files and wakes it on the fix report
whatever the app decided: on such a machine this plan buys the reviewer a correctly scoped brief and
the supervisor a turn it does not have to compose, and **the wake-up is not saved**. The same shape as
plan 02's report, which says its second column is modelled and not measured; here the whole saving is.

To collect it: `{ "runners": { "supervisor": { "runner": "terminal", "wake": "ticket" } } }`, a real
fix round with a `REROUTE:` line in the verdict, and the ticket numbers in
`orchestrator.log.jsonl` (`wake ticket <n> —`) against the round's entries.

## Commands run, and their output

Only targeted `--filter` runs. **The full suite was not run from here** — the dispatching session's,
per the brief; its baseline for this branch is **4 224 passed / 10 skipped / 0 failed** (`371cdbb`).

```
dotnet test … --filter "FullyQualifiedName~AFixRoundCostsOneSupervisorWakeTests"
  Passed!  - Failed: 0, Passed: 2, Skipped: 0, Total: 2, Duration: 12 s     (three runs, 12 s each)

dotnet test … --filter "…RoutedReport|…WakeTicket|…PauseGatesEveryWakerScanTests|
                        …WakeDecision|…RoutedReportRidesTheNextTurnTests|…AFixRoundCostsOneSupervisorWakeTests"
  Passed!  - Failed: 0, Passed: 88, Skipped: 0, Total: 88

dotnet test … --filter "FullyQualifiedName~Tests.Kit"
  Passed!  - Failed: 0, Passed: 235, Skipped: 0, Total: 235

bash kit/hooks/watcher-behaviour-check.sh   →  44 checks, 0 failures
bash kit/hooks/hook-behaviour-check.sh      →  All cases match intent.
bash kit/self-write-suppression-check.sh    →  all cases passed
```

**The mutation, red then restored.** With `RoutedHold_Policy.Resolve_RidingOnly`'s one effective line
(`riding.Add(contract.ReportIdentity)`) removed — the hold gone, everything else untouched:

```
[FAIL] AFixRoundCostsOneSupervisorWakeTests.AFixRoundWhoseVerdictCarriedAContract_…
  Assert.Equal() Failure: Values differ
  Expected: 2
  Actual:   3
Failed!  - Failed: 1, Passed: 1, Skipped: 0, Total: 2
```

Exactly one case red, on the ticket count, and the control green beside it — which is what makes the
green a statement about the hold and not about the fixture. Restored (`git diff` clean on
`AIOrchestratorCoreLib/`), green at 2 again.

**The unstable pair `371cdbb` noticed was re-run here, not cited:**
`ClosingTurnTests.AClosingTurnThatAlsoTimesOut…` and
`PrintTurnDispatcherTests.ARequestIdAlreadyExecuted_IsNeverRunAgain`, together, in isolation,
**3/3 green** (20 s each run). They belong with the wall-clock family already tracked
(`ClosingTurnReviewFixTests`, `TolerantFileReaderTests`, `EffortDialOnABridgeDrivenSupervisorTests`)
— five now, not three.

## Acceptance for the whole plan, item by item

| # | criterion | state |
|---|---|---|
| 1 | suite green apart from the named flakes | **the dispatching session's run** — not claimed here; every targeted family above is green |
| 2 | the round costs one wake with a contract and two without, same file, same run | **met** — and the raw ticket numbers are in the table, not a difference |
| 3 | no edit to any existing wake oracle | **met** — task 11 adds one file and touches nothing; `git status` shows a single untracked test |
| 4 | `PauseGatesEveryWakerScanTests` carries the new sweep's row and passes | **met** (in the 88) |
| 5 | `RoleCommandMarkerTests` passes in both directions | **met** (in the 235) — but see gap 2: the two new markers are covered by only one of those directions |
| 6 | the relay is `[agent]`-tagged in every test that reads it | **met** — `RoutedReportSweepTests` asserts it twice; nothing here reaches Telegram |
| 7 | no member ever appends to a channel that is not its own | **met** — the relay is `ChannelAppender.Append_AppEntry`, signed `app`, into the reviewer's own file |

## Gaps, stated

1. **The saving is gated on the fix report being DIGESTABLE, not merely on a contract existing.**
   Measured, above: with `memberDigestMinutes: 0`, or on a first-contact channel, the round costs two
   wake-ups with the contract exactly as without. The fix is an ordering one — the postman's round
   above the wake sweep in the tick — and the engine's comment explaining the current order
   (`BridgeEngineModel.cs`, above `Sweep_RoutedReports_Async`) states its own precondition without
   noticing it is one.
2. **`ChannelAppender` does not clean bodies, and the relay carries a member's words verbatim.** A fix
   report containing a line shaped like an entry header (`## [12] FROM supervisor — …`) outside a
   closed fence would open a phantom entry in the REVIEWER's channel — `Append_Entry` writes
   `body.Trim()` with no escaping, and `RoutedReport_Composer` inserts the report unchanged.
   **Pre-existing and not this plan's**: the app already forwards the owner's Telegram text the same
   way. Putting a guard in the composer would mean the app reformatting a member's words, which task 6
   forbids in as many words. Recorded, not fixed.
3. **`TAUGHT_MARKERS` does not cover `REROUTE:` and `FIXED:`, and its docstring reads as if it did.**
   The guard runs two directions: "every marker-looking phrase in a role command is real" walks
   `ChannelGrammar.All_Markers` (which the grammar JSON feeds, so the new words ARE covered there), and
   "every marker a role must write is taught" walks `TAUGHT_MARKERS` — a hand-written list, which the
   new words are not in. So nothing would notice if the implementer skill stopped teaching `FIXED:`.
   **And neither direction reads `kit/skills/*/reference/*.md`**: `KitRepoFiles.Find_AllRoleProtocols`
   returns `kit/skills/<role>/SKILL.md` and nothing else, so the supervisor's half of this contract —
   which lives in `kit/skills/supervisor/reference/reviews.md` — is invisible to the guard in both
   directions. A marker taught only in a reference file counts as untaught.
4. **`REVIEW_CAP` (90 min), `HOLD_CEILING` (180 min) and `HANDLED_MEMORY` (200) are constants, not
   catalogue rows.** Right in principle — the model and the effort are data — unasked for, parked
   under decision 22.
5. **`reroute.json` is read without any cache, and the sweep's own cost paragraph under-describes the
   steady-state cost.** `RerouteContract_Store.Read_Set` reads the file on every call; per tick that is
   at most one read from the wake decision (`RoutedHold_Policy.Resolve_RidingOnly`, gated on the
   session being a SUPERVISOR with pending traffic) plus one from the postman's round when the file
   exists. **The brief's "three call sites" is two in production**: `PrintTurnDispatcherModel:801` and
   `BridgeEngineModel:1985`; the third, `WakeDecision_Resolver.Resolve_OrNull:82`, still has no
   production caller — re-verified here, as plan 01's report gap 2 recorded. **The larger cost is not
   the JSON at all:** `Open_RerouteContracts` walks every open implementer channel's entries BACKWARDS
   on every tick, calling `Contains_Marker` on each supervisor entry, and when no `REROUTE:` was ever
   written it walks the whole history every time. The entries come from `ChannelHistory_Cache`, so
   there is no re-read — but the scan is O(entries) per implementer per tick, for ever, on every
   orchestration that has a reviewer and has never used this feature. The type header promises "one
   `File.Exists` per open orchestration, plus one cache-validation stat per open implementer channel",
   which is the I/O and not the work.
6. **Two questions the plan put to the owner are still unanswered, and both ship as decided
   provisionally.** `REVIEW_CAP = 90 minutes` is **a judgement, not a measurement** — a re-review is
   `quick` by construction, so 90 is generous, and being wrong costs one extra supervisor wake, which
   is the direction of more supervision. And the relay carries **the implementer's report body in
   full**, capped at 8 192 characters with the cut stated
   (`RoutedReport_Composer.MAXIMUM_REPORT_CHARACTERS`). If the owner wants a blinder re-review, that
   cap goes to zero and `RoutedReportComposerTests.TheImplementersReportIsCarriedVerbatim` changes.
7. **A second unstable pair joins the tracked family** (above) — wall-clock, not this plan's, green
   3/3 in isolation here.
8. **The kit is verified against the branch source and its benches, not against a restarted session**
   (decision 17). `~/.claude/plugins/cache/` on this machine carries **none** of this series:
   `grep -r "REROUTE" ~/.claude/plugins/cache/ | wc -l` → **0**, read on 2026-09-17, because
   `known_marketplaces.json` registers `aiorch-local` at `/Users/nvene/Visual Studio/AIOrchestrator/kit`
   — the MAIN checkout, which is correct and must stay so (plan 01's report says why). What the
   installed sessions read is unchanged until the owner merges and reinstalls.
9. **The digest window's config key is `printRunner`, not `limits`.** Not a defect — the name is
   deliberate and documented (`RunnerConfigs_Json.LIMITS_KEY = "printRunner"`) — but the constant's
   NAME says `LIMITS` and the docstrings say "under `LIMITS_KEY`", which cost this task one red run
   before the fixture worked. Worth a minute of the next person's life.

## What this plan did NOT change

No channel-format change: `ChannelAppender`, `kit/bin/channel-append.sh`, `ChannelWrite_Lock` and
`Channel_Compactor` are untouched, and the two new words are rows in the grammar data file, which is
what that file is for. No Telegram change — the relay is `AppEntryAudiences.Agent` and never reaches
the phone (decision 15). **No change to review independence**: the reviewer still has no write tools
and no stake, its findings are still input to a verdict and never a verdict, and the relay carries the
supervisor's own words and the implementer's own words with no judgement of the app's between them.
**No member ever writes in another member's channel** — the hub gained a clerk, not an edge
(decision 4). The routing is OPT-IN: no `REROUTE:` line, no contract, no hold, so **no orchestration
behaves differently until a supervisor writes one** — and the wake it saves is saved only where
`runners.<role>.wake` says `ticket`, which is not the shipped default.
