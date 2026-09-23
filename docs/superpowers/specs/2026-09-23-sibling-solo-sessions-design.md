# Sibling solo sessions: one endeavour, several topics

**Date:** 2026-09-23
**Status:** proposal approved by the owner ("love your proposal, let's do it"). This spec is waiting for the four owner decisions in §9.
**Branch:** `feat/sibling-solos`, off `master` at `83da77c`.
**Copy read (decision 18):** every claim about existing behaviour below comes from the branch source in the worktree `AIOrchestrator-siblings` at `83da77c`. It says nothing about the build output, the installed plugin or the running app (decision 23). Line numbers are approximate and drift, so each citation names a symbol and gives the line only to help you find it.
**Scope:** `AIOrchestratorCoreLib` (requests, sessions, launcher, bridge engine, print runner, planning, Telegram text), `kit/skills/solo/**`, `kit/skills/general-supervisor/SKILL.md` (one paragraph), the bash harnesses in `kit/`, and the tests.
**Out of scope:** the WPF app beyond whatever it gets for free from the engine; crews (full orchestrations); the supervisor role.

---

## 0. The owner's request, and the proposal they approved

The owner, 2026-09-23:

> *"sometime I have to perform some parallel work, like two big parallel tasks. And the solo can't
> really do both, he will concentrate on one talking with me, but won't go ahead with sub agents do
> the other. I think we should design a way, to be able to have a solo spawn some sort of parallel
> solo session, which can talk both to me and to the first solo, we should kind of have all access to
> each other conversation to be updated on how the whole endeavour is going, but so I can talk to one
> about a job, and to the other about the other job. But the telegram topic would be a mess, it would
> be impossible to talk to one with the other knowing what message is for whom."*

The owner approved this proposal:
- A solo asks the app to spawn a **sibling** solo, using a request file plus a handover brief.
- The app opens the sibling as **its own orchestration with its own Telegram topic**, so each thread carries exactly one job.
- Siblings are **linked** in session.json.
- Each sibling reads the others' PLAN.md and recent owner-channel entries at every boundary.
- Siblings talk to each other on a **private duplex channel**, the way a supervisor and an implementer do.
- The siblings work in disjoint worktrees, and the endeavour has **one shared progress bar**.

---

## 1. Goal and non-goals

### Goal

Let the owner run two (or a few) big jobs in parallel from the phone. Each job has:
- its own thread;
- its own session doing the work;
- its own progress line.

The jobs are still one endeavour: the sessions know what the others are doing, they can coordinate without the owner acting as a relay, and the owner sees one combined bar.

### Non-goals

- **Not a crew.** There is no supervisor, no reviewer, and no gate between the siblings. Every sibling is a solo, and the owner is still the reviewer of each one (`kit/skills/solo/SKILL.md`, "The owner IS your reviewer").
- **Not a supervisor in disguise.** No sibling briefs, verifies or commands another. The parent that asked for the sibling has no authority over it once it exists. They are peers.
- **Not a replacement for fan-out.** Work that only needs to go *wide* is still the solo's own parallel sub-agents (`solo/SKILL.md` "FAN OUT", CLAUDE.md decision 16). A sibling is for work that needs its **own conversation with the owner**, because the owner wants to steer it separately.
- **Not a replacement for promotion.** A job that needs an independent review or a coordinated crew still goes through `promote-orchestration` (`OrchestrationRequests_Reader.PROMOTE_ORCHESTRATION_ACTION`).
- **No shared topic, no shared owner-channel, no shared PLAN.md.** §3 explains why each of these would bring back exactly the mess the owner described.
- **At most N open siblings per endeavour.** N is 3 unless the owner decides otherwise (§9, O2).
- **Crews cannot have siblings in v1.** A linked orchestration cannot be promoted or `/switch`ed (§7.6).

---

## 2. What the owner sees on the phone

### 2.1 Birth

1. The owner, in topic `AI-Orch · settings`, writes: *"do the limits rework in parallel, I want to follow both."* The solo may also suggest a sibling itself, but only as a question to the owner. It never requests one on its own initiative (§8.1).
2. The solo prepares the handover:
   - it creates the child's worktree (§6);
   - it appends a `HANDOVER` entry to **its own sibling outbox** (§3.3);
   - it drops the `spawn-sibling` request (§4).
3. The app **holds** the request and posts a confirmation prompt **in the requesting topic**. The prompt goes through the same machinery as close and promote: `CloseConfirmation_Parking.Park`, `Ask_OwnerToConfirmClose_Async` in `BridgeEngineModel.cs` around line 5886, and callback data `close-yes-{guid}` / `close-no-{guid}`. The prompt reads:

   > 🔗 **settings** wants a sibling session for a parallel job
   > New topic: **AI-Orch · limits**
   > Job: *Rework the usage-limit pause so a restored pause can be lifted per window*
   > Why: *two jobs you want to steer separately; they touch disjoint files*
   > [✅ Start it] [✋ Keep one session]

   Whether this tap is required at all is owner decision **O1**. The recommendation is yes.
4. On ✅ the following happens:
   - A new orchestration `ai-orchestrator-<n+1>` starts.
   - It gets its topic when its first entry is mirrored (`Resolve_ThreadId_OrNull_Async`, around line 4906). The topic is created under the raw orch id and renamed to the requested name on the same tick by `Sync_TopicNames_BestEffort_Async` / `Build_WantedTopicName` (line 9917).
   - The topic **inherits the repo's colour** automatically, because `Resolve_TopicColour_OrNull(repoName)` (around line 5148) is keyed on the repo. So siblings look alike in the topic list without any new code.
   - The first message in the new topic is the app's birth note: `🔗 Sibling of AI-Orch · settings — job: … Write here about this job only.`
   - The requesting topic gets one line: `⚙ App: sibling started — AI-Orch · limits (its own topic)`.

### 2.2 Talking to each

- **The owner addresses a sibling by writing in its topic, and in no other way.** Routing is unchanged: `Route_OwnerMessage_Async` (around line 13529) resolves thread id → orch id through `Find_ByTelegramTopicId_OrNull` and appends to that orchestration's `owner-channel.md`. Nothing in any topic ever needs to be "for whom".
- The owner may **ask either sibling about either job.** Each sibling knows the other's state from its digest (§5) and answers from it. A sibling **never acts** on the other's job. If the owner gives topic A an instruction that belongs to B's job, A relays it verbatim through its outbox (§8.3) and tells the owner in one line that it has passed it on.
- **Sibling-to-sibling traffic is never texted** (owner decision **O3**; the recommendation is never). The owner sees it on demand with `/endeavour` in any sibling topic (§2.4).

