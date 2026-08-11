# Implementer Parallel Fan-Out Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Let implementers dispatch parallel sub-agents, and give supervisors the vocabulary to brief work that can go wide.

**Architecture:** Primarily a prompt/documentation change. Five role commands in `kit/commands/`
define agent behaviour. Two things install them: the orchestrator app itself, which globs the kit
shipped in its output folder and refreshes `~/.claude/commands` at every startup — this is the real
delivery path, so a repo edit reaches nothing until `dotnet build` — and `kit/install.ps1`, the
bootstrap path for a machine that has not built yet. The change adds a fan-out contract to
`implementer.md`, a `PARALLEL UNITS` brief shape and a session-vs-fan-out test to `supervisor.md`,
repairs two now-false cross-references, and makes the bootstrap installer stop disagreeing with the
app's.

**Tech Stack:** Markdown role commands, PowerShell installer. Tasks 1-5 involve no build and no test
project. (One C# fix was added after the final review — see Global Constraints.)

## Global Constraints

- **Spec:** `docs/superpowers/specs/2026-08-11-implementer-parallel-fanout-design.md`. Read it first.
- **Scope for Tasks 1-5:** `kit/commands/*.md`, `kit/install.ps1`, `CLAUDE.md`. **No C# change, no
  test change, no UI change** — this constraint bound the five tasks and was honoured by all of them.
  It was lifted once, deliberately and with the owner's approval, AFTER the final whole-branch review:
  `KitAssets_Installer.Copy_IfChanged` now copies bytes rather than text, with two covering tests.
  See the spec's "Scope added after approval".
- **Voice:** match the existing kit — bold lead-in phrase, imperative, reasons given. Never invent an
  incident ("this really happened on <date>") that did not occur; only Task 4 may cite dated history,
  and only history already recorded in `CLAUDE.md`.
- **English only**, in every file touched.
- **Git:** stage by explicit path. Never `git add -A`, `git add .`, or `git commit -a`. Every commit
  in this plan has a single-line subject and no body, so plain `git commit -m "<subject>"` is correct
  and safe. The repo's `git commit -F <tempfile>` rule exists for MULTI-line messages, which Windows
  PowerShell mangles when passed via `-m` with a here-string — none of these commits need one.
- **Line width:** wrap prose at ~100 columns, matching the surrounding files.
- **There is no automated test for these files.** Each task's verification is a `grep` assertion that
  the new text landed AND that the text it replaces is gone. Treat a failed grep exactly as a failed
  test: fix before committing.

## File Structure

| File | Responsibility | Task |
|---|---|---|
| `kit/commands/implementer.md` | The fan-out contract — who may fan out, and the write-safety rules | 1 |
| `kit/commands/supervisor.md` | Briefing for parallelism; session-vs-fan-out choice; ledger rule | 2 |
| `kit/commands/reviewer.md` | Repair the now-false claim about implementers | 3 |
| `kit/commands/solo.md` | Same contract by reference, no second copy | 3 |
| `CLAUDE.md` | Locked decision #16 so the asymmetry is not re-litigated | 4 |
| `kit/install.ps1` | Glob the kit folder instead of naming three commands by hand, so this bootstrap path stops disagreeing with the app's own installer | 5 |

---

### Task 1: The implementer's fan-out contract

**Files:**
- Modify: `kit/commands/implementer.md:24-26` (scope the boot ban)
- Modify: `kit/commands/implementer.md` (new section between the Git discipline section ending at
  line 89 and `## \`GO AHEAD — resume\` entries` at line 91)

**Interfaces:**
- Produces: the section title `## Fan out — parallel agents are YOURS to use`, and the marker phrase
  `WRITING WINDOW OPEN`. Tasks 2, 3 and 4 reference both by name — do not reword the title.

- [ ] **Step 1: Scope the boot-time ban so it stops reading as a blanket ban**

Replace this exact text at `kit/commands/implementer.md:24-26`:

```
Boot is NOT the time to study the repo: **do NOT read the repo's `CLAUDE.md`/docs, run exploration
commands, or spawn agents at boot.** Repo study happens when you HAVE a task — then read the
repo's `CLAUDE.md` and its full mandatory reading list BEFORE writing any code.
```

with:

```
Boot is NOT the time to study the repo: **do NOT read the repo's `CLAUDE.md`/docs, run exploration
commands, or spawn agents at boot.** Repo study happens when you HAVE a task — then read the
repo's `CLAUDE.md` and its full mandatory reading list BEFORE writing any code, and fan out to
parallel agents as "Fan out" below describes. **That ban is about BOOT, not about the job.**
```

- [ ] **Step 2: Add the fan-out section**

Insert this immediately after the Git discipline section's last bullet (the one ending
"…and stopping after it is the known failure mode.") and immediately before the line
``## `GO AHEAD — resume` entries``:

