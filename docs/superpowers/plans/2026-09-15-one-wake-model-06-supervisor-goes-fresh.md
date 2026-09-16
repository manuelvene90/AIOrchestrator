# One Wake Model — Plan 06: the supervisor goes fresh

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Stop carrying the supervisor's transcript from turn to turn. `[measured, VPS, 6–9 Sep 2026 — quoted, re-derived by Task 1]` 91 % of the supervisor's per-call context is inherited from earlier turns; it is the largest single saving in this system. It is also **the only change in the series that can lose something**, because the state pack carries facts and *"we already tried this and it failed"* is not a fact on disk — it is a conclusion, and it lives only in the conversation being thrown away.

**Architecture:** Three things must be true before the transcript can go, and only the third is a flip.
1. The pack must actually reach the supervisor. On the runner the supervisor runs — `stream` — **it does not today**: `StreamTurnExecutorModel` contains zero references to `StatePack` (verified 2026-09-15, `grep -c StatePack` ⇒ 0). A supervisor flipped to `resume: fresh` on stream right now gets a role command and its pending entries, and nothing else. Tasks 3 and 4.
2. The supervisor's **conclusions** must have a home the app reads back. Members have `progress.md`; `StatePack_Locator.Get_ProgressFile_OrNull` returns **null for `SessionRoles.Supervisor`**, so the supervisor has nowhere to write one down. Task 5, plus the brake in Task 6 that refuses freshness when that home is empty.
3. Only then, the flip — per orchestration, one key, with the rollback criterion written before the work and the transcript-era verdicts captured **before** the switch, or the blind comparison has nothing to compare against. Tasks 7–10.

**Tech Stack:** C# / .NET 10 (the lib is platform-neutral), xUnit, bash (kit skills + preflight), python3 (measurement only).

**Spec:** `docs/superpowers/specs/2026-09-15-one-wake-model-design.md` §4 step 6, which points at `docs/superpowers/specs/2026-09-08-token-efficiency-design.md` §C1 (stage-scoped sessions), C1.2 (the `STATE:` block) and C2 (the pack).

**Branch:** `feat/supervisor-fresh`, worktree `../AIOrchestrator-fresh-sup`, based on `ours/integration` at `c3b53fc`.

---

## Copies read (decision 18)

Everything asserted below about the code was read **in this worktree**, `feat/supervisor-fresh` at `c3b53fc`, on 2026-09-15. **No installed kit copy was read**, and **no running app exists on this machine** — so every claim about `kit/skills/**` is about the BRANCH SOURCE, and Task 9's verification is explicitly against a restarted session instead (decisions 17 and 23). The VPS was not reached: every `[measured]` figure carried over from the September documents stays quoted until Task 1 re-derives it.

---

## Preconditions — declared, not assumed

This plan does not start until all three hold. Task 2 turns them into one command that **refuses to run rather than reporting a pass it did not check** (decision 20).

| # | precondition | why it is a precondition | status on 2026-09-15 |
|---|---|---|---|
| **P1** | **Plan 01 shipped and `runners.<role>.wake = ticket` live**, so a terminal session is handed a pack (plan 01 Task 11). | The supervisor may run in a terminal on the owner's Windows machine. `resume` is ONE key read by both runners, so flipping it fresh reaches the terminal respawn path too (`OrchestrationLauncherModel.cs:600`) — and the terminal runner writes no pack at all until plan 01 Task 11. Without P1, a fresh terminal supervisor starts empty with nothing handed to it. | **NOT MET.** Plan 01 is written, not shipped on this branch. |
| **P2** | **The pack has run on BOTH runners for ≥ 3 consecutive days**, on a real orchestration, with no pack-attributed incident. | Three days is the spec's own gate. It is not a formality: the pack is the only thing standing between a fresh supervisor and amnesia, and its failure mode is silent — `Write_StatePack` swallows every exception by design, and the session then falls back to its boot sequence without anyone being told. | **NOT MET**, and not measurable until P1. |
| **P3** | **The C1.2 `STATE:` block shipped** — ≤ 2 KB, five fixed keys, **stripped from the channel entry before the append**, stored as `<member>/state.md` — so a conclusion is transcribed instead of being lost with the transcript. | This is the whole mitigation for the one risk. Without it the flip is a data-loss event with a saving attached. | **NOT MET**, and the word is already taken — see **OQ1**. |

**P3 is not this plan's to build.** It is a plan of its own (spec C1.2, rollout step 4), for every role. What **is** this plan's is the half that is specific to the supervisor and that P3's plan will not cover, because the supervisor is not a member: it has no member folder, so `<member>/state.md` has no meaning for it. Task 5 gives it one and wires it into its pack.

**Verified state of P3's ground on 2026-09-15, and it is not what the spec's word suggests.** `STATE:` already exists in this tree and means something else: `AIOrchestratorCoreLib/Channels/DeclaredState_Parser.cs` reads **one line, capped at 120 characters** (`MAX_LENGTH = 120`), last-one-wins, for the PULSE status row — the owner's 2026-09-09 ruling that a session says what it is doing instead of the app guessing. A five-field ≤ 2 KB block whose first line begins `STATE:` would be picked up by that parser and truncated into the phone's status row. `grep -rn "state\.md" AIOrchestratorCoreLib kit` returns **nothing**: the C1.2 file does not exist anywhere.

---

## The risk this plan exists to survive

The spec states it in one line and then moves on. It is the reason this plan is longer than the change:

> the pack carries facts, not conclusions — *"we already tried this and it failed"* lives only in the conversation.

Everything in `StatePack_Builder` today is a **fact that can be re-derived**: the brief (an entry in the channel), the last own report (an entry), `PLAN.md`, `git status`, `git log -3`, the owner tail, the pending entries. Throw the transcript away and every one of them survives, because each is read from disk each time. Exactly one class of thing does not survive: **what the supervisor concluded and wrote down nowhere.**

- *"imp-2 tried the `ChannelFence_Screen` route on 2026-09-13 and it cannot work, because the tailer re-anchors before the fence closes."*
- *"The owner said on the 11th that the Italian layer question is open — do not re-decide it."*
- *"I already rejected rev-1's finding 3; it is a false positive and re-opening it costs a round."*

None of those are in `PLAN.md`, none in git, none necessarily in a channel entry — a conclusion reached mid-turn and acted on is often never written at all. A fresh supervisor **re-proposes the dead end**, an implementer spends a day on it again, and the loss is invisible: nothing errors, nothing is red, the work simply goes round a second time. That is worse than a crash, because a crash is reported.

**So the plan's shape is: the transcript may only go once the conclusions have somewhere else to live, and the app must refuse to make the session fresh while that somewhere is empty.** Tasks 5 and 6 are that refusal. Tasks 7 and 9 are how we find out whether it worked, and Task 8 is how we put it back if it did not.

---

## Global Constraints

- **Coding patterns:** `AIOrchestratorCoreLib` is STRICT — interface + `Model` + `_Factory` triples, immutable types, no mutable public state. Follow the files this plan touches (`Running/StatePack/`, `Running/TurnExecutor/`), not memory.
- **No channel-format change.** `ChannelAppender`, `channel-append.sh`, `Channel_Compactor` and the entry header are untouched. The conclusions file is a sibling file, never an entry format.
- **One formatter, never two (decision 12).** Task 3 exists because there is one pack writer and it lives in the print executor; the fix is to MOVE it to a shared home, never to add a second one to the stream executor. If you find yourself writing `StatePack_Builder.Build` in a second place, stop.
- **Say which copy you read (decision 18)** in every step's output: branch source, build output, installed kit, or the running binary. A kit edit is verified against a RESTARTED session, never against `git diff` (decisions 17 and 23).
- **Additive until Task 8.** Tasks 1–7 change nothing about what any live session does: the supervisor's default stays `resume: transcript` (`RoleRunnerConfig_Factory.Create_Default` returns `Transcript` for every role but `General`). Task 8 is the only behavioural flip and it is one key on one orchestration.
- **Stage explicit paths, never `git add -A` / `git add .`. Never `--no-verify`. Multi-line messages via `git commit -F <tempfile>`. Commits in English, `type(scope): a descriptive clause`, body with the why and dated evidence. The merge to `master` is Nathan's — do not do it.**
- **`dotnet` is NOT on PATH on this Mac.** It is at `~/.dotnet/dotnet` (SDK **10.0.401**, verified 2026-09-15). Start every shell with `export PATH="$HOME/.dotnet:$PATH"`.
- **Never run the whole suite in a sub-agent step.** A sub-agent gets 120 s per command and the full suite is ~3 min 20 s — it will be killed mid-run and report a false red. **Every step below names a `--filter`.** The full run belongs to the dispatching session, once, at the end: `dotnet test AIOrchestratorCoreLib.Tests/AIOrchestratorCoreLib.Tests.csproj`, **3 955 passed / 10 skipped / 0 failed** as of 2026-09-15 `[documented — the brief's figure, not re-measured here]`.
- **Three tests are flaky under load, green 3/3 in isolation:** `ClosingTurnReviewFix…`, `TolerantFileReader…`, `EffortDial…`. A red in one of those three is re-run alone before it is believed. **Any other red is yours.**
- **Filter syntax, verified 2026-09-15:** `dotnet test AIOrchestratorCoreLib.Tests/AIOrchestratorCoreLib.Tests.csproj --filter "FullyQualifiedName~StatePackBuilderTests"` ⇒ *Passed! Failed: 0, Passed: 7*, 5.4 s wall including the build.

---

## File Structure

**Created:**

| file | responsibility |
|---|---|
| `tools/wake-baseline/context_share.py` | reads transcripts, prints inherited-context share per role — the 91 % re-derived. Aggregates only; never dumps a `.jsonl`. Lives beside plan 01's `baseline.py`: ONE measurement home. |
| `tools/wake-baseline/test_context_share.py` | its test, including the refusal case |
| `tools/fresh-supervisor/preflight.sh` | answers "may this orchestration go fresh?" — P1, P2, P3 — and **exits non-zero when it cannot check**, never silently passes |
| `tools/fresh-supervisor/capture-verdicts.sh` | pulls N supervisor verdicts out of a channel + its archive, anonymised, for the blind read |
| `AIOrchestratorCoreLib/Running/StatePack/TurnStatePack_Writer.cs` | the ONE place a turn's pack is written — used by both executors |
| `AIOrchestratorCoreLib/Running/StatePack/FreshSupervisor_Gate.cs` | may this session be started fresh? — refuses while its conclusions file is empty |
| `AIOrchestratorCoreLib.Tests/Running/StatePack/StreamSessionGetsItsPackTests.cs` | the stream runner writes and points at a pack |
| `AIOrchestratorCoreLib.Tests/Running/StatePack/SupervisorConclusionsTests.cs` | the conclusions file is located, read, capped and ordered |
| `AIOrchestratorCoreLib.Tests/Running/StatePack/FreshSupervisorGateTests.cs` | the refusal, and that it is said once |
| `docs/superpowers/plans/2026-09-15-one-wake-model-06-report.md` | before/after figures, the reds, the copies read, what is left open |