### 2.3 Closing

- Each sibling closes like any orchestration. The sibling files `close-orchestration` and the owner taps (`Process_CloseOrchestrationRequests`, around line 5649), or the owner types `/close` in that topic. **Closing one never closes another.**
- The surviving siblings get an `[agent]` entry: `sibling 'limits' closed — its outbox and PLAN.md stay on disk; unfinished lines: …`. The owner is not texted about this: they just tapped the close, so decision 15 applies.
- When the last open sibling closes, the endeavour simply has no open members. Nothing else needs to happen, because membership is derived (§3.1).

### 2.4 What General shows

`/progress` in General and the General dashboard share one body, `Build_ProgressReportText(null)` (around line 7601), which `GeneralDashboard_Composer.Compose` wraps. That body **groups linked orchestrations** under an endeavour line:

```
🔗 AI-Orch · settings + limits — 9/14 done (64%)
   · AI-Orch · settings: 6/8 done (75%) · 1 running
   · AI-Orch · limits: 3/6 done (50%) · 1 running
AI-Orch · away mode loop: 4/4 done (100%)
```

- An orchestration with no endeavour renders exactly as today. The golden-text test in §10 pins that.
- In a sibling topic, `/progress` still shows **that topic's** ledger: one topic, one job. The new command `/endeavour` shows the combined block plus the last three outbox subjects of each sibling.

---

## 3. Data model

### 3.1 Why a sibling is its own orchestration

The deciding fact: **almost every per-session mechanism in this system is keyed on the orchestration id.** A sibling that *is* an orchestration inherits all of them unchanged:

| Mechanism | Keyed on | Where |
|---|---|---|
| Telegram topic, routing | `session.TelegramTopicId` | `Find_ByTelegramTopicId_OrNull`, `Route_OwnerMessage_Async` |
| owner channel | `<root>/<orch>/owner-channel.md` | `ISupervisionPaths.Get_OwnerChannelFile` |
| ledger | `<root>/<orch>/PLAN.md` | `ISupervisionPaths.Get_PlanFile`; `run-to-the-end-check.sh` reads `$AIORCH_ID/PLAN.md` |
| question hold | `<root>/<orch>/.awaiting-answer` | `AwaitingAnswerFlag_Marker`, `QuestionHold_Policy.Should_Hold` ("PER ORCHESTRATION, NOT GLOBAL", around line 12617) |
| owner debt | `<root>/<orch>/.ledger-behind` | `supervisor-ledger-check.sh` |
| pause | `session.Paused`, `.paused` | `Toggle_Paused_Async`, `PausedFlag_Marker`, `EffectiveMode_Resolver.Resolve` |
| resume | `<root>/<orch>/solo-1/.usage.json` | `ResumableSession_Resolver.Resolve_ForMember_OrNull` |
| watchdog | per slot `imp:<orch>/<member>` | `SessionWatchdogModel.Check_AllSessions` |
| `/model`, `/effort`, `/merge`, `/close`, `/pause`, `/pc` | the topic's orch | `BridgeEngineModel` command switch |

**Rejected: two solos in one orchestration (`solo-1`, `solo-2`).** This option breaks four things:
- Both solos would share one `owner-channel.md` and one topic. That is the mixing the owner called impossible.
- Both would sign `FROM solo`. `channel-append.sh` `derive_author` takes the author from `AIORCH_ROLE` and refuses any other word. `ChannelEntry_Parser.Parse_Author` knows role words only, so `FROM solo-2` parses as `Unknown`.
- `record_self_write` keys its record **per author** (`${CHANNEL}.self-write.${author}`). Solo-2's append would therefore write the very record solo-1's watcher checks, and solo-1 would **sleep through its sibling**. `watcher.md` names that as the failure it must never allow ("the saving is small and what it costs is the owner's message").
- The Stop hooks read one `PLAN.md` and one `.awaiting-answer` per `AIORCH_ID`, so each solo would be blocked by the other's open lines and the other's questions.

### 3.2 session.json

`IOrchestrationSession` (`Sessions/OrchestrationSession/IOrchestrationSession.cs`) gains four nullable fields. They are serialised by `SessionJson_Serializer` with absent meaning null, so every existing session.json loads unchanged.

| Field | Meaning | Written by |
|---|---|---|
| `endeavourId` | The endeavour this orchestration belongs to. **Its value is the orch id of the first orchestration**, e.g. `ai-orchestrator-7`: stable, readable, and never re-allocated because `OrchId_Allocator.Allocate_NextOrchId` only counts upwards. | The executor, on the parent the first time it gets a sibling and on every child at creation. |
| `bornFromOrchId` | The sibling that asked for this one. Kept for audit only; it grants no authority. | Executor, on the child. |
| `bornFromHandover` | `"<orch>#<n>"`: the HANDOVER entry that authorised this birth. It is the idempotency key (§4.4). | Executor, on the child. |
| `workingPath` | The directory the session is spawned in. Null means `RepoPath`, which is today's behaviour. | Executor, on the child. |

**Membership is derived, never stored as a list.** The siblings of X are `store.Load_All()` where `EndeavourId == X.EndeavourId`, excluding X. The proposal's `siblings[]` array was rejected:
- N arrays in N files are N copies of one fact.
- A close, a crash between two saves, or a hand edit desynchronises them.
- This codebase has paid for a second copy every time: decision 12's second duration formatter, and `ChannelDiscovery`'s missing reviewer prefix.
- `Load_All` is already cached per file (`OrchestrationSessionStoreModel` checks length and mtime), and `Build_ProgressReportText` already walks it every dashboard tick.

### 3.3 The sibling channel: one outbox per sibling

**Path:** `<root>/<orch>/sibling-outbox.md`, plus a new `ISupervisionPaths.Get_SiblingOutboxFile(orchId)`.

**Rule:** a sibling appends **only to its own outbox** (through `channel-append.sh`, as `FROM solo`) and reads everyone else's. Two outboxes, one per side, make up the "private duplex channel" of the proposal. For N siblings it is N files, each with exactly one writer.

