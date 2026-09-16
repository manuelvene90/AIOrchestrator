# One wake model — the app decides when a session wakes, whichever runner drives it

**Date:** 2026-09-15 · **Status:** design, approved in shape by the owner 2026-09-15 · **Owner:** Nathan
**Branch:** `feat/one-wake-model`, worktree `../AIOrchestrator-relations`, based on `ours/integration` at `0bcc47a`.

**Copies read.** Source: this worktree, `ours/integration` at `0bcc47a` (2026-09-15). Installed kit:
`~/.claude/plugins/cache/aiorch-local/aiorch/1.0.0` — **it derives from a checkout that no longer
exists** (`known_marketplaces.json` registers `aiorch-local` at
`/Users/nvene/Visual Studio/AIOrchestrator-integration/kit`, absent), and its skill files are 4.9–14.6 KB
smaller than this branch's. No running app on this machine. No live orchestration data here
(`~/.claude/supervision/` holds `config.json`, `secrets.json`, `statusline.sh`; `"repos": []`), so every
figure below marked `[measured]` was measured **on the VPS, 6–9 Sep 2026** and is quoted from
`2026-09-08-token-efficiency-design.md` and from `WakeUp_Policy`'s own docstring — **not re-derived
here.** Step 0 exists to close that gap.

**Upstream check, 2026-09-15.** `upstream` (`manuelvene90/AIOrchestrator`, the co-owner's repo) is
**57 commits behind this branch and 0 ahead**; 36 of those 57 touch `Running/`, `Bridge/` or
`kit/skills/`. The three files this design turns on — `RoleRunnerConfig_Factory.cs`, `watcher.md`,
`WakeUp_Policy.cs` — are **byte-identical** between the two. So the defect described here is present
on both machines, and no newer fix for it exists anywhere reachable. Unpushed local work on the
Windows machine would be invisible to this check and is the one thing that could invalidate it.

---

## 1. The defect

**The question "does this session wake now, and with what?" has two answers in this system, in two
languages, and they have diverged.**

- The **bridge** runner (`runners.<role>.runner = print | stream`) answers it in C#:
  `PrintTurn_Trigger.Is_Inbound` decides what counts as inbound, `WakeUp_Policy.Resolve_WakeReason_OrNull`
  decides whether a pending set is worth a turn, `ITurnCursor` records what has been delivered, and
  `StatePack_Builder` assembles what the turn is handed.
- The **terminal** runner answers it in bash: `kit/skills/<role>/reference/watcher.md` fingerprints
  every channel every 5 seconds, compares against its own previous fingerprint, and fires on any
  change it cannot prove is its own. It has no cursor, no digest, no notion of an entry that should
  not wake anybody, and nothing to hand the turn.

**Terminal is the default.** `RoleRunnerConfig_Factory.Create_Default` returns
`SessionRunners.Terminal` for every role, and neither shipped preset (`kit/presets/classic.json`,
`quiet.json`) names a runner — so a machine that states nothing gets the bash answer.

### What each side actually has

| capability | bridge | terminal |
|---|---|---|
| member traffic digested into one turn | ✅ `WakeUp_Policy` + `MemberDigestWindow` (5 min) | ❌ |
| app bookkeeping does not start a turn | ✅ `Is_Inbound` excludes `ChannelAuthors.App`; notes ride via `Select_AgentNotes` | ❌ every append fires the watcher |
| state pack handed to the turn | ✅ `StatePack_Builder` → `PrintTurnExecutorModel` | ❌ |
| delivery recorded by identity | ✅ `ITurnCursor` (digest set, compaction-safe) | ❌ a fingerprint string |
| turn timeout | ✅ 30 min / 2 h members | ❌ |
| memory sandbox | ✅ | ❌ |
| money cap per ordinary turn | ❌ (`--max-budget-usd` is print-only **and** used only on the closing turn) | ❌ |

The last row is a CLI limitation, not a defect of this codebase, and this design does not try to fix it.

### Why it matters in money

`[measured, VPS]` A supervisor wake-up is ~1 M input tokens (2.5 calls × ~398 k mean context).
~400 wake-ups in the window, **247 of them from member traffic**, and **15 % of its turns produced
under 600 output tokens** — an acknowledgement bought at a million. `[derived]` That is ~60 M tokens
in the window spent on turns that said nothing. `[measured]` The supervisor is 406 M against 1 260 M
of member work: **32 % coordination overhead**, where the AWS Agentic AI Lens publishes a target of
**≤ 15 %** and states that past ~15 % the benefit of hierarchical planning is erased by its cost.

Every mitigation for this exists **on the bridge side only**. `[judgement]` The machine running the
terminal default therefore pays the unmitigated cost, and no measurement of it exists.

### Why it is an architectural defect and not a backlog item

`PrintTurnDispatcherModel`'s own docstring states the principle this violates:

> HOW a turn reaches the model is NOT this class's business — that is `ITurnExecutor` […] and that is
> the point: **two transports must never grow two answers to "has this turn already run".**

The project applied that to turn *execution* and has not yet applied it to turn *initiation*. CLAUDE.md
decision 12 puts it more bluntly: *"Never add a second copy of a formatter."* The bash watcher is a
second copy of the wake policy.

---

## 2. Principle

**The app owns the wake decision for every session. A runner is a transport for that decision, never
an author of it.**

Corollary: a capability built once in the wake policy reaches both machines. Today each one has to be
built twice, and the second build has never happened.

---

## 3. The design

### 3.1 One decider

Extract the wake decision into a single addressable component, `WakeDecision_Resolver`, that answers,
for any `(role, orchId, memberId)`:

> Given this session's sources, its cursor and the clock — does it wake now, with which entries, which
> app notes riding along, and for what stated reason?

It is not new logic. It is `TurnSources_Resolver` + `PrintTurn_Trigger.Select_Pending` +
`Select_AgentNotes` + `WakeUp_Policy.Resolve_WakeReason_OrNull`, composed behind one interface and
reachable from outside `PrintTurnDispatcherModel`. The dispatcher becomes its first consumer rather
than its owner.

### 3.2 Two deliveries

- **Bridge:** unchanged. The dispatcher asks the decider and opens the turn itself.
- **Terminal:** the app asks the decider on the same tick and, when the answer is "wake", writes the
  **wake ticket** — `<supervision-root>/<orch-id>/<member-id>/.wake` — containing the reason, the
  state-pack path, and a monotonic ticket number.

The watcher script becomes:

```bash
ticket="$sup/$member/.wake"; last=""
while true; do
  sleep 2
  [ -f "$sup/.meeting" ] && continue
  now="$(cat "$ticket" 2>/dev/null)" || continue
  [ "$now" = "$last" ] && continue
  last="$now"
  printf '%s\n' "$now"
done
```

Everything else in `watcher.md` — `read_fp`, `foreign_change`, the per-channel `.self-write.<role>`
records, the blind-alarm strike counter — is **deleted**. `[judgement]` ~90 lines of policy in bash
become ~10 lines of transport, and the failure modes they exist to handle (a self-write mistaken for
foreign traffic, a fingerprint read that fails silently, a channel the watcher has never seen) stop
being possible because nobody is guessing any more.

### 3.3 The cursor problem, and the rename it forces

Terminal sessions today **delete** their `PrintSessionState_Store` file at spawn, deliberately
(`OrchestrationLauncherModel.cs:554`): that file means "the dispatcher should run turns for this
session", and a member with both a window and headless turns would answer one brief twice.

Under this design a terminal session needs the same file for a different reason — it holds the cursor
and the pack path. So the store's single meaning must be split into two fields:

- `DrivesTurns` — the dispatcher runs this session's turns. True for bridge, **false for terminal**.
- the cursor and pack state — present for **both**.

The launcher's delete becomes a `DrivesTurns = false` write. This is the one genuinely invasive change
in the design and it is the reason step 1 is the load-bearing step: after it, the remaining six are
configuration and deletion.

**`PrintSessionState_Store` is deliberately NOT renamed.** `SessionState_Store` would read better once
the type serves both runners, but the rename touches every call site and every test that names it,
for no change in behaviour — churn spread across exactly the files this step is already rewriting,
which is where a mistake would be hardest to see. The name stays print-shaped and the docstring says
why; rename it later, alone, if it ever becomes worth a commit of its own.

### 3.4 What this does not change

The channel files stay exactly as they are: same format, same append helper, same lock, same
compaction. `channel-append.sh` is untouched. A session from before this change keeps working until
it is respawned, because the old watcher still fingerprints channels that are still being written.

---

## 4. The seven changes

Each is shippable alone and leaves the system working if the series stops there.

**Decomposition into plans.** Steps 0–2 are one plan: they share the extraction, the store split and
the kit change, and none of them is verifiable without the others. Steps 3, 4, 5 and 6 are each their
own plan — step 3 depends on 2, step 6 depends on 2 and on C1.2, and steps 4 and 5 depend on nothing
in this series and may be pulled forward if the owner wants the saving sooner.

### Step 0 — Measure the baseline `[blocked on VPS credentials]`

Read-only on the VPS: per-role token totals, wake-up counts and causes, turns-per-deliverable, and
the share of supervisor turns that are acknowledgement-sized. Aggregates only, via python3 — never a
whole `.jsonl`.

**Why first:** every figure in §1 is quoted, not re-derived. Without this the series is judged against
a document instead of against the machine.

**Done when:** the six figures in §1 are reproduced (or corrected) by a script committed under
`tools/`, and the same script runs against the terminal-runner machine when its data is available.

### Step 1 — `WakeDecision_Resolver` and the wake ticket

The extraction of §3.1, the ticket writer of §3.2, the store split of §3.3, and the new watcher in
`kit/skills/*/reference/watcher.md`.

**Risk:** a terminal session that stops waking is a session that silently does nothing. Mitigated by
a **liveness assertion**: if a terminal session has pending entries and no ticket has been written for
longer than `MemberDigestWindow × 2`, the app logs it and raises the existing stall path. The old
watcher is not deleted from the kit until step 2 has run for two days on both machines.

**Done when:** a terminal supervisor and a terminal implementer each take a turn driven only by a
ticket; `watcher-behaviour-check.sh` is rewritten against the new script and passes; the suite is green.

### Step 2 — Digest, riding notes and the state pack reach the terminal

Falls out of step 1: the decider already holds all three. The work is wiring the ticket to point at a
pack written by `StatePack_Builder`, and adding the pack path to the role commands' boot sequence.

**This is the step that repairs the Windows machine.** `[estimate]` The three capabilities it delivers
are worth, on the measured bridge figures, a ~40 % cut in supervisor wake-ups and the removal of every
app-bookkeeping wake.

**Done when:** on a terminal orchestration, zero turns are started by a `FROM app` entry; member
reports arriving within the digest window ride one turn; the pack appears in the turn's prompt.

### Step 3 — Bookkeeping out of the channels (spec C3)

`turn_ended`, `STATUS`, ledger advisories, orphan and respawn notes move to `<member>/status.jsonl`.
`[measured]` ~1 966 such entries in the VPS window, **zero turns triggered by them alone** — but every
session reads them at boot.

Safe only after step 2, because until then the terminal watcher's only knowledge of anything is the
channel file.

**Done when:** a fresh session's boot read contains no app bookkeeping; the owner-facing subset
(decision 15) still reaches Telegram unchanged.

### Step 4 — The routed report

Today a fix round costs four supervisor wake-ups: findings in, fix brief out, fix report in,
re-review brief out. `[derived]` ~4 M tokens to act as a postman.

The supervisor's round-one verdict may carry a **re-review contract** (`REROUTE: rev-1 on FIXED from
<commit>`). When the named implementer files a report matching it, **the app** writes a relay entry
into the reviewer's channel — author `app`, marked inbound for that member — carrying the delta and
the open findings. The supervisor is woken once, at the re-verdict.

**Members still never write into each other's channels.** Hub-and-spoke (CLAUDE.md decision 4) is
intact; the app is the hub's clerk, not a new edge. `Is_Inbound` gains one case: an app entry
explicitly tagged as a routed report is inbound for a member. Nothing else about `ChannelAuthors.App`
changes.

**Constraint from the reviewer's own skill:** a re-review reviews the delta and the earlier findings,
never the branch again. The relay entry carries exactly those two things and nothing else.

**Done when:** a full fix round completes with one supervisor wake-up instead of four, and the
reviewer's report still reaches the supervisor before anything is accepted.

### Step 5 — The supervisor's skill diet (spec C5, reopened)

C5 was parked on the finding that the skill is ~8 k of a member's 34–47 k boot. That is true of a
member. It is not true of the supervisor, and the spec's own reopen condition is *"the skill above
40 % of the supervisor's boot"*.

`[measured here, 2026-09-15]` `kit/skills/supervisor/SKILL.md` is **97 689 bytes**. At the spec's own
measured ratio of 2.8–3.2 bytes/token that is **30.5–34.9 k tokens**, against a measured supervisor
boot of **55.9–56.9 k**: **54–62 %**. The supervisor−implementer skill delta was 62 KB when measured
and is **67 KB** now.

The condition asks for one measured empty `-p` turn, with and without the skill, to settle it. That is
one command and it has not been run.

**Done when:** the empty-turn measurement is recorded; if it confirms ≥ 40 %, the skill is reduced
with every HARD RULE and every dated incident preserved, and the supervisor's boot is re-measured.

### Step 6 — The supervisor goes fresh (spec C1 step 5)

`[measured]` 91 % of the supervisor's per-call context is carried from earlier turns. Removing the
transcript is the largest single saving in the system and the only one that can lose something: the
pack carries facts, not conclusions — *"we already tried this and it failed"* lives only in the
conversation.

**Last, and gated on the pack.** Preconditions: step 2 shipped and the pack proven on both runners for
at least three days; the `STATE:` block (spec C1.2 — ≤ 2 KB, stripped from the channel entry, stored as
`<member>/state.md`) shipped, so conclusions are transcribed rather than lost.

**Done when:** supervisor mean context < 80 k; ten verdicts read blind by the owner against ten from
the transcript era show no quality regression.

---

## 5. Compatibility

The VPS runs 24/7 on bridge; the Windows machine runs the app on terminal. Both must keep working
through every step.

- **Additive first, deleting last.** The ticket file is new; the old watcher ignores it and keeps
  working. The old watcher is removed from the kit only after step 2 has run two days on both machines.
- **No channel-format change anywhere in the series.** A session of any era can read any channel.
- **Config-gated.** Steps 1–2 ship behind `runners.<role>.wake = ticket | watcher`, defaulting to
  `watcher` until the gate in step 1's acceptance passes, then flipped in one commit.
- **The kit gate still applies.** `KitAssets_Bootstrapper` refuses spawning on a plugin mismatch, so
  the new watcher and the app that writes tickets must ship together. **And the marketplace source is
  currently a deleted directory** — re-registering `aiorch-local` against a real checkout is a
  precondition of step 1, not an afterthought, or no corrected skill can reach any session.

---

## 6. Non-goals

- Unifying the two transports into one. The terminal window is a first-class owner surface
  (CLAUDE.md, *"Dual interaction"*) and stays.
- Any change to the channel file format, the append helper, or its lock.
- Any change to Telegram, to the owner-facing delivery modes, or to the review independence
  guarantee. Independence is the product: a separate session, read-only tools, no stake. None of the
  three costs a round-trip, and none of them is touched.
- A money cap per ordinary turn. `--max-budget-usd` is print-only and the CLI offers nothing else.
- Retrofitting the terminal runner with a turn timeout. The app does not start that turn and cannot
  end it; the existing stall detection is the honest substitute.

---

## 7. Open questions

1. **VPS credentials** for step 0 — user and key for `159.195.254.120`. Read-only access is authorised;
   the connection details are not known.
2. **The Windows machine's `config.json`** — whether it names a runner at all. Assumed terminal
   (the default, and no preset names one), never verified. It changes the size of step 2's payoff,
   not its shape.
3. **Unpushed work on the Windows machine** — invisible to the upstream comparison in the header. If
   it exists it must be seen before step 1.