**Modified:**

| file | change |
|---|---|
| `AIOrchestratorCoreLib/Running/TurnExecutor/PrintTurnExecutorModel.cs` | its private `Write_StatePack` moves out to `TurnStatePack_Writer`; it calls it |
| `AIOrchestratorCoreLib/Running/TurnExecutor/StreamTurnExecutorModel.cs` | a fresh turn writes a pack and sends a pointer instead of the entries |
| `AIOrchestratorCoreLib/Running/PrintTurnPrompt_Builder.cs` | `+ Build_FreshTurnPointer(requestId, packFile)` |
| `AIOrchestratorCoreLib/Running/StatePack/StatePack_Locator.cs` | `+ Get_ConclusionsFile_OrNull` — `<orch>/.supervisor.state.md` for the supervisor, `<orch>/<member>/state.md` for the rest, null for the general |
| `AIOrchestratorCoreLib/Running/StatePack/StatePackInputs.cs` | `+ string? Conclusions` |
| `AIOrchestratorCoreLib/Running/StatePack/StatePackInputs_Reader.cs` | reads it, names it in `Unavailable` when it cannot |
| `AIOrchestratorCoreLib/Running/StatePack/StatePack_Builder.cs` | `+ CONCLUSIONS_HEADING`, `+ CONCLUSIONS_CAP`, the section, and its place in the pinned order |
| `AIOrchestratorCoreLib/Running/PrintTurnDispatcher/PrintTurnDispatcherModel.cs:1374` | `fresh` passes through the gate |
| `kit/skills/supervisor/SKILL.md` | the supervisor is told to keep its conclusions file, and what belongs in it |

---

## Task 1: Re-derive the 91 %

**Files:**
- Create: `tools/wake-baseline/context_share.py`
- Create: `tools/wake-baseline/test_context_share.py`

**Interfaces:**
- Consumes: Claude Code transcripts (`~/.claude/projects/**/*.jsonl`).
- Produces: `python3 tools/wake-baseline/context_share.py --projects <dir> --format json` printing, per role, `calls`, `mean_input_tokens`, `mean_inherited_tokens`, `inherited_share`. Task 10 runs it again after the flip.

**Why it is Task 1:** the 91 % is quoted from a September document (spec §4 step 6, `[measured]` on the VPS). The acceptance of this plan is *"supervisor mean context < 80 k"* — a number measured by nothing in this repo. **Do not flip a dial whose before-value you have never read.** This is also the task that forces the metric to be defined before it can be gamed (**OQ5**).

- [ ] **Step 1: Write the failing test**

```python
# tools/wake-baseline/test_context_share.py
import json, pathlib, subprocess, sys, tempfile

def write_transcript(path, rows):
    path.write_text("\n".join(json.dumps(r) for r in rows) + "\n")

def test_inherited_share_is_cache_read_over_total_input():
    """INHERITED = what the model was handed that it had already been handed before.

    In the CLI's own accounting that is `cache_read_input_tokens`: a resumed turn re-sends the whole
    conversation and the prefix hits the cache. `input_tokens` is what is NEW this call. The share is
    read/(read+new) — NOT read/input, which would be a ratio greater than one, and not a count of
    turns, which says nothing about size.
    """
    with tempfile.TemporaryDirectory() as d:
        root = pathlib.Path(d)
        proj = root / "repo-1"; proj.mkdir()
        write_transcript(proj / "sup.jsonl", [
            {"sessionId": "s1", "type": "assistant",
             "message": {"id": "m1", "usage": {"input_tokens": 1_000, "cache_read_input_tokens": 9_000,
                                               "cache_creation_input_tokens": 0, "output_tokens": 300}}},
            {"sessionId": "s1", "type": "assistant",
             "message": {"id": "m2", "usage": {"input_tokens": 1_000, "cache_read_input_tokens": 9_000,
                                               "cache_creation_input_tokens": 0, "output_tokens": 300}}},
        ])
        out = subprocess.run(
            [sys.executable, "tools/wake-baseline/context_share.py",
             "--projects", str(root), "--role-of", "sup.jsonl=supervisor", "--format", "json"],
            capture_output=True, text=True, check=True).stdout
        got = json.loads(out)["supervisor"]

        assert got["calls"] == 2
        assert got["mean_input_tokens"] == 10_000
        assert got["inherited_share"] == 0.9

def test_the_same_message_is_counted_once():
    """A transcript replays assistant messages on resume, and sidechains repeat them too. Counting
    `m1` twice inflates every figure this plan is judged by (C7: dedupe by message.id)."""
    with tempfile.TemporaryDirectory() as d:
        root = pathlib.Path(d)
        proj = root / "repo-1"; proj.mkdir()
        row = {"sessionId": "s1", "type": "assistant",
               "message": {"id": "m1", "usage": {"input_tokens": 1_000, "cache_read_input_tokens": 9_000,
                                                 "cache_creation_input_tokens": 0, "output_tokens": 300}}}
        write_transcript(proj / "sup.jsonl", [row, row])
        out = subprocess.run(
            [sys.executable, "tools/wake-baseline/context_share.py",
             "--projects", str(root), "--role-of", "sup.jsonl=supervisor", "--format", "json"],
            capture_output=True, text=True, check=True).stdout

        assert json.loads(out)["supervisor"]["calls"] == 1

def test_it_refuses_a_projects_dir_that_does_not_exist():
    """A harness that cannot find what it measures REFUSES (decision 20). Printing `0 calls` for a
    mistyped path is how a plan gets certified against nothing."""
    r = subprocess.run(
        [sys.executable, "tools/wake-baseline/context_share.py", "--projects", "/nope/nowhere"],
        capture_output=True, text=True)

    assert r.returncode != 0
    assert "does not exist" in (r.stderr + r.stdout)
```

- [ ] **Step 2: Run it and watch it fail**

Run: `python3 -m pytest tools/wake-baseline/test_context_share.py -q`
Expected: FAIL — `context_share.py` does not exist.

- [ ] **Step 3: Write the script**

```python
#!/usr/bin/env python3
"""Inherited-context share per role. Aggregates only — NEVER prints a transcript or any message text.

THE ONE NUMBER THIS PLAN TURNS ON. The 2026-09-15 spec says 91 % of a supervisor call's context is
carried from earlier turns; that figure was measured on the VPS in early September and has never been
re-derived. `inherited_share` is cache_read / (cache_read + input + cache_creation): of everything the
model was handed, the fraction it had already been handed. A FRESH session's share is near zero by
construction — it has no prefix to hit — which is exactly what the flip is bought with.
"""
import argparse, collections, json, sys
from pathlib import Path


def usage_rows(path):
    """Yields (message_id, usage) for assistant messages. A malformed line is skipped, not fatal:
    a transcript is appended to live and its last line can be half-written."""
    with path.open(errors="replace") as handle:
        for line in handle:
            try:
                row = json.loads(line)
            except json.JSONDecodeError:
                continue
            message = row.get("message") or {}
            usage = message.get("usage")
            if row.get("type") == "assistant" and isinstance(usage, dict) and message.get("id"):
                yield message["id"], usage


def collect(projects, role_of):
    seen = set()
    totals = collections.defaultdict(lambda: {"calls": 0, "input": 0, "inherited": 0})

    for path in sorted(projects.rglob("*.jsonl")):
        role = role_of.get(path.name, "unattributed")
        for message_id, usage in usage_rows(path):
            if message_id in seen:
                continue
            seen.add(message_id)
            fresh = (usage.get("input_tokens") or 0) + (usage.get("cache_creation_input_tokens") or 0)
            inherited = usage.get("cache_read_input_tokens") or 0
            bucket = totals[role]
            bucket["calls"] += 1
            bucket["input"] += fresh + inherited
            bucket["inherited"] += inherited

    result = {}
    for role, bucket in totals.items():
        calls = bucket["calls"]
        result[role] = {
            "calls": calls,
            "mean_input_tokens": round(bucket["input"] / calls) if calls else 0,
            "mean_inherited_tokens": round(bucket["inherited"] / calls) if calls else 0,
            "inherited_share": round(bucket["inherited"] / bucket["input"], 4) if bucket["input"] else 0.0,
        }
    return result


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--projects", required=True)
    parser.add_argument("--role-of", action="append", default=[],
                        help="<transcript file name>=<role>, repeatable")
    parser.add_argument("--format", choices=["text", "json"], default="text")
    args = parser.parse_args()

    projects = Path(args.projects)

    # REFUSES rather than reporting zero (decision 20).
    if not projects.is_dir():
        print(f"context_share: --projects {projects} does not exist or is not a directory", file=sys.stderr)
        return 2

    role_of = dict(pair.split("=", 1) for pair in args.role_of)
    result = collect(projects, role_of)

    if args.format == "json":
        json.dump(result, sys.stdout)
    else:
        for role, figures in sorted(result.items()):
            print(f"== {role} ==")
            for key, value in figures.items():
                print(f"  {key:24s} {value}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
```

- [ ] **Step 4: Run it and watch it pass — and run the text path by hand**

Run: `python3 -m pytest tools/wake-baseline/test_context_share.py -q`
Expected: PASS, 3 tests.

Then the command a person types, against a real projects dir, and READ the output:
`python3 tools/wake-baseline/context_share.py --projects ~/.claude/projects | head -40`

- [ ] **Step 5: Record the before-figure**

Write the supervisor's `inherited_share` and `mean_input_tokens` into the report file's *Before* table (Task 10 creates the file; create it now with just that table if it does not exist). **Say which machine and which window.** If the only transcripts reachable are this Mac's, say so — the VPS figure stays `[quoted]` until someone with credentials runs the same script there, and **the acceptance gate in Task 9 then compares like with like or not at all.**

- [ ] **Step 6: Commit**

```bash
git add tools/wake-baseline/context_share.py tools/wake-baseline/test_context_share.py docs/superpowers/plans/2026-09-15-one-wake-model-06-report.md
git commit -F /tmp/cm.txt   # "tools(baseline): the inherited-context share, re-derived from transcripts instead of quoted"
```

---

## Task 2: The preflight — the three preconditions become one command that can say no

**Files:**
- Create: `tools/fresh-supervisor/preflight.sh`
- Test: the script's own `--self-test` mode, run from the repo root

**Interfaces:**
- Produces: `bash tools/fresh-supervisor/preflight.sh <orch-id>` ⇒ exit 0 (may go fresh) / 1 (a precondition fails, and it says which) / 2 (**cannot check** — a path it needs does not exist).

**Why it is a task and not a paragraph:** "the pack proven on both runners for three days" is a sentence nobody can be held to. Made into a command it becomes an acceptance criterion, and — this is the half that matters — a command that **exits 2 when it cannot see what it is asked about** cannot certify a precondition by accident. `hook-behaviour-check.sh` reported 16 confident failures about code it never executed (decision 20); this script is written against that memory.

- [ ] **Step 1: Write the failing self-test**