This beats one shared file for the following reasons:
- **The author problem disappears.** Every writer is a `solo`, and the parser can only say `Solo`. The *file* says which sibling wrote an entry, so no new author word, `ChannelAuthors` value or `derive_author` rule is needed. `channel-append.sh` needs **no change**: `AIORCH_ROLE=solo` signs `FROM solo`, as it already does.
- **Self-write suppression is moot.** A sibling never watches its own outbox. The record `sibling-outbox.md.self-write.solo` that the helper writes is harmless and read by nobody. A shared record never exists, so the missed-wake failure of §3.1 cannot happen.
- **Wake loops are structurally impossible.** A's append fires only B's watcher. B's reply lands in B's outbox and fires only A's. Nothing the app writes goes into an outbox, so there are no app-authored wakes. The only loop left is two agents acknowledging each other's acknowledgements, and the prose forbids that (§8.2).
- **It is not tailed or mirrored.** `ChannelDiscovery.Find_ChannelFiles` enumerates only `owner-channel.md` and `imp-*`/`rev-*` spokes (`SPOKE_FOLDER_PREFIXES`), so a file in the orch folder root is never mirrored to Telegram. That is the O3 recommendation, obtained with no code.
- **Closing one sibling closes its outbox with it.** The audit trail stays in that orchestration's folder beside its PLAN.md.

**Compaction.** The outbox is not tailed, so the tick's compaction step (`Tailing/Channel_CompactionStep`) never sees it. A per-tick sweep calls `Channel_Compactor.Compact_IfNeeded(path)` (the no-guard overload) on each open outbox. That overload is safe here because no tailer cursor exists to re-anchor. The print runner's cursor is by entry identity (`PrintTurn_Trigger`, "PENDING IS DECIDED BY IDENTITY"), which compaction does not break. History readers go through `ChannelHistory_Counter` (decision 13).

### 3.4 Two derived files per linked orchestration

These follow the `.paused` / `.meeting` pattern: **derived, never authored**, reconciled every tick, removed when the fact stops being true. See `PausedFlag_Marker`, `MeetingFlag_Marker` ("IT MUST NOT OUTLIVE THE MODE").

- **`<root>/<orch>/.siblings`**: plain text for bash, one open sibling per line: `<orchId>\t<outbox path>\t<paused|live>\t<display name>`. The watcher loop reads it (§5.3). It is deleted when the orchestration has no open siblings.
- **`<root>/<orch>/ENDEAVOUR.md`**: the awareness digest the solo reads at every boundary (§5.1), built in C# by `EndeavourDigest_Builder`. It is rewritten through `Atomic_FileWriter` **only when its text changes**, to spare the disk the way `GeneralDashboard_Composer` spares Telegram ("NO CLOCK IN THE TEXT"). No watcher watches it, so a rewrite wakes nobody.

### 3.5 The shared progress bar: a sum of the linked PLAN.md files

**Decision:** the endeavour bar is computed, never written. `EndeavourProgress_Reader` parses each linked orchestration's PLAN.md with the existing `PlanLedger_Parser.Parse_OrNull` and sums `Done`, `InProgress`, `Blocked`, `BlockedOnOwner`, `Open`, `NotDoing` and `Total`. The skipped sections (`PlanLedger_Sections.NON_LEDGER_HEADING_PREFIXES`: `PARKED`, `OWNER REQUESTS`) are therefore skipped exactly as they are today. Formatting is `PlanProgress_Formatter.Describe_Counts`, so no second spelling of the bar exists.

**Closed siblings stay in the sum** for as long as any member of the endeavour is open. Otherwise a finished job that closes would make the bar go *backwards* at the moment it succeeded, which is the non-monotonic-count trap of decision 13 in another shape.

**Rejected: a shared endeavour ledger file.**
1. PLAN.md is edited with the Edit/Write tools, which the channel lock does not cover. Two writers on one file means one whole-file write clobbering the other's lines. That is the `imp-3` wipe `supervisor/SKILL.md` records, moved to the ledger.
2. `run-to-the-end-check.sh` and `supervisor-ledger-check.sh` read `$AIORCH_ID/PLAN.md`. A shared file would let one sibling's open lines refuse the other's turn end, or would need every hook rewritten.
3. Decision 22 requires every ledger line to trace to an owner request **in the conversation where it was asked**. Per-orchestration ledgers keep that trace local to one topic.

---

## 4. The request: `spawn-sibling`

### 4.1 JSON

It is written to `$AIORCH_SUPERVISION_ROOT/.requests/sibling-<orchId>-<timestamp>.json`. The orch id and timestamp in the filename follow the rule `solo/SKILL.md` already states for close requests.

```json
{
  "action":   "spawn-sibling",
  "orchId":   "ai-orchestrator-7",
  "name":     "AI-Orch · limits",
  "job":      "Rework the usage-limit pause so a restored pause can be lifted per window",
  "handover": 14,
  "worktree": "C:/Users/Gianpiero/source/repos/AIOrchestrator.worktrees/limits",
  "reason":   "two jobs the owner wants to steer separately; disjoint files"
}
```

| Field | Rule |
|---|---|
| `orchId` | The requester's own orchestration. Required. |
| `name` | The new topic's name. Required. It must have the platform-code form `<code> · <2-4 words>` (`solo/SKILL.md` "EVERY TOPIC NAME STARTS WITH THE PLATFORM CODE"). The code is not checked against the parent's, because a Strategy Lab endeavour can legitimately have an `IS` sibling ("A SUB-PRODUCT KEEPS ITS OWN CODE"). |
| `job` | One line, at most 200 characters. Required. The owner sees it on the prompt and in the birth note, and it becomes the child's first `FROM owner` entry (§4.3). |
| `handover` | The `[n]` index `channel-append.sh` printed when the requester appended its HANDOVER entry **to its own outbox**. Required. The helper allocates `n` inside the lock, so this is the one index an agent does not guess (decision 12). |
| `worktree` | Absolute path of an existing git worktree for the child. Required. Validated in §4.2. |
| `reason` | Why one session is not enough. Required, `MISSING_REASON_MESSAGE`. It is relayed to the owner. |

Absent from the request on purpose:
- **No model and no effort.** The child copies the parent's `ImplementerModelOverride` and `ImplementerEffortOverride`, the slot a solo reads (decision 24). Those are the owner's dial on this endeavour, and a half of the endeavour should not quietly run on a different one. With no override set, the catalogue default applies as it does for any solo (`OrchestrationLauncherModel.Respawn_Implementer`, around lines 450 and 476). This also means a session can never pick `fable` here (`FORBIDDEN_MEMBER_MODEL`).
- **No branch.** The worktree's HEAD is the branch, and the app reads it via `Git/GitSnapshot_Reader` for display. Two sources for one fact would disagree.

### 4.2 Parsing and validation

