# Implementer parallel fan-out — design

**Date:** 2026-08-11
**Status:** approved
**Scope:** `kit/commands/*.md` only. No C# change, no UI change, no test change.

## Problem

Implementers work strictly sequentially, and nothing in the kit tells them otherwise.

- `implementer.md` never mentions subagents except to forbid them at boot: *"do NOT read the
  repo's `CLAUDE.md`/docs, run exploration commands, or spawn agents at boot."* Read alongside
  the rest of the kit, that lands as a blanket ban.
- `reviewer.md:65` is the only role told to fan out — *"You are read-only, so parallel agents are
  safe here in a way they are not for an implementer."* The asymmetry was deliberate, but only the
  reviewer half was ever written down.
- `supervisor.md:307` — *"NEVER use a sub-agent (the Task tool) for long work"* — is correct for
  the supervisor and wrong as a kit-wide norm, and it reads like one.
- Briefs are prose. The supervisor has no vocabulary for *"these three units touch disjoint files,
  do them at once"*, so even a perfectly parallelisable task arrives as a sequential instruction.

Consequence: the only parallelism available today is spawning another implementer **session** —
a terminal, a channel, a watchdog entry, Telegram noise, and a `reason` relayed to the owner's
phone — even when the work is one deliverable that merely spans several files.

## Non-goals

- Worktree-isolated parallel writers (`isolation: "worktree"`). Considered and rejected: expensive
  per agent, and merging N worktrees is real work the implementer would have to do carefully.
- Any app-side change — no fan-out state chip, no new channel marker for the parser to read.
- Any change to PLAN.md ledger shape.

## Decisions

| # | Decision |
|---|---|
| D1 | Implementers may fan out. Read-only agents freely; **writing** agents only on **disjoint file sets**. |
| D2 | The supervisor **proposes** a split in the brief; the implementer **decides** and may collapse it, with reasons. |
| D3 | Visibility reuses the existing `WRITING WINDOW OPEN/CLOSED` marker plus agent counts in the boundary report. No new protocol. |
| D4 | A new implementer **session** is for a separately reviewable deliverable. Splitting one deliverable is **fan-out**, not a session. |

## 1. The implementer's fan-out contract (`implementer.md`)

Boot line amended so it scopes to boot only: *"…or spawn agents **at boot** — once you have a
task, see 'Fan out' below."*

New section, **Fan out — parallel agents are yours to use**:

**The asymmetry, stated.** The supervisor's "never use a sub-agent" rule exists because a subagent
blocks its turn, and its turn is the owner's phone line. The implementer's turn is *meant* to be
blocked — it is the one doing the work. That rule is the supervisor's, not the implementer's.

**Read-only fan-out is the default, not the exception.** Exploring an unfamiliar subsystem, hunting
call sites, reading docs, running independent suites or builds, gathering evidence for a report —
dispatch these in parallel as a matter of course. Give each agent a *different* lens or target;
N identical agents find one thing N times.

**Parallel writers, only on disjoint file sets.** Each writing agent's prompt names exactly the
files it may edit. If two units want the same file, they are one unit — do it sequentially,
yourself. Three corollaries:

- **Shared/ambient files are never disjoint.** A `.csproj`, a DI registration, a shared constants
  file, the ledger, the channel. Those edits belong to the implementer, made after the agents
  return.
- **No subagent ever runs git** — no `add`, no `commit`, no branch operations. Staging stays the
  implementer's, by explicit path. A subagent running `git add -A` is the existing shared-machine
  hazard multiplied by N.
- **`WRITING WINDOW OPEN` before dispatching writers**, naming every file across every unit;
  `CLOSED` only after verification. Without it the supervisor may audit half-written state — a
  failure this kit has already paid for once.

**A subagent's report is not evidence.** The implementer's own protocol says claims without
evidence are worthless; that applies one level down. Before reporting: read the actual diff, run
the suite, count the tests. Never forward a subagent's summary as your own result.

**Self-limiting, no magic number.** Read-only agents: as many as the work has distinct angles.
Writers: few — every line they produce must be personally verified, and that verification is
serial. The verification cost is the real cap, and it is better than a number because it scales
with the work.

**Report planned vs actual agent count** and what each unit produced — the same auditability
`reviewer.md` already requires for its depth tiers.

## 2. The supervisor's side (`supervisor.md`)

### 2.1 Brief for parallelism

New subsection under *Managing implementers*. When independent units are visible, the brief carries
them in a fixed shape:

```
PARALLEL UNITS (proposal — verify before you dispatch):
- unit A: <what> — files: <paths>
- unit B: <what> — files: <paths>
shared/after: <files only you touch, once the units return>
```

Explicitly a **proposal**. The supervisor briefs lean and has not read the code, so its file sets
will sometimes be wrong. The implementer verifies and collapses the split to sequential if the
units overlap, stating why in its report. This is the existing "push back with evidence" culture
applied to decomposition.

### 2.2 Sessions vs fan-out

> A new implementer **session** is for a separately reviewable deliverable — one that needs its own
> review cycle, its own worktree/branch, or its own PLAN.md line. Splitting **one** deliverable
> across files or units is **fan-out inside one implementer**. Before spawning `imp-N`, ask:
> *is this a second deliverable, or the same one going faster?* If it is the same one, it is a
> brief with `PARALLEL UNITS`, not a session.

This also keeps the mandatory `reason` honest: "go faster on the task `imp-1` already has" was
never a good line to relay to the owner's phone.

### 2.3 PLAN.md is unchanged — stated explicitly

A ledger line is one reviewable deliverable. Units inside a line are the implementer's business and
never become their own lines. This is written down because the existing "lines that lump several
tasks together" complaint could otherwise be misread into shattering the ledger whenever a task
parallelises — which would corrupt the progress bar and `/progress`.

## 3. Stale cross-references to fix

- **`supervisor.md:307`** — scope the sub-agent ban to *your own turn*, and note that an implementer
  fanning out is the intended shape, so it stops reading as a kit-wide norm.
- **`reviewer.md:65`** — *"safe here in a way they are not for an implementer"* becomes false the
  moment this ships. Reword to: safe for the reviewer *without the disjoint-file discipline an
  implementer needs*.

## 4. `solo.md`

Solo writes code, and today `solo.md:70` tells it to escalate to a full orchestration when work
needs parallel streams. It gets the same fan-out contract **by reference** — a few lines pointing at
the implementer rules, not a second copy of them. With fan-out available, solo absorbs more work
before escalation is warranted.

## 5. `CLAUDE.md` — locked decision #16

Record the asymmetry in the decisions list so it is not re-litigated or half-reverted later:
implementers fan out, supervisors do not, and writing agents require disjoint file sets.

## 6. Verification

No unit tests: these are prompts, and nothing in C# reads them (`kit/install.ps1` only copies them).

1. Run `kit/install.ps1`; confirm all five role commands land in `~/.claude/commands/`.
2. Exercise once in a real orchestration: a supervisor briefs a task carrying `PARALLEL UNITS`, the
   implementer fans out, and its boundary report shows planned-vs-actual agent counts and a
   `WRITING WINDOW` spanning every unit's files.

## Risks

- **A wrong disjointness call corrupts a file.** Mitigated by making the supervisor's split a
  proposal the implementer must verify, by the ambient-files rule, and by git staying serial and
  implementer-owned.
- **Subagent claims laundered into reports.** The single most likely regression, and the reason
  "a subagent's report is not evidence" is stated as a rule rather than implied.
- **Fan-out used to dodge a needed second session.** Mitigated by the deliverable test in §2.2:
  anything needing its own review cycle or branch is still a session.