> **Where this goes:** `self_test` must be DEFINED before the dispatch line in Step 3 calls it — bash
> resolves a function at call time, so a definition placed after the `--self-test` branch gives
> `command not found`. Put the whole function immediately under the script's header comment. And note
> that it re-invokes the script as `bash "$0"`, never `"$0"`: the file is not executable until somebody
> `chmod +x`es it, and `"$0"` alone exits **126**. Verified 2026-09-15 by assembling these two blocks
> and running them — which is the only reason this note exists.

```bash
# tools/fresh-supervisor/preflight.sh — the self-test cases, written first
self_test() {
  local failures=0 root out rc

  # CANNOT CHECK ⇒ 2. Never 0, never 1: "I did not look" is not "it is fine".
  root="$(mktemp -d)"
  out="$(AIORCH_SUPERVISION_ROOT="$root/nope" bash "$0" repo-1 2>&1)"; rc=$?
  [ "$rc" = 2 ] || { echo "FAIL: a missing supervision root must exit 2, got $rc"; failures=1; }
  case "$out" in *"cannot check"*) ;; *) echo "FAIL: it must say it cannot check"; failures=1;; esac

  # P3 missing ⇒ 1, naming P3.
  mkdir -p "$root/repo-1"
  : > "$root/repo-1/.supervisor.pack.md"
  out="$(AIORCH_SUPERVISION_ROOT="$root" bash "$0" repo-1 2>&1)"; rc=$?
  [ "$rc" = 1 ] || { echo "FAIL: no conclusions file must exit 1, got $rc"; failures=1; }
  case "$out" in *P3*) ;; *) echo "FAIL: it must name the precondition that failed"; failures=1;; esac

  # All three ⇒ 0.
  printf 'dead ends:\n- the fence route (2026-09-13): cannot work, the tailer re-anchors first\n' \
    > "$root/repo-1/.supervisor.state.md"
  touch -t 202609010000 "$root/repo-1/.supervisor.pack.md"
  out="$(AIORCH_SUPERVISION_ROOT="$root" bash "$0" repo-1 2>&1)"; rc=$?
  [ "$rc" = 0 ] || { echo "FAIL: all preconditions met must exit 0, got $rc — $out"; failures=1; }

  rm -rf "$root"
  [ "$failures" = 0 ] && echo "preflight self-test: PASS"
  return "$failures"
}
```

- [ ] **Step 2: Run it and watch it fail**

Run: `bash tools/fresh-supervisor/preflight.sh --self-test`
Expected: FAIL — the script does not exist.

- [ ] **Step 3: Write the script**

```bash
#!/usr/bin/env bash
# MAY THIS ORCHESTRATION'S SUPERVISOR GO FRESH? — the three preconditions of plan 06, asked of the
# machine rather than of a document.
#
#   0  yes
#   1  no, and it names which precondition failed
#   2  it CANNOT TELL — a path it needs is not there. Never confused with 0: a check that did not run
#      is not a check that passed (CLAUDE.md decision 20, hook-behaviour-check.sh, 2026-08-11).
set -u

SUP="${AIORCH_SUPERVISION_ROOT:-$HOME/.claude/supervision}"
PACK_MIN_AGE_DAYS=3

[ "${1:-}" = "--self-test" ] && { self_test; exit $?; }

ORCH="${1:-}"
[ -n "$ORCH" ] || { echo "usage: preflight.sh <orch-id>" >&2; exit 2; }

FOLDER="$SUP/$ORCH"
[ -d "$FOLDER" ] || { echo "cannot check: $FOLDER does not exist" >&2; exit 2; }

PACK="$FOLDER/.supervisor.pack.md"
STATE="$FOLDER/.supervisor.state.md"
fail=0

# P1 + P2 in one reading: a pack that EXISTS and is at least PACK_MIN_AGE_DAYS old is a pack that has
# been written to for that long. (A pack is rewritten every fresh turn, so its mtime is recent; its
# BIRTH is what "three days" is about — read the oldest pack-shaped file in the folder.)
oldest="$(find "$FOLDER" -maxdepth 2 -name '*pack.md' -type f -print 2>/dev/null | head -1)"
if [ -z "$oldest" ]; then
  echo "P1/P2 FAIL: no state pack has ever been written under $FOLDER — plan 01 has not shipped here"
  fail=1
elif [ -z "$(find "$oldest" -mtime +"$PACK_MIN_AGE_DAYS" -print 2>/dev/null)" ] \
  && [ -z "$(find "$FOLDER" -maxdepth 2 -name '*pack.md' -mtime +"$PACK_MIN_AGE_DAYS" -print 2>/dev/null)" ]; then
  echo "P2 FAIL: no pack older than $PACK_MIN_AGE_DAYS days under $FOLDER — the pack has not been proven long enough"
  fail=1
fi

# P3: the conclusions have a home AND something in it. An EMPTY file is a fail, not a pass — it is
# precisely the state in which the flip loses everything and reports success.
if [ ! -s "$STATE" ]; then
  echo "P3 FAIL: $STATE is missing or empty — the supervisor's conclusions have nowhere to live, and going fresh would drop them"
  fail=1
fi

# Said even on success, because the next reader needs to know WHICH copies were consulted (decision 18).
echo "preflight read: $FOLDER (pack: ${oldest:-none}, conclusions: $STATE)"
exit "$fail"
```

- [ ] **Step 4: Run the self-test and watch it pass**

Run: `bash tools/fresh-supervisor/preflight.sh --self-test`
Expected: `preflight self-test: PASS`.

Then run it for real against this machine and **read the exit code**:
`bash tools/fresh-supervisor/preflight.sh repo-1; echo "exit $?"`
Expected today: **2** — there is no supervision data on this Mac (`~/.claude/supervision` holds `config.json`, `secrets.json`, `statusline.sh` and `"repos": []`). That is the correct answer and it is the point.

- [ ] **Step 5: Commit**

```bash
git add tools/fresh-supervisor/preflight.sh
git commit -F /tmp/cm.txt   # "tools(fresh-supervisor): the three preconditions become a command that can answer 'I cannot tell'"
```

---

## Task 3: One pack writer, and the stream runner starts using it

**Files:**
- Create: `AIOrchestratorCoreLib/Running/StatePack/TurnStatePack_Writer.cs`
- Modify: `AIOrchestratorCoreLib/Running/TurnExecutor/PrintTurnExecutorModel.cs` (its private `Write_StatePack` moves out)
- Modify: `AIOrchestratorCoreLib/Running/TurnExecutor/StreamTurnExecutorModel.cs`
- Test: `AIOrchestratorCoreLib.Tests/Running/StatePack/StreamSessionGetsItsPackTests.cs`

**Interfaces:**
- Consumes: `StatePackInputs_Reader.Read`, `StatePack_Builder.Build`, `StatePack_Writer.Write`, `StatePack_Locator.Get_File`.
- Produces: `TurnStatePack_Writer.Write_OrNull(paths, state, requestId, pending, sources)` ⇒ the pack's path, or null when nothing was written. Task 4 uses the return value.

**The defect this closes, verified 2026-09-15 on this branch:** `grep -c "StatePack" AIOrchestratorCoreLib/Running/TurnExecutor/StreamTurnExecutorModel.cs` ⇒ **0**. The pack is written by `PrintTurnExecutorModel.Write_StatePack`, a `private void`. The supervisor's recommended transport is `stream` (p50 1.27 s vs 5.77 s, which is why it is the supervisor's shape). **So the one role this plan is about runs on the one transport that has never written a pack.** The spec's step 6 is gated on "the pack proven on both runners" and quietly assumes both runners have one.

- [ ] **Step 1: Write the failing test**

```csharp
// AIOrchestratorCoreLib.Tests/Running/StatePack/StreamSessionGetsItsPackTests.cs
using AIOrchestratorCoreLib.Channels;
using AIOrchestratorCoreLib.Running;
using AIOrchestratorCoreLib.Running.SessionLaunch;
using Xunit;

namespace AIOrchestratorCoreLib.Tests.Running.StatePack;

/// <summary>
/// THE SUPERVISOR'S OWN TRANSPORT HAD NO PACK. Verified 2026-09-15 on `feat/supervisor-fresh` at
/// c3b53fc: <c>StreamTurnExecutorModel</c> contained zero references to StatePack, while
/// <c>PrintTurnExecutorModel</c> wrote one in a private method. The supervisor runs on stream by
/// design (p50 1.27 s against 5.77 s), so a supervisor flipped to <c>resume: fresh</c> would have got
/// its role command and its pending entries and nothing else — no brief, no ledger, no git state, no
/// owner tail, and no conclusions. That is the amnesia this whole plan exists to prevent, and it was
/// one config word away.
/// </summary>
public class StreamSessionGetsItsPackTests
{
    [Fact]
    public async Task A_fresh_stream_supervisor_is_handed_a_pack()
    {
        using var harness = new PrintRunnerTestHarness("supervisor:stream", resumeForMembers: "fresh");
        var supervisorId = harness.Register_Supervisor();
        harness.Write_Scenario("""{"turns":[{"result":"sup online"},{"result":"VERDICT — merged"}]}""");
        var dispatcher = harness.Create_Dispatcher();

        Assert.True(ChannelAppender.Append_OwnerEntry(harness.Paths.Get_OwnerChannelFile("repo-1"), "do the merge", DateTime.Now));
        Assert.True(PrintRunnerTestHarness.Drive_Until(
            dispatcher,
            () => harness.Read_State(SessionRoles.Supervisor, "repo-1", supervisorId).ExecutedTurns.Count == 1,
            PrintRunnerTestHarness.GENEROUS));
        await dispatcher.Stop_Async();

        var pack = harness.Read_Pack_OrNull(SessionRoles.Supervisor, "repo-1", supervisorId);

        Assert.NotNull(pack);
        Assert.Contains("do the merge", pack);
        Assert.Contains("## The ledger — PLAN.md", pack!);
    }

    /// <summary>
    /// AND A RESUMED ONE IS NOT. A pack is what replaces a transcript; a session that still has its
    /// transcript is handed its entries on stdin as it always was, and writing a pack for it would be
    /// a second copy of the same context in the same turn.
    /// </summary>
    [Fact]
    public async Task A_resumed_stream_supervisor_gets_no_pack()
    {
        using var harness = new PrintRunnerTestHarness("supervisor:stream", resumeForMembers: "transcript");
        var supervisorId = harness.Register_Supervisor();
        harness.Write_Scenario("""{"turns":[{"result":"sup online"},{"result":"VERDICT — merged"}]}""");
        var dispatcher = harness.Create_Dispatcher();

        Assert.True(ChannelAppender.Append_OwnerEntry(harness.Paths.Get_OwnerChannelFile("repo-1"), "do the merge", DateTime.Now));
        Assert.True(PrintRunnerTestHarness.Drive_Until(
            dispatcher,
            () => harness.Read_State(SessionRoles.Supervisor, "repo-1", supervisorId).ExecutedTurns.Count == 1,
            PrintRunnerTestHarness.GENEROUS));
        await dispatcher.Stop_Async();

        Assert.Null(harness.Read_Pack_OrNull(SessionRoles.Supervisor, "repo-1", supervisorId));
    }
}
```