**The reader** is `OrchestrationRequests_Reader`, with a new constant `SPAWN_SIBLING_ACTION`, a new `ISpawnSiblingRequest` plus factory, and a new list on `IPendingRequests`. It checks **JSON only**: required fields, types, `handover` a positive integer, `job` length, and the `name` shape. It follows the precedent written in the promote case, which says facts about the world are the executor's, refused "WITH ITS OWN REASON".

The action must also be added to the `default:` arm's `known` list. That comment records what leaving one out cost: "actively teaching it the feature does not exist".

**The executor** is `Process_SpawnSiblingRequests`. It joins the `!dispatchPaused` group in `Process_PendingRequests` (around line 4999, "THE THREE THAT SPAWN"). A spawn during a usage-limit pause stays on disk and runs when the pause lifts, exactly like `start-orchestration`.

Each refusal below is:
- an `[agent]` entry (`AppEntryAudiences.Agent`) in the requester's owner channel;
- the request archived with `Archive_ResolvedRequest_BestEffort(path, <label>)`;
- **no owner involvement**, which is the promote precedent ("TWO REFUSALS BEFORE THE OWNER IS EVER INVOLVED").

| Check | Label | What the requester is told |
|---|---|---|
| requester missing or closed | `unspawnable` | general-channel failure, as in promote |
| requester is a crew (`!OrchestrationShape.Is_BasicOrchestration`) | `not-a-solo` | "a crew adds members with add-implementer" |
| endeavour already has N open members | `at-cap` | names the open siblings and says the owner can close one |
| entry `[handover]` missing from the requester's outbox history, not `FROM solo`, or without the `HANDOVER` marker | `no-handover-entry` | `HandoverEntry_Detector` checks the marker (subject, or start of a body line). History is read through `ChannelHistory_Counter.Read_Entries`, which spans live file and archive (decision 13). |
| a live or parked sibling already cites this handover | `handover-already-used` | **the idempotent answer**: "already started as `<id>`" or "already held — do not re-drop" |
| `worktree` is not a directory | `worktree-missing` | — |
| `worktree` is not in `git worktree list` for the requester's `RepoPath` | `worktree-not-of-repo` | — |
| `worktree` equals the requester's `RepoPath`, the requester's `workingPath`, or any open sibling's | `worktree-shared` | "siblings never share a tree" |
| `name` equals an open sibling's `DisplayName` | `name-taken` | — |

**Checked at the tap, not only at arrival.** A parked request can wait up to `CloseConfirmation_Parking.EXPIRY_HOURS` (12). `Ask_OwnerToConfirmClose_Async` already archives a request that has become `moot`, with a rule per kind. The Sibling kind re-runs the table above both at prompt time and at tap time, so a sibling born from a stale request is impossible.

### 4.3 Hold, tap, execution

A new `ParkedCloseKinds.Sibling` joins `Orchestration`, `Implementer` and `Promotion` (`GeneralSupervision/ParkedCloseRequest/IParkedCloseRequest.cs`).

- **Prompt.** `CloseConfirmationPrompt_Builder` gets `Build_ForSibling`. Its buttons are `("✅ Start it", "✋ Keep one session")`, and `Describe_AskedFor` / `Describe_NothingDone` get wording for the new kind.
- **Execute branch.** `Execute_ConfirmedClose` (around line 6330) **names every kind**, and its comment explains why a catch-all is a live hazard. The Sibling branch is named explicitly and calls `Execute_ConfirmedSibling`. A build that does not know the kind falls into the existing "cannot execute — NOTHING was done" arm, never into `Execute_Close`.

**`Execute_ConfirmedSibling`, in order:**
1. Re-validate (§4.2). Any refusal at this point goes to the requester and is archived.
2. If the parent has no `endeavourId`, set it to the parent's own orch id (`Set_EndeavourId`).
3. `_launcher.Start_SiblingOrchestration(...)`, a new sibling of `Start_BasicOrchestration` (`OrchestrationLauncherModel.cs` around line 89). In one call it:
   - allocates the id (`OrchId_Allocator`);
   - calls `Create_Orchestration`;
   - stamps `endeavourId`, `bornFromOrchId`, `bornFromHandover`, `workingPath`, `DisplayName` (the requested name) and the copied overrides **before** the spawn, because the terminal title is built at spawn (`SessionWindowTitle_Builder`);
   - seeds PLAN.md (`PlanSeed_Writer.Ensure_Exists`);
   - calls `Add_Member(orchId, MemberKinds.Solo)`.
4. **After the launch, never before** (the ordering rule in `Process_StartRequests` around line 5240: a bridge-driven session baselines at registration):
   - Append an **Owner-audience** `FROM app` entry to the child's owner channel. This is the birth note, the first message in the new topic. It names the parent, the job, and where the brief is: `<parent outbox> entry [n]`.
   - `ChannelAppender.Append_OwnerEntry(child owner-channel, job)`: the job as a `FROM owner` entry, then `Raise_OwnerWait(child)`. This is the exact `start-orchestration` precedent, and it is honest: the owner read these words on the prompt and tapped to approve them. `FROM owner` entries are never mirrored (`MirrorText_Formatter.Should_Mirror`), so the owner is not texted their own approval. The entry is what wakes a print-runner solo (`PrintTurn_Trigger.Is_Inbound(Solo, Owner)`) and what puts the child in owner debt, so its first act is an OWNER REQUESTS row.