````markdown
## Fan out — parallel agents are YOURS to use

**The supervisor's "never use a sub-agent" rule is THEIRS, not yours.** It exists because a
sub-agent runs inside the caller's turn, and the supervisor's turn is the owner's phone line —
blocking it leaves the owner talking to a wall. Your turn is MEANT to be blocked: you are the one
doing the work. Working sequentially when the task has independent parts costs the owner wall-clock
for nothing.

- **Read-only fan-out is your DEFAULT, not an exception.** Exploring an unfamiliar subsystem,
  hunting call sites, reading docs, running independent test suites or builds, gathering the
  evidence your report needs — dispatch these in parallel as a matter of course. Give each agent a
  DIFFERENT lens or target: N identical agents find one thing N times.
- **Parallel WRITERS are allowed only on DISJOINT file sets.** Name in each agent's prompt the exact
  files it may edit ("you may edit exactly these files: …; touch nothing else"). If two units want
  the same file, they are ONE unit — do it yourself, sequentially. Two agents editing one file
  overwrite each other, and the loser's work disappears with no error to tell you.
- **Ambient files are never disjoint.** A `.csproj`, a DI registration, a shared constants file, a
  test list, PLAN.md, this channel — anything the whole task touches is YOURS, edited by you once
  the agents return. Handing an ambient file to a unit is how a "disjoint" split stops being one.
- **No sub-agent EVER runs git** — not `add`, not `commit`, not a branch operation. Staging stays
  yours, by explicit path (see Git discipline above). A sub-agent running `git add -A` is that
  hazard multiplied by the number of agents you dispatched.
- **`WRITING WINDOW OPEN` before you dispatch writers**, naming every file across every unit; close
  it only after you have verified the results. A parallel write batch IS a multi-file write batch,
  so the existing rule already covers it — and without the window your supervisor may audit
  half-written state and report it as defects.
- **A sub-agent's report is NOT evidence.** Your own rule — claims without evidence are worthless —
  applies one level down. Before you report: read the actual diff, run the suite yourself, count the
  tests yourself. Never forward an agent's summary as your result; you did not see what it saw.
- **Your cap is your own verification, not a number.** Read-only agents: as many as the work has
  distinct angles. Writers: few — every line they produce must be personally verified, and your
  verification is serial. When verifying costs more than the parallel writing saved, you fanned out
  too wide.
- **Report planned vs ACTUAL agent count**, and what each unit produced, in your boundary report.
  Depth the owner is paying for must be auditable after the fact.
````

- [ ] **Step 3: Verify the edit landed and the old wording is gone**

Run:

```bash
cd "C:/Users/Gianpiero/source/repos/AIOrchestrator"
grep -c "^## Fan out — parallel agents are YOURS to use" kit/commands/implementer.md
grep -c "That ban is about BOOT, not about the job" kit/commands/implementer.md
grep -c "A sub-agent's report is NOT evidence" kit/commands/implementer.md
grep -n "BEFORE writing any code\.$" kit/commands/implementer.md
```

Expected: `1`, `1`, `1`, and **no output** from the last one (the old sentence ended there; it now
continues with ", and fan out…").

- [ ] **Step 4: Verify the section landed in the right place**

Run:

```bash
grep -n "^## " kit/commands/implementer.md
```

Expected: `## Fan out — parallel agents are YOURS to use` appears **after** `## Git discipline…` and
**before** ``## `GO AHEAD — resume` entries``.

- [ ] **Step 5: Commit**

```bash
git add kit/commands/implementer.md
git commit -m "feat(implementer): fan out to parallel agents"
```