> **Note for the implementer:** `PrintRunnerTestHarness` already does all of this — `Register_Supervisor(runner: SessionRunners.Stream)` is its default, `resumeForMembers` sets `resume` for every non-general role (read `Write_Config`, lines 109–147), and `Read_Pack_OrNull` already resolves through `StatePack_Locator`. **Read the harness before touching it.** If a knob is genuinely missing, add it there; do not build a second harness.

- [ ] **Step 2: Run it and watch it fail**

Run: `dotnet test AIOrchestratorCoreLib.Tests/AIOrchestratorCoreLib.Tests.csproj --filter "FullyQualifiedName~StreamSessionGetsItsPackTests"`
Expected: FAIL on the first case — `Read_Pack_OrNull` returns null. The second case passes already; that is fine and it is there to stop the fix from over-reaching.

- [ ] **Step 3: Move the writer out of the print executor**

```csharp
// AIOrchestratorCoreLib/Running/StatePack/TurnStatePack_Writer.cs
using AIOrchestratorCoreLib.Running.PendingTraffic;
using AIOrchestratorCoreLib.Running.PrintSessionState;
using AIOrchestratorCoreLib.Running.TurnSource;
using AIOrchestratorCoreLib.SupervisionPaths;

namespace AIOrchestratorCoreLib.Running.StatePack;

/// <summary>
/// THE ONE PLACE A TURN'S PACK IS WRITTEN. It lived as a private method of
/// <c>PrintTurnExecutorModel</c>, which is why the stream transport — the supervisor's own, and the
/// role this matters most for — never had one (verified 2026-09-15: zero StatePack references in
/// <c>StreamTurnExecutorModel</c>). Moved rather than copied: two executors writing their own pack
/// would be two answers to "what does a fresh session know", which is the failure
/// <c>ITurnExecutor</c> exists to prevent, and CLAUDE.md decision 12 in one sentence.
///
/// <para>
/// GUARDED AS A WHOLE, on top of the reader's per-section guards: a pack that cannot be written must
/// never stop a turn. The session then finds no pack and falls back to its boot sequence, which is
/// the behaviour it had before packs existed. NOTE the cost of that mercy once the supervisor is
/// fresh: a silent fallback is a supervisor with no memory and no complaint, which is why
/// <see cref="FreshSupervisor_Gate"/> exists and why the failure is LOGGED here rather than swallowed
/// in silence (decision 21: a guard that cannot evaluate its predicate says so).
/// </para>
/// </summary>
public static class TurnStatePack_Writer
{
    /// <summary>The pack's path, or null when none was written (nothing pending, or the write failed).</summary>
    public static string? Write_OrNull(
        ISupervisionPaths paths,
        IPrintSessionState state,
        string requestId,
        IReadOnlyList<PendingEntry> pending,
        IReadOnlyList<ITurnSource> sources,
        IOrchestrationLog? log = null)
    {
        // A BOOT TURN WRITES NONE: with nothing pending the role command's own boot sequence is the
        // right thing, and a pack describing no traffic is a page of nothing.
        if (pending.Count == 0)
            return null;

        try
        {
            var file = StatePack_Locator.Get_File(paths, state.Role, state.OrchId, state.MemberId);
            StatePack_Writer.Write(file, StatePack_Builder.Build(StatePackInputs_Reader.Read(paths, state, requestId, pending, sources)));
            return file;
        }
        catch (Exception ex)
        {
            log?.Log_Warning(state.OrchId, $"'{state.MemberId}': the state pack could not be written ({ex.Message}) — the session falls back to its boot sequence");
            return null;
        }
    }
}
```

In `PrintTurnExecutorModel`, delete the private `Write_StatePack` and replace its call site:

```csharp
        else if (fresh && pending.Count > 0)
            TurnStatePack_Writer.Write_OrNull(_paths, state, requestId, pending, sources, _log);
```

- [ ] **Step 4: Write the pack in the stream executor**

In `StreamTurnExecutorModel.Execute_Async`, immediately after `Archive_ProgressNote_IfNewTask`'s equivalent point and **before** the follow-up message is sent (Task 4 uses the returned path):

```csharp
        // THE PACK IS THIS TRANSPORT'S TOO. A fresh stream turn mints a new session id every turn
        // (Ensure_Process: "Fresh mode mints a new id every turn"), so every turn is a new process
        // with no memory — exactly the print executor's situation, and until 2026-09-15 it was the
        // only one of the two that said so in a file.
        var packFile = resumeTranscript ? null : TurnStatePack_Writer.Write_OrNull(_paths, state, requestId, pending, sources, _log);
```

**And carry the progress-note archiving with it** — `StatePack_Locator.Archive_ProgressNote_IfNewTask(...)` runs in the print executor "in every resume mode" by a deliberate 2026-09-11 review finding. The stream executor never called it. Add it beside the pack write, unconditionally, and say in a comment that it is the same call for the same reason.

- [ ] **Step 5: Run the test and watch it pass**

Run: `dotnet test AIOrchestratorCoreLib.Tests/AIOrchestratorCoreLib.Tests.csproj --filter "FullyQualifiedName~StreamSessionGetsItsPackTests"`
Expected: PASS, 2 tests.

- [ ] **Step 6: Run the existing pack and executor oracles — this is the real gate**

```bash
dotnet test AIOrchestratorCoreLib.Tests/AIOrchestratorCoreLib.Tests.csproj \
  --filter "FullyQualifiedName~StatePack|FullyQualifiedName~StreamTurnDispatcherTests|FullyQualifiedName~StreamRunnerUnitTests|FullyQualifiedName~PrintTurnDispatcherTests"
```
Expected: PASS, **with no edits to any of those files.** The move is only correct if the print side is byte-for-byte unchanged in behaviour; if one of them needed an edit, you changed something and the move is wrong.

- [ ] **Step 7: Commit**

```bash
git add AIOrchestratorCoreLib/Running/StatePack/TurnStatePack_Writer.cs AIOrchestratorCoreLib/Running/TurnExecutor/ AIOrchestratorCoreLib.Tests/Running/StatePack/StreamSessionGetsItsPackTests.cs
git commit -F /tmp/cm.txt   # "fix(running): the stream transport writes a state pack too, from the one writer both executors share"
```

---

## Task 4: A fresh stream turn is handed a pointer, not the entries twice

**Files:**
- Modify: `AIOrchestratorCoreLib/Running/PrintTurnPrompt_Builder.cs` (`+ Build_FreshTurnPointer`)
- Modify: `AIOrchestratorCoreLib/Running/TurnExecutor/StreamTurnExecutorModel.cs`
- Test: append to `StreamSessionGetsItsPackTests.cs`

**Interfaces:**
- Consumes: the pack path from Task 3.
- Produces: `PrintTurnPrompt_Builder.Build_FreshTurnPointer(string requestId, string packFile)`.

**Why:** after Task 3 a fresh stream turn sends the pending entries **twice** — once in the pack file and once in `Build_FollowUp`'s stdin message. On a supervisor whose entries are the expensive part, that is the saving paid back. The print executor already solved this: for a fresh turn `prompt` stays null and everything travels in the file. Stream cannot send null (the process needs a message to answer), so it sends the shortest possible one.

**The `[bridge turn <id>]` marker stays.** It is what the entry splitter and the accounting read; a message without it is a turn nothing can attribute.

- [ ] **Step 1: Write the failing test**

```csharp
    /// <summary>
    /// THE ENTRIES TRAVEL ONCE. With the pack written (Task 3) and the follow-up unchanged, a fresh
    /// stream turn paid for its pending entries twice in the same turn — in the file and on stdin.
    /// The pointer keeps the one thing stdin is needed for: the bridge-turn marker that attributes
    /// the turn.
    /// </summary>
    [Fact]
    public async Task A_fresh_stream_turn_is_sent_a_pointer_to_its_pack_not_the_entries()
    {
        using var harness = new PrintRunnerTestHarness("supervisor:stream", resumeForMembers: "fresh");
        var supervisorId = harness.Register_Supervisor();
        harness.Write_Scenario("""{"turns":[{"result":"sup online"},{"result":"VERDICT — merged"}]}""");
        var dispatcher = harness.Create_Dispatcher();

        Assert.True(ChannelAppender.Append_OwnerEntry(harness.Paths.Get_OwnerChannelFile("repo-1"), "do the merge", DateTime.Now));
        Assert.True(PrintRunnerTestHarness.Drive_Until(
            dispatcher,
            () => harness.Read_State(SessionRoles.Supervisor, "repo-1", supervisorId).ExecutedTurns.Count == 1,
            PrintRunnerTestHarness.GENEROUS));
        await dispatcher.Stop_Async();

        var messages = harness.Read_Invocations()
            .Where(line => line["line_kind"]?.GetValue<string>() == "stream-message")
            .Select(line => line["prompt"]!.GetValue<string>())
            .ToList();

        // Two messages, one process: the role command, then the turn.
        Assert.Equal(2, messages.Count);
        Assert.StartsWith($"/aiorch:supervisor repo-1", messages[0]);

        // The turn message names the pack and carries the marker — and does NOT carry the entry.
        Assert.Contains("[bridge turn ", messages[1]);
        Assert.Contains(".supervisor.pack.md", messages[1]);
        Assert.DoesNotContain("do the merge", messages[1]);

        // …which is in the pack, where it is paid for once.
        Assert.Contains("do the merge", harness.Read_Pack_OrNull(SessionRoles.Supervisor, "repo-1", supervisorId)!);
    }
```

> **Note for the implementer:** the boot message's exact text comes from `SessionRole_Names.Build_RoleCommand` — read it and assert what it actually returns rather than the string above if they differ. `StreamTurnDispatcherTests` shows the `Lines(harness, "stream-message")` helper; reuse its shape.

- [ ] **Step 2: Run it and watch it fail**

Run: `dotnet test AIOrchestratorCoreLib.Tests/AIOrchestratorCoreLib.Tests.csproj --filter "FullyQualifiedName~StreamSessionGetsItsPackTests"`
Expected: FAIL — `messages[1]` contains `do the merge` and no pack path.

- [ ] **Step 3: Add the pointer**

```csharp
    /// <summary>
    /// THE WHOLE STDIN MESSAGE OF A FRESH STREAM TURN. Its memory is in the pack (a FILE, for the
    /// reason <see cref="StatePack.StatePack_Builder"/> states: stdin is appended to the positional
    /// prompt, so a slash command's $ARGUMENTS swallows it), and the entries are in the pack too — so
    /// everything that is left to say on stdin is which turn this is and where the pack is.
    ///
    /// <para>
    /// THE MARKER IS NOT DECORATION: <c>[bridge turn …]</c> is what the entry splitter and the
    /// per-stage accounting read. A message without it is a turn nothing can attribute (spec C7).
    /// </para>
    /// </summary>
    public static string Build_FreshTurnPointer(string requestId, string packFile)
    {
        return $"[bridge turn {requestId}]\n"
            + $"You are a FRESH session and your state pack is at {packFile}. Read it FIRST and whole: it holds "
            + "the entries that woke you, your brief, your last report, the ledger, the code state and the "
            + "conclusions you recorded earlier. Then act and write your entry. Do not go looking through the "
            + "channels for what woke you — it is in the pack.";
    }
```