5. Append an Owner-audience `FROM app` entry to the parent: `sibling '<id>' started — <name> (its own topic)`. Also post the general-channel outcome line (`Report_CloseOutcome_ToGeneral`'s sibling).
6. Archive the request as `started`.

**Declined:** an `[agent]` entry to the parent via `Decline_CloseConfirmation` (around line 6472, already kind-aware: "THE VERB COMES WITH THE PHRASE").
**Lapsed:** handled by the existing expiry sweep, `Expire_StaleCloseConfirmations`.

### 4.4 Idempotency

- One HANDOVER entry can birth **at most one** sibling. The key is `bornFromHandover` on the child plus the parked files, which `Find_Parked` enumerates.
- A retry of the same file is refused with the existing sibling's id. It is never a duplicate orchestration, which is the failure `start-orchestration` still has: CLAUDE.md decision 8 records "`--continue` once re-ran a failed start and duplicated orchestrations".
- A **second** sibling needs a **second** HANDOVER entry. That is deliberate: each child needs its own brief.

---

## 5. Awareness

### 5.1 What each sibling reads, and when

**At boot, and at every boundary** (the existing "RE-READ THIS CHANNEL AT EVERY BOUNDARY, before you write anything", `solo/SKILL.md`), a linked solo reads, in this order:

1. its own `owner-channel.md`, as today;
2. every sibling outbox listed in `.siblings`, from the last entry it has seen down;
3. its own `ENDEAVOUR.md`.

`ENDEAVOUR.md` is **bounded by construction**. It is built in C# from the parsers that already exist, so bash never parses markdown (decision 19 and the heredoc memories are why this is not a shell script). For each open sibling it contains:

- one header line: name, orch id, worktree, branch (via `GitSnapshot_Reader`), and `live` or `paused`;
- the counts line (`PlanProgress_Formatter.Describe_Counts`);
- the ledger lines that are **not finished** (`[>]`, `[ ]`, `[!]`, `[?]`), at most 15, in file order, with a `+k more` tail;
- the **last 6 owner-channel entries** that are `FROM owner` or `FROM solo`, skipping `[agent]` app entries, each shown as subject plus at most 400 characters of body, newest last;
- the subjects of the last 3 entries in that sibling's outbox.

**Cost.** Owner-channel entries are phone-shaped, since the app caps them at 600 characters (`solo/SKILL.md`). A sibling block is therefore about 4-6 KB (roughly 1.5k tokens), and with N=3 the digest stays under about 3k tokens per boundary. The outboxes are read incrementally, and the prose limits sibling entries to the same 600-character budget (§8.2).

**Why the solo pulls and the app does not push.** Pushing a digest into a channel would do two bad things:
- It wakes the reader (every channel is watched), paying a full context reload to learn nothing, which is exactly what self-write suppression was built to stop ("about half of every wake on this machine").
- It gives the app an author in a conversation where it has nothing to say.

A derived file that nobody watches costs nothing until the solo chooses to read it, which is at a boundary where it is already reading.

**Freshness.** The digest is regenerated on the mirror tick (every 2 s, and only when the text changes), so a boundary read is at most one tick stale.

### 5.2 Which changes wake whom

| Change | Wakes | Why |
|---|---|---|
| owner writes in topic A | A only | unchanged |
| A appends to A's outbox | every other live sibling | the only sibling-to-sibling signal |
| A appends to A's owner-channel | nobody else | B reads it at its next boundary via the digest. Waking B on A's owner conversation would double the cost of every exchange. |
| A edits A's PLAN.md | nobody | read via the digest |
| app rewrites `ENDEAVOUR.md` / `.siblings` | nobody | derived, unwatched |
| app writes an `[agent]` entry to A's owner channel | A only | unchanged |

### 5.3 The watcher

The watcher in `solo/reference/watcher.md` grows a second, independent fingerprint set. The owner-channel half stays byte-for-byte as it is, `self_write_suppresses` included. The sibling half works like this:

```bash
sibs="$orch/.siblings"   # derived by the app; re-read EVERY iteration, so a sibling born mid-loop joins
read_sib_fp() {          # one line per outbox: "<path>|<size> <hash>" (supervisor watcher's shape)
  SIB_FP=""; [ -f "$sibs" ] || return 0
  while IFS=$'\t' read -r id path state name; do
    [ -f "$path" ] || continue
    ...wc -c / md5sum per file, status checked exactly as read_fp does...
    SIB_FP="$SIB_FP$path|$size ${hash%% *}"$'\n'
  done < "$sibs"
}
# fire:  "SIBLING <id> WROTE — read its outbox from the last entry you saw, act if it asks you something."
# never: on a path that appears for the FIRST time (a new sibling's outbox) — record it as baseline.
# never: while "$orch/.paused" exists — the owner put you to sleep; sibling traffic waits for them.
```

- **There is no self-write logic in this half, and none is needed.** A solo's own outbox is never listed in its own `.siblings`, because the file is derived from the *other* members.
- A failed read counts as unknown, never as a change, and after 12 consecutive failures the half reports `WATCHER BLIND`, as `read_fp` already does.
- The supervisor watcher's shape (`kit/skills/supervisor/reference/watcher.md`, `foreign_change`) is the model for the per-file lines. The copy lives in the solo reference file because `kit/self-write-suppression-check.sh` states that it transcribes the role-command logic.

### 5.4 The print and stream runners

These are bridge-driven solos, the ones where `Runner_Support.Supports` holds.

- `TurnSources_Resolver.Resolve` today returns only `Resolve_Own` for every role except the supervisor. For a Solo whose orchestration has open siblings, it also adds one `TurnSource_Factory.Create_Sibling(orchId, outboxPath)` per sibling. `Create_Sibling` is a new source kind.
- `PrintTurn_Trigger.Is_Inbound(Solo, author)` is `author == Owner` today. It becomes source-aware: **on a Sibling source, an entry authored `Solo` is inbound**, because the reader never writes that file. On the Owner source nothing changes, so a solo's own entries still never start its own turn ("a turn started by the record of the previous turn would be a loop with one member in it").
- A paused sibling's Sibling sources are not dispatched. This mirrors the watcher's `.paused` rule and joins the waker list in the PAUSE bullet of CLAUDE.md.
- The fresh-start pack (`Running/StatePack/StatePackInputs_Reader`) gets the digest appended, so a `Fresh` resume starts with the same knowledge a terminal solo gets at boot.

### 5.5 No app nudges for sibling traffic

`Nudge_IdleImplementers_Async` walks solos through `MemberChannel_Locator`, which maps a solo to `owner-channel.md`. It stays owner-channel-only. An unanswered sibling entry is not the owner waiting. The watcher or the dispatcher already wakes the recipient, and a nudge would be one more app voice in a conversation where the app has nothing to say. That is the decision 15 test applied to agents.

---

## 6. Git and worktree discipline

- **Before asking, the parent creates the child's worktree and branch.** Git writes stay in sessions (the `aiorch:subagents` rule "keeps git writes in the main session"), and the app never runs `git worktree add`. The layout is the repo's own convention if it has one, otherwise `git worktree add ../<repo>.worktrees/<orch-slug>-<job> -b <branch> <base>` (`supervisor/SKILL.md` "Worktree management"). The base is whatever the parent names in the handover, normally the default branch.
- **The app spawns the child in its worktree.** `SpawnCommand_Builder.Build_ForSolo` takes `repoPath` as the `wt -d` directory, and today every solo gets `session.RepoPath` with "no worktree assignment". `Respawn_Implementer`, the one path every spawn and respawn goes through (watchdog, `/model`, `/effort`, app restart), passes `session.WorkingPath ?? session.RepoPath`. The app therefore **knows** each sibling's tree. That is a first: `IOrchestrationMember` has no worktree, and the only worktree knowledge today is the display-only `WORKTREE:` scan of supervisor entries in the WPF `SessionRows_Builder`.
- **Disjoint files are declared, not hoped for.** The HANDOVER entry lists the files the child owns, the files the parent keeps, and the ambient files nobody owns (`.csproj`, DI registrations, shared constants, `CLAUDE.md`). Before touching an ambient file a sibling appends `CLAIM <path>` to its outbox and waits for the other's next boundary, which may be one wake. After committing it appends `RELEASE <path>`. This is the siblings' version of the fan-out rule "Git and ambient files … stay yours" (decision 16), across sessions this time instead of across sub-agents.
- **Staging rules are unchanged.** Explicit paths only, never `git add -A` / `.` / `commit -a`, and `git commit -F <tempfile>` (`solo/SKILL.md` "Git").
- **Merging is the owner's, per topic.** Each sibling reports `branch <x> ready to merge` in **its own** topic, and `/merge` in that topic asks that sibling to land its own branch (`Ask_SessionToMerge_Async`). If a second merge conflicts with the first, the sibling that owns the conflicting branch rebases it in its own worktree. It never touches the other's branch, and it tells its sibling through the outbox.
- **Handing a finished job back.** When a sibling's ledger is all `[x]`/`[-]`, it:
  1. appends a `HANDBACK` entry to its outbox: branch, commits, test counts, what the others must know, and leftovers it parked;
  2. tells the owner in its topic that the job is done (the existing "CLOSE IT WHEN THE JOB IS DONE" rule);
  3. offers to close, with `close-orchestration`, which the owner taps.

  At its next boundary the parent updates its own OWNER REQUESTS row for the split (`split to limits — done, branch x`). The parent does **not** copy the child's lines into its ledger: the endeavour bar already counts them (§3.5).

---

## 7. Lifecycle

### 7.1 Respawn and resume (decision 8)

Each sibling is `solo-1` of its own orchestration, so the rules apply unchanged:
- The terminal runner resumes its own conversation from `<orch>/solo-1/.usage.json` (`ResumableSession_Resolver`, `Resumes_ItsOwnConversation`).
- Print and stream resume from `print-session.json`.
- It is never `--continue`.

**One new risk, to verify live:** the child is spawned in its worktree, not the repo root. Claude Code stores transcripts per working directory, and the resolver only checks that `transcript_path` exists. Resume must therefore always happen **from the same directory**. `WorkingPath` is stored and used on every spawn, which gives exactly that. The implementation plan includes a live check: kill the child, let the watchdog respawn it, and confirm the log says `resuming conversation` and not `fresh`.

### 7.2 Pause

`/pause` is per topic, so pausing one sibling leaves the others running. `.siblings` carries `paused`, so the live siblings know the paused one will not answer, and their prose says to route around it (§8.2). The paused sibling's watcher and dispatcher ignore sibling traffic (§5.3, §5.4). When the owner writes in its topic, the pause lifts (`Wake_PausedTopic_IfNeeded`) and the sibling reads its outboxes at that boundary as usual.

### 7.3 Usage-limit pause

The dispatch pause is **global** (`_dispatchPausedUntilUtc`, `Update_DispatchPause_Async`, `DispatchPause_Gate`). It stops the watchdog for every orchestration and holds every spawning request, so all siblings behave identically and `spawn-sibling` waits on disk (§4.2). `/resume` already appends `GO AHEAD — resume` to every open, unpaused orchestration's owner channel (`Resume_AllSessions_Async`), which reaches every sibling with no change.

### 7.4 App restart

Nothing is new here. The first watchdog pass respawns every open sibling. `.siblings` and `ENDEAVOUR.md` are re-derived on the first tick, and a stale one left by a crash is removed there, as `MeetingFlag_Marker.Sync` does. On exit, `Kill_AllSessions` kills them with everything else.

### 7.5 One sibling closes; the last sibling closes

- `Execute_Close` runs exactly as today (store `ClosedUtc`, kill, topic delete). A new post-step appends an `[agent]` entry to each surviving sibling, naming the closed one's unfinished lines, and the next tick drops it from `.siblings`.
- Closing a sibling that has open lines is **not refused**. The owner may close anything. The closing sibling's prose asks it to write `HANDBACK` first.
- When the last sibling closes, nothing extra happens. The endeavour group disappears from General on its own, because only open orchestrations render (§2.4).

### 7.6 Promotion and `/switch` of a linked orchestration: refused in v1

The supervisor role has no sibling protocol, and a crew beside a solo would need one. `Process_PromoteOrchestrationRequests` gains a refusal, `linked-orchestration`, sent to the solo. The `/switch` handler (around line 7243) answers the owner: *"this topic is linked to siblings — close them or keep one session."* v1 does not unlink an orchestration from its endeavour.

---

## 8. Skill and prose changes

### 8.1 `kit/skills/solo/SKILL.md`

A new section goes after "When a basic orchestration outgrows itself", titled **"When the work splits in two — asking for a SIBLING"**. It covers:
- **The test**, with three routes to keep distinct. Width → fan out. Needs an independent review or a crew → promote. **Two jobs the owner wants to steer separately, each big enough to run for hours** → sibling.
- **Only on the owner's word.** Either the owner asked, or you asked them with a `QUESTION:` and they said yes. Never on your own judgement. It is a spend increase, like promotion.
- **The recipe:**
  1. worktree and branch;
  2. a `HANDOVER` entry in *your outbox*, with the job, the file split, the ambient files, the traps and the base, and a note of the index the helper prints;
  3. the request;
  4. one line to the owner;
  5. back to work.
  Do not re-drop the request.
- A pointer to `reference/siblings.md`, resolved through `$REF` the way `print-runner.md` is ("a bare `reference/...` is NOT a path your tools can open").

The boot sequence gets one step: *"If `$ORCH/.siblings` exists you are part of an endeavour: read `reference/siblings.md`, then `ENDEAVOUR.md`, then — if your first `FROM app` entry names one — your brief (the HANDOVER entry it names)."*

### 8.2 New `kit/skills/solo/reference/siblings.md`

- **Paths.** `your outbox = $ORCH/sibling-outbox.md` (you write it); theirs are listed in `$ORCH/.siblings` (you read them). Append with `channel-append.sh --channel <your outbox>`. Never write another sibling's outbox or PLAN.md.
- **Shape.** Same limits as the owner's phone: at most 600 characters and 3-5 lines. Subjects start with what the entry is: `ASK`, `FYI`, `CLAIM <path>`, `RELEASE <path>`, `RELAY owner#<n>`, `HANDOVER`, `HANDBACK`.
- **Never answer an FYI, never acknowledge an acknowledgement.** An `ASK` gets one answer. This is the only loop the structure cannot prevent (§3.3).
- **Boundaries.** Read the outboxes, then `ENDEAVOUR.md`, at every boundary, after your own channel.
- **Owner questions about the other job.** Answer from `ENDEAVOUR.md`, say it is the sibling's job, and never act on it.
- **The relay rule.** An owner instruction for the other job is relayed verbatim as `RELAY owner#<n>`, naming your channel entry. The receiver writes it as an OWNER REQUESTS row marked `via <topic> #n`, so decision 22's trace survives the hop. The sender tells the owner in one line that it passed it on.
- **Blocked on a sibling.** Mark the line `- [!] … (waiting on sibling <name>)`. That is a machine block: `run-to-the-end-check.sh` only counts `[ ]` and `[>]` as open, and `[?]` would wrongly put it on the owner.
- **Paused siblings do not answer.** Route around them, or ask the owner in your own topic.
- **SCOPE (decision 22).** Your ledger traces to requests in **your** topic, or to `RELAY` rows. A finding about the other's job goes to them as an `FYI`, not into your PARKED section.
- **HANDBACK** before closing.

### 8.3 `kit/skills/solo/reference/watcher.md`

The owner-channel loop is kept verbatim, and the sibling half is added (§5.3). The file already says why a failed read is not a change. The new half inherits that rule and says so.

### 8.4 `kit/skills/general-supervisor/SKILL.md`

One paragraph: siblings exist, General groups them under 🔗, and the general supervisor never starts one. It only tells the owner to ask the solo in the topic.

### 8.5 One question at a time across siblings

**The rule is per topic, and a question held in topic A does not hold topic B** (owner decision **O4**; this is the recommendation). The reasons:
- The owner's complaint was ten questions **in one thread**, "without the session waiting for my answers to each question before sending the next". Each sibling still asks one question at a time in its own thread, and `.awaiting-answer` / `QuestionHold_Policy` are already per orchestration.
- The owner split the work **so that** the two jobs do not wait on each other. A cross-topic hold would make limits wait for the owner's answer about settings, which recreates the single-thread bottleneck.
- The one real overlap is two questions about **the same decision**, and the prose covers it: a question that affects both jobs is asked **once**, by the sibling whose job it blocks, and the other hears the answer through the digest.

---

## 9. Owner decisions

These four are genuinely the owner's. Everything else is decided above, with its reasons.

| # | Question | Recommendation |
|---|---|---|
| **O1** | Does a sibling's birth need your tap? | **Yes.** It is a second session running indefinitely, the same kind of spend as a promotion, which you already confirm. The tap is also where you see the new topic's name and job before the topic exists. It costs one tap and lapses after 12 hours. |
| **O2** | Maximum open siblings per endeavour | **3**, as a settings-catalogue row `endeavour.maxOpenSiblings` so it is data, not code (the "effort is data too" lesson). Each extra sibling adds a topic, a session, and another block in every sibling's digest. |
| **O3** | Should sibling-to-sibling traffic ever reach your phone? | **Never pushed.** On demand with `/endeavour`. Pushing it would put two sessions' coordination into the topics the split was meant to keep clean. |
| **O4** | Does a question pending in one sibling's topic hold the other's? | **No, holds are per topic.** A question that affects both jobs is asked once, by the sibling it blocks. |

---

## 10. Risks

| Risk | Mitigation |
|---|---|
| `--resume` from a worktree cwd does not find the transcript | `WorkingPath` is used on every spawn, so the cwd is stable. A live check is in the plan (§7.1). If it fails, spawn at `RepoPath` and have the sibling `cd` into the worktree at boot, the implementer precedent. |
| Siblings both touch an ambient file and the merges conflict | `CLAIM`/`RELEASE` entries, the file split declared in HANDOVER, per-branch merges on the owner's word, and the conflicting sibling rebases its own branch (§6). |
| Two sessions at xhigh double the spend | The owner's tap (O1), the cap (O2), and `/cost` per topic, which already exists. |
| Wake storms between siblings | They cannot happen structurally (§3.3). The ack rule covers the rest, and per-file fingerprints keep one outbox's traffic from being excused by another's. |
| The digest grows with a long ledger | Hard caps in `EndeavourDigest_Builder` (15 lines, 6 entries, 400 characters), pinned by tests. |
| The owner cannot tell which topic is which | The platform code plus a distinct name is enforced (`name-taken`), there is a birth note in each topic, and the colours match by repo. |

**Parked (decision 22) and not part of this work.** Found while reading and listed so it is not lost:
- `solo/SKILL.md` frontmatter registers only `soft-boundary-check.sh` under PreToolUse. It does not register `supervisor-awaiting-answer-check.sh`, even though the prose tells the solo that "A PreToolUse hook denies your tool calls while an answer is outstanding (it covers solo sessions as of 2026-09-09)". The hold still works, because the mirror holds the channel. The tool-call half does not reach any solo, siblings included.
- `SessionWatchdogModel.Clear_AwaitingAnswer_ForDeadSession` runs for a dead **supervisor** only, so a respawned solo keeps a stale `.awaiting-answer` until the 10-minute cap.
- The terminal spawn script (`SpawnCommand_Builder.Build_SessionScript`) exports `AIORCH_ROLE`, `AIORCH_ID` and `AIORCH_MEMBER` but not `AIORCH_SUPERVISION_ROOT`, so terminal sessions and hooks rely on the `$HOME` default.
- The `[sup → imp-2]` merged-spoke tagging described in CLAUDE.md decision 4 does not exist in `MirrorText_Formatter`. Spokes mirror only their `online` line.

---

## 11. Test strategy

The suite is `AIOrchestratorCoreLib.Tests`, using the existing fakes and harnesses:
- `TempTree`, `SupervisionPaths_Factory.Create(temp)`;
- `RecordingSpawner_Fake` / `LaunchWitness_Fake`;
- `TappableTelegram_Fake` / `RecordingTelegram_Fake`;
- `SteppingClock_Fake`;
- `ChannelAppendTool`, which runs the real `channel-append.sh`.

**Request reader** (`GeneralSupervision/OrchestrationRequestsReaderTests` style, like `PromoteOrchestrationRequestTests`):
- a well-formed `spawn-sibling` is read;
- each missing field is rejected with its reason;
- a non-integer `handover` is rejected;
- `name` without ` · ` is rejected;
- `job` over 200 characters is rejected;
- `spawn-sibling` appears in the unknown-action `known` list.

**Executor** (engine-level, same shape as `CloseTapArchiveProbeTests` and `PromoteToFullCrewTests`):
- Each refusal in §4.2 produces an `[agent]` entry (not texted), the archive label, and no owner prompt.
- A held request produces one prompt in the parent's topic, with the right buttons.
- Tap yes:
  - one new orchestration, with `endeavourId`, `bornFrom*`, `workingPath`, `DisplayName` and the copied overrides all set **before** the spawn;
  - birth note, then `FROM owner` job, both written **after** registration;
  - `Raise_OwnerWait(child)`;
  - an Owner-audience entry to the parent;
  - archived as `started`.
- Tap no: declined wording, and nothing spawned.
- Expiry lapses the request.
- Re-validation at tap time refuses a request whose worktree has since become shared.
- A second request citing the same handover gets `handover-already-used`, both while parked and after the birth.
- Under `dispatchPaused` the file stays on disk, and it runs after the lift.
- The cap is enforced.
- A Sibling-kind file never reaches `Execute_Close`: a mutation that deletes the branch must redden (memory: "verify a mutation applied").

**Store and serializer:**
- The four fields round-trip.
- An absent field loads as null, including a pre-change session.json fixture.
- Derived membership excludes self, includes only the same `endeavourId`, and follows closes.

**Launcher:**
- `Start_SiblingOrchestration` stamps the fields before `Add_Member`.
- `Respawn_Implementer` uses `WorkingPath` on the first spawn, on a watchdog respawn, and on a `/model` respawn.
- The resume-id path is unchanged.

**Derived files:**
- `.siblings` / `ENDEAVOUR.md` are written on the tick, rewritten only on a text change, removed when unlinked or closed, and removed at startup when stale.
- `ENDEAVOUR.md` respects every cap.
- It skips `[agent]` entries.
- It reads history across compaction.

**Progress:**
- `EndeavourProgress_Reader` sums correctly.
- It includes a closed sibling while any member is open.
- PARKED and OWNER REQUESTS lines are excluded, via the parser.
- **Golden:** `Build_ProgressReportText(null)` is byte-identical for a machine with no endeavours.

**Print runner:**
- `TurnSources_Resolver` adds Sibling sources for a linked solo only.
- `PrintTurn_Trigger`: `Solo` is inbound on a Sibling source and not on the Owner source.
- A paused sibling's sources are not dispatched. Extend `PauseGatesEveryWakerScanTests`.

**Lifecycle:**
- A close appends to each survivor and drops the closed one from `.siblings`.
- Promote and `/switch` are refused for a linked orchestration.
- Every open outbox is compacted over 90 entries.

**Bash harnesses** (they refuse to run if they cannot find the thing they test, per decision 20):
- Extend `kit/watcher-behaviour-check.sh` and `kit/self-write-suppression-check.sh`:
  - A's outbox append fires B and not A;
  - A's owner-channel self-write is still suppressed;
  - a sibling added to `.siblings` mid-loop is baselined and does not fire;
  - `.paused` silences the sibling half and not the owner half;
  - a failed read of an outbox does not fire.

**Skill prose** (`AIOrchestratorCoreLib.Tests/Kit/`):
- `NoProtocolFileIsOrphanedTests` must see `reference/siblings.md` referenced from `SKILL.md`.
- A new `SoloIsToldAboutSiblingsTests`, in the `SoloIsToldToFanOutTests` style, pins:
  - the three-way test (fan-out / promote / sibling);
  - "only on the owner's word";
  - the relay rule;
  - the `- [!]` marker for a sibling block;
  - "never acknowledge an acknowledgement".
- `KitProseCarriesTheOwnersRulesTests` keeps passing.

---

## 12. Task breakdown for the implementation plan

The dependencies are marked. Tasks with disjoint file sets can run as parallel units (decision 16).

1. **Session model.** Four fields, `SessionJson_Serializer`, store setters, derived-membership helper, tests. *(first)*
2. **Paths.** `Get_SiblingOutboxFile`, `Get_SiblingsListFile`, `Get_EndeavourDigestFile` on `ISupervisionPaths`. *(first; disjoint from 1)*
3. **Request reader.** `SPAWN_SIBLING_ACTION`, `ISpawnSiblingRequest`, `IPendingRequests` plumbing, `known` list, tests. *(first; disjoint from 1 and 2)*
4. **Launcher.** `Start_SiblingOrchestration`, `WorkingPath` in `Respawn_Implementer` / `Build_ForSolo`, tests. *(after 1)*
5. **Executor and confirmation.** `Process_SpawnSiblingRequests` in the spawning group, validation table, `ParkedCloseKinds.Sibling`, prompt wording and buttons, moot rule, `Execute_ConfirmedSibling`, decline and lapse, tests. *(after 1, 3, 4)*
6. **Derived files.** `EndeavourMarkers_Sync` (`.siblings`), `EndeavourDigest_Builder` (`ENDEAVOUR.md`), the tick call, outbox compaction sweep, tests. *(after 1, 2)*
7. **Progress and Telegram.** `EndeavourProgress_Reader`, grouping in `Build_ProgressReportText`, `/endeavour` in `BotCommandMenu` plus its handler, golden test. *(after 1)*
8. **Print runner.** `TurnSource` Sibling kind, source-aware `PrintTurn_Trigger`, pause gate, state-pack digest, tests. *(after 1, 2, 6)*
9. **Lifecycle.** Post-close survivor notice, promote and `/switch` refusal for linked orchestrations, tests. *(after 1, 5)*
10. **Kit.** `solo/SKILL.md` section and boot step, `reference/siblings.md`, `reference/watcher.md` sibling half, general-supervisor paragraph, bash harness extensions, prose tests. *(independent of the C# work; lands with 5)*
11. **Live verification on the owner's machine.** Birth, both topics, the outbox round trip, the resume-from-worktree check (§7.1), pause of one sibling, close of one, the General grouping. Always against a **restarted** app whose running path has been checked (decisions 17, 23).
12. **CLAUDE.md decision 27** (sibling solos: the outbox model, derived membership, summed bar), on the owner's instruction.