---

### Task 2: The supervisor's side

**Files:**
- Modify: `kit/commands/supervisor.md:307-310` (scope the sub-agent ban to the supervisor's own turn)
- Modify: `kit/commands/supervisor.md` (two new subsections after the "Briefing a new implementer"
  paragraph ending at line 401, before `## CROSS-REVIEW IS MANDATORY` at line 403)
- Modify: `kit/commands/supervisor.md` (one new bullet in the PLAN.md section, after the
  "Re-read it as your fast resume point…" bullet at lines 514-515)

**Interfaces:**
- Consumes: the phrase `Fan out` and the section title from Task 1 — the new text points implementers
  at it.
- Produces: the brief marker `PARALLEL UNITS`, referenced by Task 4's decision entry.

- [ ] **Step 1: Scope the sub-agent ban to the supervisor's own turn**

Replace this exact text at `kit/commands/supervisor.md:307-310`:

```
- **NEVER use a sub-agent (the Task tool) for long work.** A sub-agent runs INSIDE your turn: it
  blocks you for its whole duration, which is precisely the failure this rule exists to prevent.
  An implementer is a separate session — it works while you stay free. Sub-agents are acceptable
  only for something genuinely brief.
```

with:

```
- **NEVER use a sub-agent (the Task tool) for long work.** A sub-agent runs INSIDE your turn: it
  blocks you for its whole duration, which is precisely the failure this rule exists to prevent.
  An implementer is a separate session — it works while you stay free. Sub-agents are acceptable
  only for something genuinely brief. **This rule is about YOUR turn ONLY.** An implementer fanning
  out to parallel agents is the intended shape, not a violation — its turn is supposed to be busy.
  Never relay this ban to a member.
```

- [ ] **Step 2: Add the two new subsections**

Insert immediately after the "Briefing a new implementer" paragraph (it ends "…must CONTINUE to the
remaining numbered items afterwards.") and immediately before `## CROSS-REVIEW IS MANDATORY`:

````markdown
### Brief for parallelism — organise the work so it CAN go wide

An implementer can fan out to parallel agents, but it can only parallelise what you handed it as
parallelisable. When you can see a task's independent units, say so in the brief:

```
PARALLEL UNITS (proposal — verify before you dispatch):
- unit A: <what> — files: <paths>
- unit B: <what> — files: <paths>
shared/after: <files only the implementer touches, once the units return>
```

- **It is a PROPOSAL, and say so in those words.** You brief lean and have not read the code, so your
  file sets will sometimes be wrong. The implementer verifies them, collapses the split to sequential
  when the units actually overlap, and tells you why. Being refuted there is the system working.
- **Two units that share a file are ONE unit.** If you cannot name a disjoint file set, do not invent
  one — write the brief sequentially and let the implementer find the split from the code.
- **No `PARALLEL UNITS` block is perfectly fine.** Most tasks are one unit. An invented split is
  worse than none: it costs the implementer a verification pass just to reject it.

### A second SESSION, or fan-out inside one? — the deliverable test

Before you request `imp-N`, ask: **is this a second deliverable, or the same one going faster?**

- **A separately reviewable deliverable → a new implementer session.** It needs its own review cycle,
  its own worktree/branch, or its own PLAN.md line.
- **One deliverable spanning several files or units → fan-out inside ONE implementer**, briefed with
  `PARALLEL UNITS`. A session costs a terminal, a channel, a watchdog entry, Telegram noise and a
  `reason` on the owner's phone; parallel agents inside an implementer cost none of that.

"Go faster on the task `imp-1` already has" was never a `reason` worth relaying to the owner — and it
is now the wrong mechanism as well.
````

- [ ] **Step 3: Add the ledger-shape bullet**

In the PLAN.md section, immediately after the bullet "Re-read it as your fast resume point after a
respawn — it beats replaying the whole channel narrative.", add:

```
- **One line = one reviewable deliverable — parallel units NEVER become their own lines.** When you
  brief a task with `PARALLEL UNITS`, the ledger still carries ONE line for it; the units are the
  implementer's internal business. Shattering a line into its units inflates the progress bar with
  work the owner never asked to track, and `[>]` on five sub-lines tells them less than `[>]` on the
  deliverable.
```

- [ ] **Step 4: Verify all three edits landed**

Run:

```bash
cd "C:/Users/Gianpiero/source/repos/AIOrchestrator"
grep -c "This rule is about YOUR turn ONLY" kit/commands/supervisor.md
grep -c "^### Brief for parallelism" kit/commands/supervisor.md
grep -c "^### A second SESSION, or fan-out inside one" kit/commands/supervisor.md
grep -c "One line = one reviewable deliverable" kit/commands/supervisor.md
grep -c "PARALLEL UNITS" kit/commands/supervisor.md
```

Expected: `1`, `1`, `1`, `1`, and `4` for the last (once in the fenced template, once in the
proposal bullet, once in the deliverable test, once in the ledger bullet).

- [ ] **Step 5: Verify placement**

Run:

```bash
grep -n "^## \|^### " kit/commands/supervisor.md
```

Expected: both new `###` headings appear **after** `## Managing implementers (via the orchestrator app)`
and **before** `## CROSS-REVIEW IS MANDATORY…`.

- [ ] **Step 6: Commit**

```bash
git add kit/commands/supervisor.md
git commit -m "feat(supervisor): brief for parallelism, and the deliverable test"
```

---

### Task 3: Repair the two stale cross-references

**Files:**
- Modify: `kit/commands/reviewer.md:65-68`
- Modify: `kit/commands/solo.md` (new bullet after the Git bullet at lines 65-66; and reword the
  "When a basic orchestration outgrows itself" paragraph at lines 70-72)

**Interfaces:**
- Consumes: the section title `## Fan out — parallel agents are YOURS to use` from Task 1; `solo.md`
  points at it by name rather than duplicating the rules.

- [ ] **Step 1: Fix the reviewer's now-false claim**

`reviewer.md:65-66` currently says parallel agents are safe for a reviewer "in a way they are not for
an implementer". That becomes false the moment Task 1 ships. Replace this exact text:

```
- **Fan out with subagents / the Workflow tool.** You are read-only, so parallel agents are safe
  here in a way they are not for an implementer. Give each finder a DIFFERENT lens (correctness,
  boundary/edge cases, concurrency, error paths, security, performance, test coverage, docs-vs-code
  truth) — N identical agents find one thing N times.
```

with:

```
- **Fan out with subagents / the Workflow tool.** You are read-only, so parallel agents are safe here
  WITHOUT the disjoint-file discipline an implementer needs — nothing you dispatch can collide. Give
  each finder a DIFFERENT lens (correctness, boundary/edge cases, concurrency, error paths, security,
  performance, test coverage, docs-vs-code truth) — N identical agents find one thing N times.
```

- [ ] **Step 2: Give solo the contract by reference**

In `solo.md`, immediately after the Git bullet (it ends "…via `git commit -F <tempfile>` on Windows
PowerShell."), add:

```
- **Fan out to parallel agents, exactly as an implementer does.** Read-only agents (exploration,
  searches, independent suites) freely; parallel WRITERS only on DISJOINT file sets, with every
  agent's editable files named in its prompt. Git and ambient files (`.csproj`, DI registrations,
  shared constants) stay yours. A sub-agent's report is not evidence — read the diff and run the
  suite yourself before you report. Full rules: the "Fan out" section of `/implementer`.
```

- [ ] **Step 3: Reword the escalation paragraph, since fan-out absorbs one of its three triggers**

Replace this exact text at `solo.md:70-72`:

```
If the work turns out to need parallel streams, a real review, or more coordination than one session
can hold — **say so in one line and let the owner decide.** Do not quietly start behaving like an
orchestration; they chose this mode deliberately, and switching is theirs to choose too.
```

with:

```
Work that merely needs to go WIDE you can now absorb yourself, by fanning out (above). But if it
needs a genuinely independent review, or more coordination than one session can hold — **say so in
one line and let the owner decide.** Do not quietly start behaving like an orchestration; they chose
this mode deliberately, and switching is theirs to choose too.
```

- [ ] **Step 4: Verify both files**

Run:

```bash
cd "C:/Users/Gianpiero/source/repos/AIOrchestrator"
grep -c "in a way they are not for an implementer" kit/commands/reviewer.md
grep -c "WITHOUT the disjoint-file discipline an implementer needs" kit/commands/reviewer.md
grep -c "Fan out to parallel agents, exactly as an implementer does" kit/commands/solo.md
grep -c "need parallel streams, a real review" kit/commands/solo.md
grep -c "needs a genuinely independent review" kit/commands/solo.md
```

Expected: `0`, `1`, `1`, `0`, `1`. The two zeros are the point — the stale claims must be gone, not
merely accompanied by new text.

- [ ] **Step 5: Commit**

```bash
git add kit/commands/reviewer.md kit/commands/solo.md
git commit -m "docs(kit): fan-out for solo, and reviewer's stale implementer claim"
```

---

### Task 4: Record locked decision #16

**Files:**
- Modify: `CLAUDE.md` (append after decision 15, which ends at line 70 with "Apply the same test to
  any new alert."; the next line 72 is `## Resolved Decisions (2026-08-06, owner)`)

**Interfaces:**
- Consumes: `PARALLEL UNITS` (Task 2) and the fan-out rules (Task 1). Names must match exactly.

- [ ] **Step 1: Append the decision**

Insert after decision 15's last line and before the blank line preceding
`## Resolved Decisions (2026-08-06, owner)`:

```
16. **Parallel agents: implementers fan out, supervisors do not, writers need disjoint files.** An
    implementer's turn is MEANT to block — it is the one doing the work — so the supervisor's "never
    use a sub-agent" rule covers the SUPERVISOR's turn only (that turn is the owner's phone line) and
    is never relayed to a member. Read-only fan-out is an implementer's default; parallel WRITERS are
    allowed only on disjoint file sets, with git and ambient files (`.csproj`, DI registrations,
    shared constants, PLAN.md) kept to the implementer itself. The supervisor PROPOSES the split in
    the brief (`PARALLEL UNITS`) and the implementer verifies it — a supervisor briefs lean and has
    not read the code. A sub-agent's report is NOT evidence: the implementer reads the diff and runs
    the suite before reporting. A new implementer SESSION is for a separately reviewable deliverable
    (own review cycle, worktree, or ledger line); one deliverable going faster is fan-out, and ledger
    lines never shatter into units. Spec:
    `docs/superpowers/specs/2026-08-11-implementer-parallel-fanout-design.md`.
```

- [ ] **Step 2: Verify**

Run:

```bash
cd "C:/Users/Gianpiero/source/repos/AIOrchestrator"
grep -n "^16\. \*\*Parallel agents" CLAUDE.md
grep -n "^## Resolved Decisions" CLAUDE.md
```

Expected: decision 16's line number is **lower** than the `## Resolved Decisions` line number.

- [ ] **Step 3: Commit**

```bash
git add CLAUDE.md
git commit -m "docs: locked decision 16 - implementers fan out, supervisors do not"
```

---

### Task 5: Fix the installer, then install and verify

**Files:**
- Modify: `kit/install.ps1:35-38`

**Context the implementer needs:** `install.ps1` currently copies only three of the five role
commands by name, while the orchestrator app's own installer (`KitAssets_Installer`) globs the
whole `kit/commands/` folder and has always installed all five. `reviewer.md` and `solo.md` ARE
installed today — by the app, via that glob, at every app startup — so the claim that they were
never installed is false; only this script disagreed with the app's own delivery path. The fix is
still worth having: it makes the two paths agree, so a role added to the kit in future can never be
installed by one and silently skipped by the other, and it matters on a fresh machine that has not
built the app yet. Verified on 2026-08-11: the installed copies of all five commands were also
stale, missing the fresh-read rule for `[n]` and the timestamp (locked decision #12) — the app's
installer reads from its OWN BUILD OUTPUT, not from `kit/commands/` directly, so that staleness is
fixed by rebuilding, not by this script (see CLAUDE.md decision #17).

- [ ] **Step 1: Install every role command instead of three**

Replace this exact text at `kit/install.ps1:35-38`:

```powershell
Copy-Item (Join-Path $kitFolder 'commands\supervisor.md') $commandsFolder -Force
Copy-Item (Join-Path $kitFolder 'commands\implementer.md') $commandsFolder -Force
Copy-Item (Join-Path $kitFolder 'commands\general-supervisor.md') $commandsFolder -Force
Write-Host 'Installed /supervisor, /implementer, /general-supervisor commands.' -ForegroundColor Green
```

with:

```powershell
# Every .md in kit\commands is a role command — copy them all, so a new role can never be
# added to the kit and silently never installed (reviewer.md and solo.md were missing here
# from the day they were written).
$roleCommands = Get-ChildItem (Join-Path $kitFolder 'commands') -Filter *.md
Copy-Item $roleCommands.FullName $commandsFolder -Force
$installedNames = ($roleCommands | ForEach-Object { '/' + $_.BaseName }) -join ', '
Write-Host "Installed $($roleCommands.Count) role commands: $installedNames" -ForegroundColor Green
```

- [ ] **Step 2: Verify the installer is syntactically valid WITHOUT running it**

Run:

```powershell
$null = [System.Management.Automation.Language.Parser]::ParseFile((Resolve-Path 'kit\install.ps1'), [ref]$null, [ref]$errs); $errs
```

Expected: no output (no parse errors).

- [ ] **Step 3: ASK THE OWNER before rebuilding and restarting the app**

Do NOT rebuild or restart the app unprompted. Per CLAUDE.md decision #17, the ORCHESTRATOR APP is
the kit's delivery path, not `kit/install.ps1`: `AIOrchestrator.csproj` copies `kit/commands/*.md`
into the app's build output, and `KitAssets_Installer` overwrites `~/.claude/commands` from THAT
output at every app startup. Restarting the app affects every orchestration currently running —
sessions pick up the new role-command text only on their next boot (respawn or the restart itself).
Ask the owner for a quiet moment, and tell them this delivery also carries the decision #12
fresh-read rule that has been sitting uninstalled.

- [ ] **Step 4: Once approved, rebuild and restart the app**

```powershell
dotnet build AIOrchestrator.slnx
```

Then close and relaunch the orchestrator app so `Ensure_KitAssetsInstalled` runs against the fresh
build output. (`kit/install.ps1` is the separate bootstrap path for a machine that has not built the
app yet — it is not what delivers an edit on a machine that already has.)

- [ ] **Step 5: AFTER the restart, verify kit and installed copies are now identical**

A `diff` taken before the app restart reads IDENTICAL for reasons that mean nothing — the installed
copies were never touched by the rebuild, only by the restart that follows it. Run this only after
the app has restarted:

```bash
cd "C:/Users/Gianpiero/source/repos/AIOrchestrator/kit/commands"
for f in supervisor implementer reviewer solo general-supervisor; do
  printf "%-20s " "$f"
  diff -q "$f.md" "$HOME/.claude/commands/$f.md" >/dev/null 2>&1 && echo IDENTICAL || echo DIFFERS
done
```

Expected: `IDENTICAL` on all five lines. Anything else means the app has not yet restarted with the
new build, not that the fix failed.

- [ ] **Step 6: Commit**

```bash
git add kit/install.ps1
git commit -m "fix(install): install every role command, not just three"
```

---

## After the plan — the live exercise (spec §6.2)

The plan's grep assertions prove the TEXT landed. They cannot prove agents behave differently, and no
task in this plan can. The spec's remaining verification is a single live run, to be done by the owner
at a natural moment — not manufactured as a test:

On the next real multi-file task, the supervisor briefs it with a `PARALLEL UNITS` block, and the
implementer's boundary report should show (a) planned vs actual agent count, (b) one `WRITING WINDOW
OPEN`/`CLOSED` pair spanning every unit's files, and (c) evidence the implementer verified itself —
a diff read and a suite count, not a relayed agent summary. If (c) is missing, the "a sub-agent's
report is NOT evidence" rule needs strengthening, and that is the one outcome worth a follow-up.

## Out of scope, flagged for the owner

`~/.claude/commands/communicator.md` exists on the live machine but has **no counterpart in
`kit/commands/`** — that role's protocol is unversioned and not covered by the installer. After Task 5
the installer copies whatever is in the kit, so `communicator.md` will still not be installed on a new
machine. Bringing it into the repo is a separate piece of work; raise it with the owner, do not do it
inside this plan.