and in `StreamTurnExecutorModel`:

```csharp
        var prompt = packFile != null
            ? PrintTurnPrompt_Builder.Build_FreshTurnPointer(requestId, packFile)
            : PrintTurnPrompt_Builder.Build_FollowUp(requestId, pending, alreadyExecutedTurns, sources);
```

**`packFile` is null for a resumed turn AND for a fresh turn whose pack could not be written** — and in both cases the follow-up carries the entries, which is exactly right: a session with no pack must still be told what woke it. That is the fallback working, not a bug.

- [ ] **Step 4: Run the test and watch it pass**

Run: `dotnet test AIOrchestratorCoreLib.Tests/AIOrchestratorCoreLib.Tests.csproj --filter "FullyQualifiedName~StreamSessionGetsItsPackTests"`
Expected: PASS, 3 tests.

- [ ] **Step 5: Run the prompt and stream oracles**

```bash
dotnet test AIOrchestratorCoreLib.Tests/AIOrchestratorCoreLib.Tests.csproj \
  --filter "FullyQualifiedName~PrintTurnPromptBuilderTests|FullyQualifiedName~StreamTurnDispatcherTests|FullyQualifiedName~PrintTurnEntrySplitterTests"
```
Expected: PASS.

- [ ] **Step 6: Commit**

```bash
git add AIOrchestratorCoreLib/Running/PrintTurnPrompt_Builder.cs AIOrchestratorCoreLib/Running/TurnExecutor/StreamTurnExecutorModel.cs AIOrchestratorCoreLib.Tests/Running/StatePack/StreamSessionGetsItsPackTests.cs
git commit -F /tmp/cm.txt   # "perf(running): a fresh stream turn is pointed at its pack instead of being sent its entries twice"
```

---

## Task 5: The supervisor's conclusions get a home, and the pack carries them

**Files:**
- Modify: `AIOrchestratorCoreLib/Running/StatePack/StatePack_Locator.cs`
- Modify: `AIOrchestratorCoreLib/Running/StatePack/StatePackInputs.cs`, `StatePackInputs_Reader.cs`, `StatePack_Builder.cs`
- Modify: `kit/skills/supervisor/SKILL.md`
- Test: `AIOrchestratorCoreLib.Tests/Running/StatePack/SupervisorConclusionsTests.cs`

**Interfaces:**
- Produces: `StatePack_Locator.Get_ConclusionsFile_OrNull(paths, role, orchId, memberId)`, `StatePackInputs.Conclusions`, `StatePack_Builder.CONCLUSIONS_HEADING` / `CONCLUSIONS_CAP`. Task 6's gate reads the locator.

**This is the task the plan exists for.** The pack's seven existing sections are all **facts re-derivable from disk** — brief, last report, ledger, git, owner tail, pending entries, progress note. Delete the transcript and every one survives. The conclusions do not, and today the supervisor has nowhere to put them: `StatePack_Locator.Get_ProgressFile_OrNull` returns **null for `SessionRoles.Supervisor`** by design, because the supervisor has no member folder.

**Where it goes:** `<orch>/.supervisor.state.md`, mirroring the pack's own `.supervisor.pack.md` in the same folder for the same reason. For members and the solo the same accessor returns `<orch>/<member>/state.md` — **the C1.2 file** — so when P3's plan ships, the two halves meet at one path and one reader, not two. The general supervisor gets null: it is stateless across launches by owner directive (decision 8).

**Which end is kept when it outgrows its cap, and why it is the head.** A dead end is written once and stays true for ever; recency does not make it more relevant, and the OLDEST conclusions are the ones furthest from anything else on disk — the newest are still half-visible in the last few channel entries. So the head is kept, the tail is truncated with the usual visible marker, and the overflow is **said out loud in the pack** so the supervisor can compact its own file. The supervisor is the only reader who can judge which dead end is still live; the app must not choose for it. (This is the opposite of `progress.md`'s `keepTail: true`, deliberately — that file is "where I got to", whose last line is the point.)

- [ ] **Step 1: Write the failing test**

```csharp
// AIOrchestratorCoreLib.Tests/Running/StatePack/SupervisorConclusionsTests.cs
using AIOrchestratorCoreLib.Channels;
using AIOrchestratorCoreLib.Channels.ChannelEntry;
using AIOrchestratorCoreLib.Running;
using AIOrchestratorCoreLib.Running.PendingTraffic;
using AIOrchestratorCoreLib.Running.StatePack;
using AIOrchestratorCoreLib.Running.TurnSource;
using Xunit;

namespace AIOrchestratorCoreLib.Tests.Running.StatePack;

/// <summary>
/// THE ONE THING A FRESH SESSION CANNOT RE-DERIVE. Every other section of the pack is a fact read
/// from disk — the brief is an entry, the ledger is PLAN.md, the code state is git — so throwing the
/// transcript away costs none of them. A CONCLUSION is not on disk anywhere: "we tried this route on
/// the 13th and it cannot work" was reasoned once, acted on, and never written down. A fresh
/// supervisor re-proposes the dead end, an implementer spends a day on it again, and nothing errors
/// — which is why this section exists and why <see cref="FreshSupervisor_Gate"/> refuses freshness
/// while it is empty.
/// </summary>
public class SupervisorConclusionsTests
{
    [Fact]
    public void The_supervisor_has_a_conclusions_file_beside_its_pack()
    {
        var paths = TestPaths.Create();

        var file = StatePack_Locator.Get_ConclusionsFile_OrNull(paths, SessionRoles.Supervisor, "repo-1", "supervisor");

        Assert.NotNull(file);
        Assert.Equal(Path.Combine(paths.Get_OrchestrationFolder("repo-1"), ".supervisor.state.md"), file);
    }

    /// <summary>A MEMBER'S IS THE C1.2 FILE, at the path the token-efficiency spec names — one accessor for both halves.</summary>
    [Fact]
    public void A_member_conclusions_file_is_the_C1_2_state_file()
    {
        var paths = TestPaths.Create();

        Assert.Equal(
            Path.Combine(paths.Get_OrchestrationFolder("repo-1"), "imp-1", "state.md"),
            StatePack_Locator.Get_ConclusionsFile_OrNull(paths, SessionRoles.Implementer, "repo-1", "imp-1"));

        // The general keeps its own CLAUDE.md as memory and is stateless across launches (decision 8).
        Assert.Null(StatePack_Locator.Get_ConclusionsFile_OrNull(paths, SessionRoles.General, "general", "general"));
    }

    [Fact]
    public void The_pack_carries_the_conclusions_before_the_pending_entries()
    {
        var pack = Build(conclusions: "dead ends:\n- the fence route (2026-09-13): the tailer re-anchors first, it cannot work");

        Assert.Contains(StatePack_Builder.CONCLUSIONS_HEADING, pack);
        Assert.Contains("the tailer re-anchors first", pack);

        // ORDER IS PINNED, cache-first: stable material before the trigger (spec C2). The conclusions
        // change about once a day; the entries change every turn.
        Assert.True(
            pack.IndexOf(StatePack_Builder.CONCLUSIONS_HEADING, StringComparison.Ordinal)
            < pack.IndexOf(StatePack_Builder.PENDING_HEADING, StringComparison.Ordinal));
    }

    /// <summary>
    /// THE HEAD SURVIVES, not the tail — the opposite of the progress note, on purpose. The oldest
    /// conclusions are the ones nothing else on disk still remembers; the newest are still visible in
    /// the last few channel entries.
    /// </summary>
    [Fact]
    public void An_outgrown_conclusions_file_keeps_its_oldest_entries_and_says_it_was_cut()
    {
        var pack = Build(conclusions: "OLDEST DEAD END\n" + new string('x', StatePack_Builder.CONCLUSIONS_CAP) + "\nNEWEST DEAD END");

        Assert.Contains("OLDEST DEAD END", pack);
        Assert.DoesNotContain("NEWEST DEAD END", pack);
        Assert.Contains("characters truncated by the bridge", pack);
    }

    [Fact]
    public void Without_a_conclusions_file_there_is_no_section()
    {
        Assert.DoesNotContain(StatePack_Builder.CONCLUSIONS_HEADING, Build(conclusions: null));
    }

    static string Build(string? conclusions)
    {
        var source = new StubSource("owner", "/x/owner-channel.md", true);
        var woke = Entry(9, ChannelAuthors.Owner, "how is it going", "?");

        return StatePack_Builder.Build(new StatePackInputs(
            "repo-1", "supervisor", SessionRoles.Supervisor, "repo-1/supervisor/4",
            [new PendingEntry(source, woke)], [source],
            brief: null, lastOwnEntry: null, ledgerLines: [], planText: "# PLAN\n- [ ] open",
            gitLines: [], ownerTail: [], unavailable: [], progressNote: null, conclusions: conclusions));
    }

    static IChannelEntry Entry(int index, ChannelAuthors author, string subject, string body)
    {
        var word = ChannelAuthor_Words.Get_Word(author);
        return ChannelEntry_Factory.Create(index, author, "2026-09-15 10:00", subject, body,
            $"## [{index}] FROM {word} — 2026-09-15 10:00 — {subject}\n\n{body}");
    }

    sealed class StubSource(string key, string path, bool isOwner) : ITurnSource
    {
        public string Key => key;
        public string ChannelFilePath => path;
        public bool IsOwnerChannel => isOwner;
    }
}
```

> **Note for the implementer:** `TestPaths.Create()` is a placeholder — **read `PrintRunnerTestHarness`'s own paths setup and `StatePackInputsReaderTests` first** and use whatever those already do for a temp supervision root. Do not add a parallel helper. `StatePackInputs`'s constructor gains `conclusions` as the LAST parameter with a default of null, so no existing call site changes; `StatePackBuilderTests.Inputs(...)` then needs no edit, which is the point.

- [ ] **Step 2: Run it and watch it fail**

Run: `dotnet test AIOrchestratorCoreLib.Tests/AIOrchestratorCoreLib.Tests.csproj --filter "FullyQualifiedName~SupervisorConclusionsTests"`
Expected: compile error — `Get_ConclusionsFile_OrNull`, `CONCLUSIONS_HEADING` and the `conclusions` parameter do not exist.

- [ ] **Step 3: The locator**

```csharp
    /// <summary>The file name the C1.2 block is stored under for a member — spec 2026-09-08, §C1.2.</summary>
    public const string CONCLUSIONS_FILE_NAME = "state.md";

    /// <summary>The supervisor's, beside its pack, because the supervisor has no member folder.</summary>
    public const string SUPERVISOR_CONCLUSIONS_FILE_NAME = ".supervisor.state.md";

    /// <summary>
    /// WHERE A SESSION'S CONCLUSIONS LIVE — the one part of a turn that a fresh session cannot
    /// re-derive. The brief is an entry, the ledger is PLAN.md, the code state is git: throw the
    /// transcript away and every one of them is still on disk. *"We already tried this and it
    /// failed"* is not on disk anywhere, and re-proposing a dead end costs a day and reports nothing.
    ///
    /// <para>
    /// Null for the general supervisor, which is stateless across launches by owner directive
    /// (CLAUDE.md decision 8) and keeps its memory in its own CLAUDE.md.
    /// </para>
    /// </summary>
    public static string? Get_ConclusionsFile_OrNull(ISupervisionPaths paths, SessionRoles role, string orchId, string memberId)
    {
        return role switch
        {
            SessionRoles.General => null,
            SessionRoles.Supervisor => Path.Combine(paths.Get_OrchestrationFolder(orchId), SUPERVISOR_CONCLUSIONS_FILE_NAME),
            _ => Path.Combine(paths.Get_OrchestrationFolder(orchId), memberId, CONCLUSIONS_FILE_NAME),
        };
    }
```

- [ ] **Step 4: The input, the read and the section**

`StatePackInputs`: add `string? conclusions = null` as the last constructor parameter and

```csharp
    /// <summary>
    /// What this session concluded and wrote down — <see cref="StatePack_Locator.Get_ConclusionsFile_OrNull"/>.
    /// The ONLY section of this pack that is not re-derivable from the channel, the ledger or git,
    /// and therefore the only one whose absence is a loss rather than an inconvenience.
    /// </summary>
    public string? Conclusions { get; } = conclusions;
```

`StatePackInputs_Reader.Read`: read it exactly the way `Read_ProgressNote_OrNull` reads its file — guarded, empty-as-null, failures named in `Unavailable` — and pass it through. **Reuse that method rather than writing a second one**: rename it `Read_Note_OrNull(string? file, string label, List<string> unavailable)` and call it twice.

`StatePack_Builder`:

```csharp
    public const int CONCLUSIONS_CAP = 8_000;

    public const string CONCLUSIONS_HEADING =
        "## What you concluded earlier — state.md, YOUR OWN judgements (not facts the bridge read)";
```

and, placed **after the progress note and before the ledger** — stable-before-volatile, and it is the section a fresh session must read before it decides anything:

```csharp
        if (inputs.Conclusions != null)
            Append_Block(text, CONCLUSIONS_HEADING, inputs.Conclusions, CONCLUSIONS_CAP, StatePack_Locator.CONCLUSIONS_FILE_NAME);
```

**No `keepTail`** — see the reasoning above, and put it in the constant's docstring so the next reader does not "fix" it to match `progress.md`.

- [ ] **Step 5: Tell the supervisor to keep the file**

In `kit/skills/supervisor/SKILL.md`, beside the existing pack paragraph (line 73 on this branch: *"Fresh start? Look for your PACK first"*), add the other half:

```markdown
**Your conclusions do not survive your turn — write them down.** Keep
`${AIORCH_SUPERVISION_ROOT:-$HOME/.claude/supervision}/$AIORCH_ID/.supervisor.state.md`, append-only,
and add a line whenever you conclude something the channel does not already say:

- a **dead end** — a route tried and ruled out, with the date and the reason it cannot work;
- a **ruling** you made that you do not want re-litigated (a finding rejected, a scope call);
- something the **owner decided** that is not in PLAN.md.

Not status, not what you are doing, not what is in the ledger — the bridge reads all of those itself
and hands them back to you. This file is only for what it cannot: your judgements. Your next turn may
be a session with no memory of this one; this file and the pack are what it will have.
```

- [ ] **Step 6: Run the test and watch it pass, then the pack oracles**

```bash
dotnet test AIOrchestratorCoreLib.Tests/AIOrchestratorCoreLib.Tests.csproj --filter "FullyQualifiedName~SupervisorConclusionsTests"
dotnet test AIOrchestratorCoreLib.Tests/AIOrchestratorCoreLib.Tests.csproj --filter "FullyQualifiedName~StatePack"
```
Expected: PASS, 5 new + the existing 7 builder / reader / brief-finder cases **unedited**. `StatePackBuilderTests.Build_ForAMember_PutsStableSectionsFirstAndThePendingEntriesLast_Verbatim` pins the order and must still pass without touching it.

- [ ] **Step 7: Commit**

```bash
git add AIOrchestratorCoreLib/Running/StatePack/ kit/skills/supervisor/SKILL.md AIOrchestratorCoreLib.Tests/Running/StatePack/SupervisorConclusionsTests.cs
git commit -F /tmp/cm.txt   # "feat(statepack): the supervisor's conclusions get a file, and the pack hands them back"
```

---

## Task 6: The app refuses to make a session fresh while its conclusions are empty

**Files:**
- Create: `AIOrchestratorCoreLib/Running/StatePack/FreshSupervisor_Gate.cs`
- Modify: `AIOrchestratorCoreLib/Running/PrintTurnDispatcher/PrintTurnDispatcherModel.cs:1374`
- Test: `AIOrchestratorCoreLib.Tests/Running/StatePack/FreshSupervisorGateTests.cs`

**Interfaces:**
- Consumes: `StatePack_Locator.Get_ConclusionsFile_OrNull` (Task 5), `ChannelAppender.Append_AppEntry`.
- Produces: `FreshSupervisor_Gate.Allows(paths, state, log)`.

**The rule:** a supervisor or solo whose role config says `fresh` but whose conclusions file is **missing or empty** is started on its transcript anyway, and told once, in its own channel, why. Members are untouched — the channel is their durable state by design and their brief is re-derivable.

**Why in the app and not in the skill (decision 21):** a hook advises and a session can unwire it; the flip's failure mode is silent, so the restraint has to be at the point of effect. And why *refuse* rather than *go fresh and alert*: an alert about memory that has already been thrown away is a post-mortem. **This is OQ3 — the recommendation is implemented here and the owner may overrule it.**

**The alert is `AppEntryAudiences.Agent`, never `Owner`** — it goes in the channel and rides the supervisor's next turn (`PrintTurn_Trigger.Select_AgentNotes`), and never to Telegram. The supervisor is the one who can act on it by writing the file; the owner cannot (decision 15).

- [ ] **Step 1: Write the failing test**

```csharp
// AIOrchestratorCoreLib.Tests/Running/StatePack/FreshSupervisorGateTests.cs
using AIOrchestratorCoreLib.Channels;
using AIOrchestratorCoreLib.Running;
using AIOrchestratorCoreLib.Running.StatePack;
using Xunit;

namespace AIOrchestratorCoreLib.Tests.Running.StatePack;

/// <summary>
/// THE BRAKE ON THE ONE-WAY DOOR. Going fresh throws away the supervisor's conversation, and the
/// conversation is the only place its conclusions live until it has written them down. A supervisor
/// flipped to <c>fresh</c> with an empty conclusions file loses them silently: nothing errors, the
/// turns keep coming, and a dead end is re-proposed a week later at the cost of somebody's day.
/// </summary>
public class FreshSupervisorGateTests
{
    [Fact]
    public void A_supervisor_with_no_conclusions_file_is_not_started_fresh()
    {
        using var harness = GateHarness.Create();

        Assert.False(FreshSupervisor_Gate.Allows(harness.Paths, harness.SupervisorState, harness.Log));
    }

    [Fact]
    public void An_empty_conclusions_file_counts_as_none()
    {
        using var harness = GateHarness.Create();
        harness.Write_Conclusions("   \n\n");

        Assert.False(FreshSupervisor_Gate.Allows(harness.Paths, harness.SupervisorState, harness.Log));
    }

    [Fact]
    public void A_supervisor_that_has_written_its_conclusions_may_go_fresh()
    {
        using var harness = GateHarness.Create();
        harness.Write_Conclusions("dead ends:\n- the fence route (2026-09-13): the tailer re-anchors first");

        Assert.True(FreshSupervisor_Gate.Allows(harness.Paths, harness.SupervisorState, harness.Log));
    }

    /// <summary>
    /// SAID ONCE, and to the AGENT. A refusal repeated every tick is the waterfall this system exists
    /// to prevent (decision 14), and an alert the owner cannot act on does not go to the phone
    /// (decision 15) — the supervisor is the one who can fix this, by writing the file.
    /// </summary>
    [Fact]
    public void The_refusal_is_filed_once_in_the_channel_and_never_texted()
    {
        using var harness = GateHarness.Create();

        FreshSupervisor_Gate.Allows(harness.Paths, harness.SupervisorState, harness.Log);
        FreshSupervisor_Gate.Allows(harness.Paths, harness.SupervisorState, harness.Log);
        FreshSupervisor_Gate.Allows(harness.Paths, harness.SupervisorState, harness.Log);

        var entries = ChannelEntry_Parser.Parse_All(File.ReadAllText(harness.Paths.Get_OwnerChannelFile("repo-1")))
            .Where(entry => entry.Author == ChannelAuthors.App && entry.Subject.Contains("conclusions"))
            .ToList();

        var only = Assert.Single(entries);
        Assert.Equal(AppEntryAudiences.Agent, AppEntryAudience_Tag.Read(only.Subject));
    }

    /// <summary>A MEMBER IS NEVER GATED: its channel is its durable state by design, and its brief is in it.</summary>
    [Fact]
    public void A_member_is_not_gated()
    {
        using var harness = GateHarness.Create();

        Assert.True(FreshSupervisor_Gate.Allows(harness.Paths, harness.ImplementerState, harness.Log));
    }
}
```

> **Note for the implementer:** `GateHarness` is yours to write in `TestSupport/` **only if nothing existing fits** — read `PrintRunnerTestHarness` first; a thin wrapper over its temp root, paths and log is what is wanted, not a new one. `AppEntryAudience_Tag.Read` may be named differently — read `AIOrchestratorCoreLib/Channels/AppEntryAudiences.cs` and `AppEntryAudience_Tag` and assert with what is actually there.

- [ ] **Step 2: Run it and watch it fail**

Run: `dotnet test AIOrchestratorCoreLib.Tests/AIOrchestratorCoreLib.Tests.csproj --filter "FullyQualifiedName~FreshSupervisorGateTests"`
Expected: compile error — `FreshSupervisor_Gate` does not exist.

- [ ] **Step 3: Write the gate**

```csharp
namespace AIOrchestratorCoreLib.Running.StatePack;

/// <summary>
/// MAY THIS SESSION BE STARTED WITH NO MEMORY OF ITS OWN? For a member, always: its channel is its
/// durable state by design (CLAUDE.md decision 8) and its brief is an entry in it. For a SUPERVISOR
/// or a SOLO, only once it has somewhere to have put its conclusions.
///
/// <para>
/// WHY THE APP AND NOT A HOOK (decision 21). Every other section of the state pack is a fact read
/// from disk and survives the transcript; a conclusion — *"we tried that route and it cannot work"* —
/// survives nothing. The failure is silent: a fresh supervisor with no conclusions file re-proposes a
/// dead end, an implementer spends a day on it, and no test is red and no alert fires. A hook can
/// advise about that; only the app can decline to do it.
/// </para>
/// <para>
/// IT REFUSES RATHER THAN WARNING AFTER THE FACT. An alert about memory that has already been thrown
/// away is a post-mortem. The cost of refusing is that the session keeps its transcript for another
/// turn, which is exactly today's behaviour.
/// </para>
/// </summary>
public static class FreshSupervisor_Gate
{
    static readonly HashSet<string> _told = [];
    static readonly object _lock = new();

    public static bool Allows(ISupervisionPaths paths, IPrintSessionState state, IOrchestrationLog log)
    {
        if (state.Role is not (SessionRoles.Supervisor or SessionRoles.Solo))
            return true;

        var file = StatePack_Locator.Get_ConclusionsFile_OrNull(paths, state.Role, state.OrchId, state.MemberId);

        if (file != null && Has_Content(file))
            return true;

        Say_Once(paths, state, log, file);
        return false;
    }

    /// <summary>
    /// Unreadable counts as EMPTY, deliberately: a gate that cannot evaluate its predicate must not
    /// invent a permission it cannot justify (decision 21). The refusal keeps today's behaviour, and
    /// the line below names which predicate failed rather than saying "gate error".
    /// </summary>
    static bool Has_Content(string file)
    {
        try
        {
            return File.Exists(file) && File.ReadAllText(file).Trim().Length > 0;
        }
        catch
        {
            return false;
        }
    }

    static void Say_Once(ISupervisionPaths paths, IPrintSessionState state, IOrchestrationLog log, string? file)
    {
        lock (_lock)
        {
            if (!_told.Add($"{state.OrchId}/{state.MemberId}"))
                return;
        }

        log.Log_Warning(state.OrchId, $"'{state.MemberId}': configured resume: fresh, but {file ?? "its conclusions file"} is missing or empty — kept on its transcript, because going fresh would drop everything it has concluded and nothing would say so");

        ChannelAppender.Append_AppEntry(
            paths.Get_OwnerChannelFile(state.OrchId),
            AppEntryAudiences.Agent,
            "your conclusions file is empty, so you are still running on your transcript",
            $"This orchestration is configured `resume: fresh`, which means each of your turns would be a new session with no memory of the last. "
            + $"The bridge hands a fresh session its brief, the ledger, the code state and the entries that woke it — but not what you have CONCLUDED. "
            + $"Write those to {file}: dead ends with their dates and reasons, rulings you do not want re-litigated, owner decisions that are not in PLAN.md. "
            + "Until then you keep your transcript, which costs tokens and is the safe side of the trade.",
            DateTime.Now);
    }
}
```

**The one-shot token is spent only when the entry was written** — follow `BudgetAlert_Planner`'s discipline, which exists because a token spent before a failed send loses the message for ever. If `Append_AppEntry` returns false, do not keep the key.

- [ ] **Step 4: Wire it at the point of effect**

`PrintTurnDispatcherModel.cs:1374`:

```csharp
        // THE GATE, not the config key alone. `fresh` is what throws the transcript away, and for the
        // two roles that own an endeavour it is only safe once their conclusions are on disk.
        var fresh = roleConfig.Resume == ResumeModes.Fresh && FreshSupervisor_Gate.Allows(_paths, state, _log);
```

- [ ] **Step 5: Run the test and watch it pass**

```bash
dotnet test AIOrchestratorCoreLib.Tests/AIOrchestratorCoreLib.Tests.csproj --filter "FullyQualifiedName~FreshSupervisorGateTests"
dotnet test AIOrchestratorCoreLib.Tests/AIOrchestratorCoreLib.Tests.csproj --filter "FullyQualifiedName~PrintTurnDispatcherTests|FullyQualifiedName~StreamTurnDispatcherTests|FullyQualifiedName~WatchdogPrintSessionTests"
```
Expected: PASS. **Watch the general supervisor cases** — `General` is the one role shipping `resume: fresh` by default and it must stay fresh: `Get_ConclusionsFile_OrNull` returns null for it, so it is not `Supervisor` or `Solo` and the gate returns true at the first line. If a general-supervisor test goes red, the role check is wrong.

- [ ] **Step 6: Commit**

```bash
git add AIOrchestratorCoreLib/Running/StatePack/FreshSupervisor_Gate.cs AIOrchestratorCoreLib/Running/PrintTurnDispatcher/PrintTurnDispatcherModel.cs AIOrchestratorCoreLib.Tests/Running/StatePack/FreshSupervisorGateTests.cs
git commit -F /tmp/cm.txt   # "feat(running): a supervisor is not made fresh until its conclusions are on disk"
```

---

## Task 7: Capture the transcript-era verdicts — BEFORE anything is flipped

**Files:**
- Create: `tools/fresh-supervisor/capture-verdicts.sh`

**Interfaces:**
- Produces: `bash tools/fresh-supervisor/capture-verdicts.sh <orch-id> <count> <out-dir>` — N supervisor verdict entries, one file each, **anonymised of their era**.

**Why it is its own task, and why here:** the spec's acceptance is *"ten verdicts read blind by the owner against ten from the transcript era show no quality regression."* Once the flip has happened the transcript era is over and its verdicts can only be recovered from channel history — which `Channel_Compactor` moves into `channel.archive.md` (decision 13: never count a live file alone). **If this is not run before Task 8, the acceptance criterion of this plan cannot be evaluated at all.** That is an ordering constraint the spec does not state.

"Blind" means Nathan must not be able to tell which arm a verdict came from. A verdict that opens *"You are a FRESH session…"* or carries a pack reference is self-identifying, so the script strips the header, the index, the date and the `STATE:` line, and writes the files under shuffled names with a key file kept **outside** the read directory.

- [ ] **Step 1: Write it**

```bash
#!/usr/bin/env bash
# TEN VERDICTS FROM THE ERA THAT IS ABOUT TO END. Run BEFORE the flip: afterwards the transcript-era
# verdicts exist only in channel history, and the compactor moves older entries out of the live file
# into channel.archive.md (CLAUDE.md decision 13) — so both are read here, as one history.
#
# Anonymised because the comparison is BLIND: the header, the index, the date and the STATE: line all
# say which era a verdict is from. The key is written OUTSIDE the directory handed to the reader.
set -eu

SUP="${AIORCH_SUPERVISION_ROOT:-$HOME/.claude/supervision}"
ORCH="${1:?usage: capture-verdicts.sh <orch-id> <count> <out-dir>}"
COUNT="${2:?}"
OUT="${3:?}"

FOLDER="$SUP/$ORCH"
LIVE="$FOLDER/owner-channel.md"
ARCHIVE="$FOLDER/owner-channel.archive.md"

# REFUSES rather than producing an empty sample (decision 20): zero verdicts is not a finding, it is
# a script that looked in the wrong place.
[ -f "$LIVE" ] || { echo "cannot check: $LIVE does not exist" >&2; exit 2; }

mkdir -p "$OUT"
key="$OUT.key.txt"; : > "$key"

# Entries authored by the supervisor, newest first, across archive + live as ONE history.
cat "$ARCHIVE" 2>/dev/null "$LIVE" \
  | awk -v n="$COUNT" '
      /^## \[[0-9]+\] FROM supervisor /  { keep = 1; body = ""; next }
      /^## \[[0-9]+\] FROM /             { if (keep && body != "") { print "\x1e" body } ; keep = 0; next }
      keep                               { body = body $0 "\n" }
      END                                { if (keep && body != "") print "\x1e" body }
    ' \
  | tail -n "$COUNT" \
  | while IFS= read -r -d $'\x1e' entry; do
      name="$(printf '%s' "$entry" | shasum | cut -c1-8)"
      printf '%s\n' "$entry" | grep -v '^STATE:' > "$OUT/$name.md"
      echo "$name transcript-era $ORCH" >> "$key"
    done

echo "captured $(find "$OUT" -name '*.md' | wc -l | tr -d ' ') verdicts into $OUT (key: $key)"
```

- [ ] **Step 2: Run it against a real orchestration and READ what came out**

```bash
bash tools/fresh-supervisor/capture-verdicts.sh <orch-id> 10 /tmp/verdicts-before
```

Then open two of them. **If a captured file still says which era it is from — a pack path, a "fresh session" phrase, a bridge-turn marker — fix the strip and run it again.** A blind test that is not blind is worse than none: it produces a confident answer to a question nobody asked.

Expect an exit of 2 on this Mac (no supervision data). That is correct; the script runs where the data is.

- [ ] **Step 3: Commit**

```bash
git add tools/fresh-supervisor/capture-verdicts.sh
git commit -F /tmp/cm.txt   # "tools(fresh-supervisor): capture the transcript-era verdicts before the era ends"
```

---

## Task 8: The flip — one orchestration, one key, with the rollback written first

**Files:**
- Modify: `docs/superpowers/plans/2026-09-15-one-wake-model-06-report.md`

**Nothing in the code changes here.** `runners.supervisor.resume` is DATA (decision: the model and the effort are both data as of 2026-09-12; `resume` always was — `RunnerConfigs_Json` parses it and `Write` emits it). The flip is an edit to `config.json` on one machine, for one role, and it is reversible by the same edit.

**Write the rollback criterion before the work** — not after, when it is a judgement about whether to admit a mistake:

> **Rolled back if, in the three days after the flip, ANY of:** a supervisor re-proposes a route its conclusions file already rules out; the owner has to restate something they had already said; `FreshSupervisor_Gate` files its refusal (which means the file went empty, so the protection was load-bearing); or `inherited_share` for the supervisor does not fall below 0.30.
>
> **Rollback is:** set `"resume": "transcript"` for `supervisor` in `config.json`, and note the date and the reason in the report. It costs one edit and the next respawn.

- [ ] **Step 1: Run the preflight and obey it**

```bash
bash tools/fresh-supervisor/preflight.sh <orch-id>; echo "exit $?"
```
**Exit 0 or stop.** An exit of 1 names the precondition to go and satisfy; an exit of 2 means the machine could not be read and nothing may be concluded from it — neither is a reason to flip anyway.

- [ ] **Step 2: Confirm the running binary carries Tasks 3–6**

Per decision 23 the running app is a fourth copy and the only one whose behaviour anyone is describing:

```bash
# On Windows, `Get-Process AIOrchestrator | Select Path` names the dll that is ACTUALLY RUNNING.
# Put that path in LIVE_DLL and search it — not the build output, not the branch source.
LIVE_DLL="$(pwd)/AIOrchestrator/bin/Debug/net10.0-windows/AIOrchestratorCoreLib.dll"
python3 -c 'import sys,pathlib
data = pathlib.Path(sys.argv[1]).read_bytes()
for name in ("TurnStatePack_Writer", "FreshSupervisor_Gate", "Build_FreshTurnPointer"):
    print(name, data.count(name.encode("utf-8")))' "$LIVE_DLL"
```
Expected: both counts ≥ 1. **A green build and a clean `git log` say nothing about what is running** — metadata names are UTF-8, string literals UTF-16LE, so an ASCII `grep` returns confident false negatives.

- [ ] **Step 3: Verify the kit against a RESTARTED session**

Task 5 edited `kit/skills/supervisor/SKILL.md`, and the installed copy is a different file that only a restart re-reads (decisions 17, 18):

```bash
grep -c "supervisor.state.md" ~/.claude/plugins/cache/aiorch-local/aiorch/*/skills/supervisor/SKILL.md
```
Expected: ≥ 1. If it is 0, the marketplace still points at a deleted directory (it did on 2026-09-15: `aiorch-local` registered at `…/AIOrchestrator-integration/kit`, absent) — re-register and reinstall, exactly as plan 01 Task 12 does, **then restart a session and grep again.**

- [ ] **Step 4: Flip one role on one orchestration**

```json
"runners": { "supervisor": { "runner": "stream", "resume": "fresh" } }
```

Respawn the supervisor. Record in the report: the date and time, the orchestration, the machine, the runner, and the `context_share.py` figure from Task 1 as the *before*.

- [ ] **Step 5: Watch the first turn, once, and say what you saw**

Read the first fresh turn's pack (`<orch>/.supervisor.pack.md`) and its entry. Not a smoke test and not an approval — one look, recorded, because this is the turn where "the pack is enough" either is or is not true, and it is cheap to find out now. **Note which copy you read.**

---

## Task 9: Three days later — the two readings, and the blind pass for Nathan

**Files:**
- Modify: `docs/superpowers/plans/2026-09-15-one-wake-model-06-report.md`

**The machine's reading is the machine's, and it is done here.** The blind comparison is a judgement about writing quality and it is **Nathan's, by standing instruction** — it is delivered as a pass executable in one step and then this plan stops. Do not queue for him anything a machine can check, and do not call the result "verified" on his behalf.

- [ ] **Step 1: The token reading**

```bash
python3 tools/wake-baseline/context_share.py --projects ~/.claude/projects --format text
```

Into the report, against the Task 1 *before*: `calls`, `mean_input_tokens`, `mean_inherited_tokens`, `inherited_share` for the supervisor.

**Expected shape, and why — so a surprise is recognisable as one.** A fresh supervisor re-runs its role command every turn, and the supervisor skill is **98 529 bytes** on this branch (measured 2026-09-15) ≈ **31–35 k tokens** at the spec's own 2.8–3.2 bytes/token, against a measured supervisor boot of 55.9–56.9 k. So mean context should land near **boot + pack ≈ 56 k + ≤ 6 k ≈ 62 k**, under the spec's 80 k bar but **not near zero** — freshness removes the inherited transcript, not the boot. If the figure comes out far below 60 k the boot is not being paid, which would mean the role command is not running and the session has no skill: that is a failure wearing a saving's clothes, and it is the specific thing to look for.

- [ ] **Step 2: Capture ten verdicts from the fresh era**

```bash
bash tools/fresh-supervisor/capture-verdicts.sh <orch-id> 10 /tmp/verdicts-after
```

- [ ] **Step 3: Assemble the blind pass and hand it over — one step, then stop**

```bash
mkdir -p /tmp/verdicts-blind && cp /tmp/verdicts-before/*.md /tmp/verdicts-after/*.md /tmp/verdicts-blind/
ls /tmp/verdicts-blind
```

To Nathan, in one message: the directory, the twenty files, and the question — *"read them in any order and mark each one good / adequate / poor; ten are from before the change and ten from after, and the key is at `/tmp/verdicts-blind.key.txt`, which you should open only once you have finished."* That is the whole ask. **The plan does not proceed past this point without his answer, and the answer is recorded verbatim in the report — his words, not a summary of them.**

- [ ] **Step 4: The acceptance, evaluated**

- `inherited_share` below 0.30 and `mean_input_tokens` below 80 000 — **machine, measured above.**
- No quality regression in the blind read — **Nathan's, recorded above.**
- Zero rollback triggers from Task 8 — **read the log and the channel; say which copy.**

Any of the three failing ⇒ roll back per Task 8, and the report says so plainly with the evidence. **A rollback here is a result, not a failure of the plan**: it means the pack is not yet enough, and it names what is missing.

---

## Task 10: The report

**Files:**
- Modify: `docs/superpowers/plans/2026-09-15-one-wake-model-06-report.md`

- [ ] **Step 1: Write it**

Sections, and none of them optional:

1. **The figures**, before and after, from `context_share.py`, with the machine and the window named, and every carried-over number still marked `[quoted]` if nobody re-derived it.
2. **The blind read**, Nathan's words verbatim, with the key.
3. **Which copies were read** at each verification — branch source, build output, installed kit, running binary (decisions 18, 23).
4. **The reds**: which tests were red, whether they were the three known flaky ones (`ClosingTurnReviewFix…`, `TolerantFileReader…`, `EffortDial…`), and for anything else, what caused it.
5. **What is still open** — the open questions below, with any that were answered marked with who answered and when.
6. **Whether it was rolled back**, and if so what was missing.

- [ ] **Step 2: Run the full suite once, from the dispatching session**

```bash
export PATH="$HOME/.dotnet:$PATH"
dotnet test AIOrchestratorCoreLib.Tests/AIOrchestratorCoreLib.Tests.csproj
```
Expected: 3 955 + the new cases passing, 10 skipped, 0 failing. Re-run any of the three flaky families alone before believing a red in them; **anything else is yours.**

- [ ] **Step 3: Commit**

```bash
git add docs/superpowers/plans/2026-09-15-one-wake-model-06-report.md
git commit -F /tmp/cm.txt   # "docs(plans): the supervisor-goes-fresh report — the two readings, the copies, and what is still open"
```

---

## Acceptance for the whole plan

- `dotnet test` green, the three known-flaky families excepted and each re-run alone before it is believed.
- `bash tools/fresh-supervisor/preflight.sh --self-test` passes, **including its exit-2 case** — verified to still fire, not assumed.
- `python3 -m pytest tools/wake-baseline/test_context_share.py -q` passes, **including its refusal case**.
- Every existing `StatePack`, dispatcher and stream oracle passes **with no edit to those files** — Task 3 is a move, not a rewrite.
- On a fresh stream supervisor: a pack exists, holds the brief/ledger/owner tail/conclusions, and the stdin message carries the pack path and **not** the entries.
- With an empty conclusions file, `resume: fresh` does **not** make the session fresh, and the refusal is filed once, in the channel, `Agent`-audience, never texted.
- `context_share.py` run before and after, both in the report, both naming their machine.
- Nathan's blind read recorded verbatim.
- The default is untouched: a machine that states nothing still gets `resume: transcript` for the supervisor.

---

## Open questions — write the answer in the report, do not decide any of these quietly

1. **OQ1 — `STATE:` is already taken, and P3 will collide with it.** `DeclaredState_Parser` reads `STATE:` as **one line, ≤ 120 characters**, last-one-wins, for the PULSE status row (owner ruling 2026-09-09). The C1.2 block is five keys and ≤ 2 KB. A block whose first line starts `STATE:` will be swallowed by that parser and truncated into the owner's status row. **Recommendation:** the block gets its own marker (`HANDOFF:`, fenced) and `STATE:` keeps its one-line meaning; the grammar file (`kit/grammar/channel-grammar.json`, which the bash tool and the app both read) gains the new word. **This is P3's plan's decision, but it determines which file Task 5's reader is pointed at, so it must be answered before P3 ships — not before this plan starts.**

2. **OQ2 — one key, two runners.** `resume` is read by the bridge (`PrintTurnDispatcherModel:1374`, `PrintTurnExecutorModel:52`) **and** by the terminal respawn path (`OrchestrationLauncherModel:600`, which resumes a terminal supervisor's own conversation only when `Resume == Transcript`). So flipping `supervisor` to `fresh` also revokes the owner's explicit 2026-09-10 request that a respawned terminal supervisor continue its own conversation — on the Windows machine, silently. **Recommendation:** Task 8 flips only where the supervisor is bridge-driven, and the terminal half waits until plan 01's ticket+pack has run its three days there. **The alternative — splitting the key into `resume.bridge` / `resume.terminal` — is more honest and more work; the owner's call.**

3. **OQ3 — refuse, or go fresh and warn?** Task 6 implements *refuse*, on the reasoning that an alert about memory already discarded is a post-mortem. The cost is that a config key the owner set does not take effect, which is its own surprise, and the app says so only in the channel. **If the owner prefers the setting always to win, the gate becomes a loud warning and this plan's risk goes back to being carried by discipline.**

4. **OQ4 — is three days of pack enough?** P2's window is the spec's, not a measured one. Three days on a quiet orchestration may be a handful of turns. **Consider restating P2 as a count of fresh turns rather than a span of days** — the preflight would then read `executed_turns` instead of an mtime. Not changed here, because the spec says days and this plan does not get to redefine its own gate.

5. **OQ5 — define "mean context" before measuring it.** *"Supervisor mean context < 80 k"* does not say per call or per turn, nor whether cache reads count. Task 1 fixes it as **per call, cache reads included in the denominator** (`inherited/(inherited+new)`), because that is what the CLI reports and what the 91 % was computed from. **If the owner or the VPS figures meant something else, the before and after do not compare and the gate is unfalsifiable.** Say which was meant.

6. **OQ6 — does anything else read the supervisor's transcript?** `/tokens`, `/cost` and the limit alerts attribute usage by the session id of the stage (spec C7), and a fresh supervisor mints a new id **every turn**. Whether `UsageTotals_Reader`'s respawn accumulator counts each of those once was **not verified in this plan** — it is named here rather than assumed, because "every source passes exactly once through the respawn accumulator" (decision 10) was written when a supervisor had one id for its life. **Check it before Task 8, or the first thing the owner notices after the flip is their cost figure moving for a reason that is not real.**

---

## What is deliberately NOT in this plan

- **The C1.2 `STATE:` block itself** (P3). It is every role's, not the supervisor's, and it needs OQ1 answered first. This plan builds only the supervisor's half of the destination and refuses to go fresh without it.
- **Plan 01** (the wake ticket, the decider, the pack on the terminal runner). P1 and P2 depend on it; it is a separate plan and this one does not duplicate any of it.
- **Spec steps 3, 4 and 5** (bookkeeping out of the channels, the routed report, the skill diet). Step 5 would cut the supervisor's boot — the 56 k this plan explicitly does **not** remove — and is orthogonal to it; it may be planned and run in parallel.
- **C1.4's stage cap**, C9's per-stage budget, and anything about `--max-budget-usd`. Named in the spec, none of them gated on this.
- **Renaming `PrintSessionState_Store`.** Still churn, still deferred, for the same reason the spec gives.
- **The merge to `master`.** Nathan's.
