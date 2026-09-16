# One Wake Model — Plan 01: the decider, the ticket, and the pack

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Move the "does this session wake now?" decision out of the bash watcher and into the app, so one policy serves both runners, and carry the digest, the riding app-notes and the state pack to terminal sessions.

**Architecture:** Extract the decision the bridge dispatcher makes inline into `WakeDecision_Resolver`. Give terminal sessions a session-state file (so they have a cursor) by splitting `IPrintSessionState`'s single meaning into `DrivesTurns` plus the cursor state. On each tick the engine asks the resolver about terminal sessions too and, when the answer is "wake", writes a **wake ticket** file the shrunken watcher polls. The whole series ships dark behind `runners.<role>.wake`.

**Tech Stack:** C# / .NET 10 (`net10.0-windows` for the app, the lib is platform-neutral), xUnit, bash (kit watcher + hooks), python3 (measurement scripts only).

**Spec:** `docs/superpowers/specs/2026-09-15-one-wake-model-design.md`

## Global Constraints

- **Coding patterns:** `AIOrchestratorCoreLib` is STRICT — interface + `Model` + `_Factory` triples, immutable types, no mutable public state. Read the suite's `CODING_PATTERNS_QUICKREF.md` conventions as already applied in `Running/TurnCursor/` and `Running/PendingTraffic/` and follow those files.
- **No channel-format change anywhere in this plan.** `ChannelAppender`, `channel-append.sh`, `Channel_Compactor` and the entry header are untouched.
- **Additive first.** Every new file and field is additive; the old watcher keeps working unchanged until Task 11 flips the gate. Nothing is deleted in this plan.
- **Back-compat on every persisted file.** A `print-session.json` written before this plan must read without error; a missing field takes the documented default.
- **Branch:** `feat/one-wake-model`, worktree `../AIOrchestrator-relations`. Stage explicit paths, never `git add -A`. Multi-line messages via `git commit -F <tempfile>`. Commits in English, `type(scope): a descriptive clause`.
- **Known reds:** the four `OrchestratorConfigFactoryTests` cases are a ruled exception (plan 01 of the fork merge); the file-lock / wall-clock family under `Bridge/` is a known flakiness campaign. Any OTHER red is yours.
- **Run the suite as** `dotnet test AIOrchestratorCoreLib.Tests/AIOrchestratorCoreLib.Tests.csproj`. Filter a single test with `--filter "FullyQualifiedName~<ClassName>"`.
- **`dotnet` is NOT on PATH on the owner's Mac.** It lives at `~/.dotnet/dotnet` (SDK 10.0.401); the lib and tests target `net10.0`, which is platform-neutral and builds on macOS. Start every shell with `export PATH="$HOME/.dotnet:$PATH"`. Measured 2026-09-15; a full run is ~3 min 20 s, 3 923 passing / 10 skipped / **0 failing** — so on this machine the two "known red" families above are currently GREEN, and any red you see is yours.

---

## File Structure

**Created:**

| file | responsibility |
|---|---|
| `tools/wake-baseline/baseline.py` | reads transcripts + registries, prints per-role aggregates. Aggregates only — never dumps a `.jsonl`. |
| `AIOrchestratorCoreLib/Running/WakeDecision/IWakeDecision.cs` | the answer: reason, pending entries, riding notes, sources |
| `AIOrchestratorCoreLib/Running/WakeDecision/WakeDecisionModel.cs` | immutable carrier |
| `AIOrchestratorCoreLib/Running/WakeDecision/WakeDecision_Factory.cs` | construction |
| `AIOrchestratorCoreLib/Running/WakeDecision/WakeDecision_Resolver.cs` | the one place the question is answered |
| `AIOrchestratorCoreLib/Running/WakeTicket/IWakeTicket.cs` | number, reason, state-pack path, stamp |
| `AIOrchestratorCoreLib/Running/WakeTicket/WakeTicketModel.cs` | immutable carrier |
| `AIOrchestratorCoreLib/Running/WakeTicket/WakeTicket_Factory.cs` | construction |
| `AIOrchestratorCoreLib/Running/WakeTicket/WakeTicket_Store.cs` | path, read, write |
| `AIOrchestratorCoreLib/Running/WakeModes.cs` | `Watcher` \| `Ticket`, with the config words |

**Modified:**

| file | change |
|---|---|
| `AIOrchestratorCoreLib/Running/PrintSessionState/IPrintSessionState.cs` | `+ bool DrivesTurns` |
| `AIOrchestratorCoreLib/Running/PrintSessionState/PrintSessionStateModel.cs` | same |
| `AIOrchestratorCoreLib/Running/PrintSessionState/PrintSessionState_Factory.cs` | same, defaulting `true` |
| `AIOrchestratorCoreLib/Running/PrintSessionState/PrintSessionState_Store.cs` | read/write the field; absent ⇒ `true`. **On-disk keys are snake_case** (`session_id`, `sources`, `next_turn_number`): the new one is `drives_turns`. |
| `AIOrchestratorCoreLib/Running/PrintTurnDispatcher/PrintTurnDispatcherModel.cs` | discovery screens on `DrivesTurns`; the inline decision calls the resolver |
| `AIOrchestratorCoreLib/Launching/OrchestrationLauncher/OrchestrationLauncherModel.cs:554` | terminal writes `DrivesTurns=false` instead of deleting |
| `AIOrchestratorCoreLib/Running/RoleRunnerConfig/*` | `+ WakeModes Wake`, default `Watcher` |
| `AIOrchestratorCoreLib/Running/RunnerConfigs/RunnerConfigs_Json.cs` | parse `wake` |
| `AIOrchestratorCoreLib/Bridge/BridgeEngine/BridgeEngineModel.cs` | per-tick ticket sweep for ticket-mode sessions |
| `kit/skills/{supervisor,implementer,reviewer,solo,general-supervisor,communicator}/reference/watcher.md` | the 10-line ticket watcher, behind the gate |
| `kit/hooks/watcher-behaviour-check.sh` | rewritten against the new script |

---

## Task 1: The baseline measurement script

**Files:**
- Create: `tools/wake-baseline/baseline.py`
- Create: `tools/wake-baseline/README.md`
- Test: `tools/wake-baseline/test_baseline.py`

**Interfaces:**
- Consumes: nothing from this plan.
- Produces: `python3 tools/wake-baseline/baseline.py --root <supervision-root> --projects <claude-projects-dir> --since <iso> --until <iso>` printing a fixed set of aggregate lines. Task 12 re-runs it to prove the series.

**Why it is Task 1:** every figure in the spec is quoted from a September document, not re-derived. Nothing in this plan can be judged until the numbers come from the machine. **Execution against the VPS is blocked on credentials; the script and its test are not.**

- [ ] **Step 1: Write the failing test**

```python
# tools/wake-baseline/test_baseline.py
import json, pathlib, subprocess, sys, tempfile

def test_counts_wakes_by_cause():
    with tempfile.TemporaryDirectory() as d:
        root = pathlib.Path(d)
        ch = root / "repo-1"; ch.mkdir()
        (ch / "owner-channel.md").write_text(
            "## [1] FROM owner — 2026-09-01 10:00 — hello\nbody\n\n"
            "## [2] FROM app — 2026-09-01 10:05 — [agent] turn_ended\nbody\n\n"
            "## [3] FROM supervisor — 2026-09-01 10:06 — ack\nbody\n")
        (ch / "imp-1").mkdir()
        (ch / "imp-1" / "channel.md").write_text(
            "## [1] FROM implementer — 2026-09-01 10:07 — TASK 1 done\nbody\n")
        out = subprocess.run(
            [sys.executable, "tools/wake-baseline/baseline.py", "--root", str(root), "--format", "json"],
            capture_output=True, text=True, check=True).stdout
        got = json.loads(out)
        assert got["entries_by_author"]["owner"] == 1
        assert got["entries_by_author"]["app"] == 1
        assert got["entries_by_author"]["implementer"] == 1
        # Under the watcher EVERY author wakes the supervisor, the app's own bookkeeping included.
        assert got["wake_causes_under_watcher"]["app"] == 1
        # Under the app's policy the bookkeeping wakes nobody. The gap is what this series buys.
        assert got["wake_causes_under_ticket"]["app"] == 0
        assert got["wakes_avoided_by_the_ticket"] == 1
```

- [ ] **Step 2: Run it and watch it fail**

Run: `python3 -m pytest tools/wake-baseline/test_baseline.py -q`
Expected: FAIL — `baseline.py` does not exist.

- [ ] **Step 3: Write the script**

```python
#!/usr/bin/env python3
"""Aggregates only. NEVER prints a whole .jsonl or a whole channel."""
import argparse, collections, json, re, sys
from pathlib import Path

HEADER = re.compile(r"^##\s*\[(\d+)\]\s*FROM\s+(\w[\w-]*)\s*—\s*(\d{4}-\d{2}-\d{2} \d{2}:\d{2})\s*—\s*(.*)$")
MEMBERS = {"implementer", "reviewer", "solo", "communicator"}

def read_headers(path):
    for line in path.read_text(errors="replace").splitlines():
        m = HEADER.match(line)
        if m:
            yield {"index": int(m.group(1)), "author": m.group(2), "stamp": m.group(3), "subject": m.group(4)}

def collect(root):
    """TWO readings of the same entries — the gap between them is the whole point.

    Under the bash watcher a channel change is a wake, so an app-authored entry wakes the supervisor
    exactly like a member's report. Under the app's policy it wakes nobody. NEITHER column may be
    seeded with a constant and then asserted to be that constant: that pins nothing (decision 20).
    """
    by_author = collections.Counter()
    watcher = collections.Counter({"owner": 0, "member": 0, "app": 0})
    ticket = collections.Counter({"owner": 0, "member": 0, "app": 0})
    for channel in sorted(root.glob("*/**/channel.md")) + sorted(root.glob("*/owner-channel.md")):
        for h in read_headers(channel):
            author = h["author"]
            by_author[author] += 1
            kind = "owner" if author == "owner" else "member" if author in MEMBERS else "app" if author == "app" else None
            if kind is None:
                continue
            watcher[kind] += 1          # the monitor fires on any change: every author counts
            if kind != "app":
                ticket[kind] += 1       # Is_Inbound excludes ChannelAuthors.App
    return {"entries_by_author": dict(by_author), "wake_causes_under_watcher": dict(watcher),
            "wake_causes_under_ticket": dict(ticket),
            "wakes_avoided_by_the_ticket": sum(watcher.values()) - sum(ticket.values())}

def main():
    p = argparse.ArgumentParser()
    p.add_argument("--root", required=True)
    p.add_argument("--format", choices=["text", "json"], default="text")
    a = p.parse_args()
    result = collect(Path(a.root))
    if a.format == "json":
        json.dump(result, sys.stdout)
    else:
        for section, values in result.items():
            # A section is a breakdown OR a single number. Cover both, and TEST the text path: a
            # json-only test stayed green while this crashed on the scalar.
            if isinstance(values, dict):
                print(f"== {section} ==")
                for k, v in sorted(values.items(), key=lambda kv: -kv[1]):
                    print(f"  {k:16s} {v}")
            else:
                print(f"== {section} == {values}")
    return 0

if __name__ == "__main__":
    sys.exit(main())
```

- [ ] **Step 4: Run it and watch it pass — and run the TEXT path by hand**

Run: `python3 -m pytest tools/wake-baseline/test_baseline.py -q`
Expected: PASS, 2 tests (the second covers `--format text`; a json-only test hid a crash on the scalar section).

Then run the command a person would actually type, against a temp root, and read the output:
`python3 tools/wake-baseline/baseline.py --root <temp>`

- [ ] **Step 5: Write the README**

```markdown
# wake-baseline

Aggregates the supervision root into the six figures the one-wake-model spec argues from.

    python3 tools/wake-baseline/baseline.py --root ~/.claude/supervision

**Never** dumps a whole channel or transcript — aggregates only. Safe to run read-only against a
live machine. Re-run after each step of the plan and compare.
```

- [ ] **Step 6: Commit**

```bash
git add tools/wake-baseline/
git commit -F /tmp/cm.txt   # "tools(baseline): the wake figures come from the machine, not from a September document"
```

---

## Task 2: The `wake` config key — the gate everything ships behind

**Files:**
- Create: `AIOrchestratorCoreLib/Running/WakeModes.cs`
- Modify: `AIOrchestratorCoreLib/Running/RoleRunnerConfig/IRoleRunnerConfig.cs`, `RoleRunnerConfigModel.cs`, `RoleRunnerConfig_Factory.cs`
- Modify: `AIOrchestratorCoreLib/Running/RunnerConfigs/RunnerConfigs_Json.cs`
- Test: `AIOrchestratorCoreLib.Tests/Running/WakeModeConfigTests.cs`

**Interfaces:**
- Produces: `WakeModes { Watcher, Ticket }`, `WakeMode_Names.WATCHER = "watcher"`, `WakeMode_Names.TICKET = "ticket"`, `WakeMode_Names.Parse_OrNull(string?)`, `WakeMode_Names.Get_Word(WakeModes)`, and `IRoleRunnerConfig.Wake`. Tasks 8, 9 and 11 read `Wake`.

- [ ] **Step 1: Write the failing test**

```csharp
// AIOrchestratorCoreLib.Tests/Running/WakeModeConfigTests.cs
using AIOrchestratorCoreLib.Running;
using AIOrchestratorCoreLib.Running.RunnerConfigs;
using Xunit;

namespace AIOrchestratorCoreLib.Tests.Running;

/// <summary>
/// THE GATE IS OFF UNTIL SOMEBODY SAYS OTHERWISE. Every machine that states nothing keeps the bash
/// watcher, so this plan can ship in pieces without changing a single live session's behaviour.
/// </summary>
public class WakeModeConfigTests
{
    [Fact]
    public void A_role_that_states_nothing_wakes_on_the_watcher()
    {
        var configs = RunnerConfigs_Json.Parse("{}");
        Assert.Equal(WakeModes.Watcher, configs.Get_ForRole(SessionRoles.Supervisor).Wake);
    }

    [Fact]
    public void The_word_ticket_selects_the_ticket()
    {
        var configs = RunnerConfigs_Json.Parse("""{"supervisor":{"runner":"terminal","wake":"ticket"}}""");
        Assert.Equal(WakeModes.Ticket, configs.Get_ForRole(SessionRoles.Supervisor).Wake);
        Assert.Equal(WakeModes.Watcher, configs.Get_ForRole(SessionRoles.Implementer).Wake);
    }

    /// <summary>
    /// AND THE REST OF THE NODE IS STILL READ. Asserting only that the mode is Watcher passes for TWO
    /// reasons — the word fell back, or nothing parses `wake` at all — and an assertion with two routes
    /// to its state pins neither (CLAUDE.md decision 20). Pin `runner` and `resume` beside it.
    /// </summary>
    [Fact]
    public void An_unknown_word_falls_back_to_the_watcher_without_costing_the_rest_of_the_node()
    {
        var configs = RunnerConfigs_Json.Parse(JsonNode.Parse("""{"runners":{"supervisor":{"runner":"print","resume":"fresh","wake":"telepathy"}}}""") as JsonObject);
        var role = configs.Get_ForRole(SessionRoles.Supervisor);

        Assert.Equal(WakeModes.Watcher, role.Wake);
        Assert.Equal(SessionRunners.Print, role.Runner);
        Assert.Equal(ResumeModes.Fresh, role.Resume);
    }

    /// <summary>AND IT SURVIVES A SAVE — see the Write note in Step 5.</summary>
    [Fact]
    public void An_explicit_wake_mode_survives_a_save_and_a_reload()
    {
        var parsed = RunnerConfigs_Json.Parse(JsonNode.Parse("""{"runners":{"supervisor":{"runner":"terminal","wake":"ticket"}}}""") as JsonObject);
        var written = new JsonObject();
        RunnerConfigs_Json.Write(written, parsed);

        Assert.Equal(WakeModes.Ticket, RunnerConfigs_Json.Parse(written).Get_ForRole(SessionRoles.Supervisor).Wake);
    }
}

> **Signature note, verified 2026-09-15:** `RunnerConfigs_Json.Parse` takes a `JsonObject?`, NOT a
> string, and the roles live under a `"runners"` wrapper. Follow `RunnerConfigsJsonTests.cs` for the
> house shape: `RunnerConfigs_Json.Parse(JsonNode.Parse(json) as JsonObject)`.
```

- [ ] **Step 2: Run it and watch it fail**

Run: `dotnet test AIOrchestratorCoreLib.Tests/AIOrchestratorCoreLib.Tests.csproj --filter "FullyQualifiedName~WakeModeConfigTests"`
Expected: compile error — `WakeModes` and `IRoleRunnerConfig.Wake` do not exist.

- [ ] **Step 3: Write `WakeModes.cs`**

```csharp
namespace AIOrchestratorCoreLib.Running;

/// <summary>
/// HOW a session learns it should take a turn. <see cref="Watcher"/>: the session's own bash monitor
/// fingerprints its channels and decides for itself — the shape the terminal runner has always had,
/// and a SECOND implementation of the wake policy (see the 2026-09-15 one-wake-model spec).
/// <see cref="Ticket"/>: the app decides, with the same policy the bridge uses, and says so by
/// writing <see cref="WakeTicket.WakeTicket_Store"/>'s file; the monitor only carries the message.
///
/// <para>
/// Orthogonal to <see cref="SessionRunners"/>: a bridge-driven session needs no ticket because the
/// app opens its turn directly, so this key is read only where the runner is
/// <see cref="SessionRunners.Terminal"/>. It is a separate key rather than a third runner word so the
/// migration can be per role and reversible in one edit.
/// </para>
/// </summary>
public enum WakeModes
{
    Watcher,
    Ticket,
}

public static class WakeMode_Names
{
    public const string WATCHER = "watcher";
    public const string TICKET = "ticket";

    public static string Get_Word(WakeModes mode)
    {
        return mode switch
        {
            WakeModes.Watcher => WATCHER,
            WakeModes.Ticket => TICKET,
            _ => throw new Exception($"Unhandled WakeModes: {mode}"),
        };
    }

    /// <summary>
    /// Null for anything this build does not know, INCLUDING a typo. The caller falls back to
    /// <see cref="WakeModes.Watcher"/>: a machine whose config names a mode this binary has never
    /// heard of must keep the behaviour it already had, never lose its wake-ups to a spelling.
    /// </summary>
    public static WakeModes? Parse_OrNull(string? word)
    {
        return word?.Trim().ToLowerInvariant() switch
        {
            WATCHER => WakeModes.Watcher,
            TICKET => WakeModes.Ticket,
            _ => null,
        };
    }
}
```

- [ ] **Step 4: Add `Wake` to the role config triple**

In `IRoleRunnerConfig.cs` add `WakeModes Wake { get; }`. In `RoleRunnerConfigModel.cs` add the primary-constructor parameter `WakeModes wake` and `public WakeModes Wake { get; } = wake;`. In `RoleRunnerConfig_Factory.cs` add a `WakeModes wake = WakeModes.Watcher` parameter to `Create` and pass `WakeModes.Watcher` from `Create_Default`.

- [ ] **Step 5: Parse the key**

In `RunnerConfigs_Json.cs`, beside the existing `resume` read, add:

```csharp
var wake = WakeMode_Names.Parse_OrNull(Read_String_OrNull(element, "wake")) ?? WakeModes.Watcher;
```

and pass `wake` into every `RoleRunnerConfig_Factory.Create(...)` call in the file. **There are THREE, not two** (counted 2026-09-15): the non-bg branch, the bg-refused-falls-back-to-terminal branch, and the bg-accepted branch. Miss one and that path silently drops the setting. Confirm with `grep -n "RoleRunnerConfig_Factory.Create(" RunnerConfigs_Json.cs` after editing.

**And `Write` must emit it too.** `RunnerConfigs_Json.Write` is called on every save (`OrchestratorConfig_Loader.cs:259`) and emitted runner, resume, permission_mode and settings only — so an explicit `"wake": "ticket"` was silently returned to the default by the app's own next save, against this file's own header promise that *"every role and every limit is written"*. Add `[WAKE_KEY] = WakeMode_Names.Get_Word(roleConfig.Wake),` beside the `RESUME_KEY` line. The whole series ships behind this key: "reversible in one edit" is false if a save undoes the edit.

- [ ] **Step 6: Run the test and watch it pass**

Run: `dotnet test AIOrchestratorCoreLib.Tests/AIOrchestratorCoreLib.Tests.csproj --filter "FullyQualifiedName~WakeModeConfigTests"`
Expected: PASS, 3 tests.

- [ ] **Step 7: Run the whole suite**

Run: `dotnet test AIOrchestratorCoreLib.Tests/AIOrchestratorCoreLib.Tests.csproj`
Expected: green apart from the two known-red families in Global Constraints.

- [ ] **Step 8: Commit**

```bash
git add AIOrchestratorCoreLib/Running/WakeModes.cs AIOrchestratorCoreLib/Running/RoleRunnerConfig/ AIOrchestratorCoreLib/Running/RunnerConfigs/RunnerConfigs_Json.cs AIOrchestratorCoreLib.Tests/Running/WakeModeConfigTests.cs
git commit -F /tmp/cm.txt   # "feat(running): a wake mode per role, defaulting to the watcher every machine already has"
```

---

## Task 3: `DrivesTurns` — split the state file's single meaning

**Files:**
- Modify: `AIOrchestratorCoreLib/Running/PrintSessionState/IPrintSessionState.cs`, `PrintSessionStateModel.cs`, `PrintSessionState_Factory.cs`
- Modify: `AIOrchestratorCoreLib/Running/PrintSessionState_Store.cs`
- Test: `AIOrchestratorCoreLib.Tests/Running/DrivesTurnsBackCompatTests.cs`

**Interfaces:**
- Produces: `IPrintSessionState.DrivesTurns` (bool). Task 4 screens on it, Task 5 writes it false, Tasks 8–9 read the state of terminal sessions through it.

**Why:** today the file's existence means "the dispatcher runs this session's turns", which is why a terminal spawn deletes it (`OrchestrationLauncherModel.cs:554`). A terminal session needs the file for its cursor. One field separates the two meanings.

- [ ] **Step 1: Write the failing test**

```csharp
// AIOrchestratorCoreLib.Tests/Running/DrivesTurnsBackCompatTests.cs
using System.IO;
using AIOrchestratorCoreLib.Running;
using AIOrchestratorCoreLib.Running.PrintSessionState;
using AIOrchestratorCoreLib.Running.TurnCursor;
using Xunit;

namespace AIOrchestratorCoreLib.Tests.Running;

/// <summary>
/// A STATE FILE WRITTEN BEFORE THIS FIELD EXISTED MEANS "the dispatcher runs my turns" — that was the
/// file's whole meaning. Reading it as false would silently stop every bridge session on the VPS at
/// the first deploy, which is the one failure this plan must not have.
/// </summary>
public class DrivesTurnsBackCompatTests
{
    [Fact]
    public void A_file_without_the_field_reads_as_driving_turns()
    {
        var file = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName() + ".json");
        File.WriteAllText(file, """
        {"sessionId":"11111111-1111-1111-1111-111111111111","sessionStarted":true,"role":"implementer",
         "orchId":"repo-1","memberId":"imp-1","workingDirectory":"/tmp","channelFilePath":"/tmp/channel.md",
         "cursors":[],"nextTurnNumber":1,"failedAttempts":0,"executedTurns":[]}
        """);

        var state = PrintSessionState_Store.Read_OrNull(file);

        Assert.NotNull(state);
        Assert.True(state!.DrivesTurns);
        File.Delete(file);
    }

    [Fact]
    public void A_session_that_does_not_drive_turns_round_trips()
    {
        var file = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName() + ".json");
        var written = PrintSessionState_Factory.Create(
            sessionId: "22222222-2222-2222-2222-222222222222", sessionStarted: false,
            role: SessionRoles.Supervisor, orchId: "repo-1", memberId: "supervisor",
            workingDirectory: "/tmp", model: null, channelFilePath: "/tmp/owner-channel.md",
            cursors: new List<ITurnCursor>(), nextTurnNumber: 1, failedAttempts: 0,
            retryNotBeforeUtc: null, executedTurns: [], drivesTurns: false);

        PrintSessionState_Store.Write(file, written);
        var read = PrintSessionState_Store.Read_OrNull(file);

        Assert.NotNull(read);
        Assert.False(read!.DrivesTurns);
        File.Delete(file);
    }
}
```

> **Note for the implementer:** the exact parameter list of `PrintSessionState_Factory.Create` is whatever the file already has — read it and append `drivesTurns` as the LAST parameter with a default of `true`, so no existing call site changes. If the factory takes a different shape (e.g. an options object), follow that shape instead and adjust this test to match before running it.

- [ ] **Step 2: Run it and watch it fail**

Run: `dotnet test AIOrchestratorCoreLib.Tests/AIOrchestratorCoreLib.Tests.csproj --filter "FullyQualifiedName~DrivesTurnsBackCompatTests"`
Expected: compile error — `DrivesTurns` / `drivesTurns` do not exist.

- [ ] **Step 3: Add the field to the triple**

`IPrintSessionState.cs`:

```csharp
    /// <summary>
    /// Whether the DISPATCHER runs this session's turns. True for every bridge-driven session — and
    /// for every state file written before this field existed, which is why its absence reads as true
    /// (<c>PrintSessionState_Store</c>).
    ///
    /// <para>
    /// FALSE IS NOT "CLOSED" AND NOT "DEAD": it is a session in a terminal, which takes its own turns
    /// and needs this file only for its cursors and its state pack. Before the 2026-09-15 one-wake-model
    /// change the file's existence WAS the "dispatcher runs this" flag, so a terminal spawn deleted it
    /// (<c>OrchestrationLauncherModel</c>) and a terminal session had no cursor at all — which is why
    /// the digest, the riding app-notes and the pack never reached it.
    /// </para>
    /// </summary>
    bool DrivesTurns { get; }
```

Mirror it in `PrintSessionStateModel.cs` and add `bool drivesTurns = true` as the last parameter of `PrintSessionState_Factory.Create`.

**AND EVERY `CreateFrom_Existing_*` MUST FORWARD IT — there are TEN, and the default makes omission
silent.** Pass it NAMED (`drivesTurns: source.DrivesTurns`), never positionally: three of these end at
`source.ExecutedTurns`, where a positional argument lands in the optional `retryNotBeforeUtc` slot
instead. This is not hypothetical — `CreateFrom_Existing_Cursors` is what Task 8's sweep calls to
advance a terminal session's cursor, so without this the FIRST wake ticket puts that session back
under the dispatcher while it still has a window: a window and headless turns answering one brief,
the exact failure the launcher's delete existed to prevent. Pin it with a census test that asserts
how many `CreateFrom_Existing_*` methods exist, so an eleventh cannot be added silently.

- [ ] **Step 4: Read and write it in the store**

In `PrintSessionState_Store.Read_OrNull`, beside the other reads:

```csharp
var drivesTurns = root.TryGetProperty("drivesTurns", out var drives) && drives.ValueKind == JsonValueKind.False ? false : true;
```

In `Write`, emit `"drivesTurns": state.DrivesTurns` alongside the existing properties.

- [ ] **Step 5: Run the test and watch it pass**

Run: `dotnet test AIOrchestratorCoreLib.Tests/AIOrchestratorCoreLib.Tests.csproj --filter "FullyQualifiedName~DrivesTurnsBackCompatTests"`
Expected: PASS, 2 tests.

- [ ] **Step 6: Run `PrintSessionStateStoreTests` — the existing oracle**

Run: `dotnet test AIOrchestratorCoreLib.Tests/AIOrchestratorCoreLib.Tests.csproj --filter "FullyQualifiedName~PrintSessionStateStoreTests"`
Expected: PASS, unchanged.

- [ ] **Step 7: Commit**

```bash
git add AIOrchestratorCoreLib/Running/PrintSessionState/ AIOrchestratorCoreLib/Running/PrintSessionState_Store.cs AIOrchestratorCoreLib.Tests/Running/DrivesTurnsBackCompatTests.cs
git commit -F /tmp/cm.txt   # "feat(running): a session state file says whether the dispatcher drives it, separately from existing"
```

---

## Task 4: The dispatcher screens on `DrivesTurns`

**Files:**
- Modify: `AIOrchestratorCoreLib/Running/PrintTurnDispatcher/PrintTurnDispatcherModel.cs:719` (`Is_BridgeDriven` gate) and `Discover_RegisteredSessions`
- Test: `AIOrchestratorCoreLib.Tests/Running/DispatcherIgnoresNonDrivingSessionsTests.cs`

**Interfaces:**
- Consumes: `IPrintSessionState.DrivesTurns` (Task 3).
- Produces: nothing new. This is the safety interlock that lets Task 5 write state files for terminal sessions without the dispatcher trying to run their turns.

- [ ] **Step 1: Write the failing test**

```csharp
// AIOrchestratorCoreLib.Tests/Running/DispatcherIgnoresNonDrivingSessionsTests.cs
using AIOrchestratorCoreLib.Running;
using AIOrchestratorCoreLib.Running.PrintSessionState;
using Xunit;

namespace AIOrchestratorCoreLib.Tests.Running;

/// <summary>
/// THE INTERLOCK OF THE ONE-WAKE-MODEL SERIES. From Task 5 a terminal session keeps a state file, and
/// the file used to mean "run my turns". If discovery did not screen on <c>DrivesTurns</c> the member
/// would have a window AND headless turns answering the same brief — the exact failure the launcher's
/// delete existed to prevent (<c>OrchestrationLauncherModel.cs:554</c>).
/// </summary>
public class DispatcherIgnoresNonDrivingSessionsTests
{
    [Fact]
    public void A_registered_session_that_does_not_drive_turns_is_not_dispatched()
    {
        using var harness = PrintRunnerTestHarness.Create();
        harness.Register_Session(SessionRoles.Implementer, "repo-1", "imp-1", drivesTurns: false);
        harness.Append_SupervisorEntry("repo-1", "imp-1", subject: "TASK 1 — build the thing");

        harness.Tick();

        Assert.Empty(harness.StartedTurns);
    }

    [Fact]
    public void A_registered_session_that_drives_turns_is_dispatched_as_before()
    {
        using var harness = PrintRunnerTestHarness.Create();
        harness.Register_Session(SessionRoles.Implementer, "repo-1", "imp-1", drivesTurns: true);
        harness.Append_SupervisorEntry("repo-1", "imp-1", subject: "TASK 1 — build the thing");

        harness.Tick();

        Assert.Single(harness.StartedTurns);
    }
}
```

> **Note for the implementer:** `PrintRunnerTestHarness` already exists at `AIOrchestratorCoreLib.Tests/Running/PrintRunnerTestHarness.cs`. Read it first and use its real member names; add a `drivesTurns` parameter to its `Register_Session` (defaulting `true`) rather than inventing a new harness.

- [ ] **Step 2: Run it and watch it fail**

Run: `dotnet test AIOrchestratorCoreLib.Tests/AIOrchestratorCoreLib.Tests.csproj --filter "FullyQualifiedName~DispatcherIgnoresNonDrivingSessionsTests"`
Expected: FAIL — the non-driving session is dispatched, `StartedTurns` has one entry.

- [ ] **Step 3: Screen in discovery**

**NOT in the discovery predicate — the plan was wrong here, corrected 2026-09-15 while executing.**
That predicate screens on the ROLE's configured runner from config.json, while `DrivesTurns` is a
per-SESSION flag on the state file. A member demoted to a terminal (Task 5) does not change
config.json, so the role can still read `print` while that member's own file says its turns are no
longer the dispatcher's — and the discovery screen would miss it, which is the failure this task
exists to prevent.

Put it in `Consider_Session`, immediately after the state is read and before the sources are resolved:

```csharp
        if (!state.DrivesTurns)
            return;
```

- [ ] **Step 4: Run the test and watch it pass**

Run: `dotnet test AIOrchestratorCoreLib.Tests/AIOrchestratorCoreLib.Tests.csproj --filter "FullyQualifiedName~DispatcherIgnoresNonDrivingSessionsTests"`
Expected: PASS, 2 tests.

- [ ] **Step 5: Run every dispatcher test**

Run: `dotnet test AIOrchestratorCoreLib.Tests/AIOrchestratorCoreLib.Tests.csproj --filter "FullyQualifiedName~Running"`
Expected: green apart from the known reds.

- [ ] **Step 6: Commit**

```bash
git add AIOrchestratorCoreLib/Running/PrintTurnDispatcher/PrintTurnDispatcherModel.cs AIOrchestratorCoreLib.Tests/Running/
git commit -F /tmp/cm.txt   # "fix(running): the dispatcher runs the turns of sessions that say it drives them, not of every registered one"
```

---

## Task 5: A terminal spawn keeps its state instead of deleting it

**Files:**
- Modify: `AIOrchestratorCoreLib/Launching/OrchestrationLauncher/OrchestrationLauncherModel.cs:554`
- Test: `AIOrchestratorCoreLib.Tests/Launching/TerminalSpawnKeepsItsCursorTests.cs`

**Interfaces:**
- Consumes: `PrintSessionState_Factory.Create(..., drivesTurns: false)` (Task 3), the dispatcher interlock (Task 4).
- Produces: a state file present for terminal sessions, which Tasks 8 and 9 read.

- [ ] **Step 1: Write the failing test**

```csharp
// AIOrchestratorCoreLib.Tests/Launching/TerminalSpawnKeepsItsCursorTests.cs
using AIOrchestratorCoreLib.Running;
using AIOrchestratorCoreLib.Running.PrintSessionState;
using Xunit;

namespace AIOrchestratorCoreLib.Tests.Launching;

/// <summary>
/// A TERMINAL SESSION NEEDS A CURSOR, and until 2026-09-15 it deliberately had none: the launcher
/// DELETED its state file, because that file's only meaning was "the dispatcher runs my turns".
/// Task 3 gave the file a second meaning and Task 4 made the first one explicit, so the delete
/// becomes a write.
/// </summary>
public class TerminalSpawnKeepsItsCursorTests
{
    [Fact]
    public void Spawning_a_terminal_session_leaves_a_state_file_that_does_not_drive_turns()
    {
        using var harness = LauncherTestHarness.Create(runner: SessionRunners.Terminal);

        harness.Spawn(SessionRoles.Implementer, "repo-1", "imp-1");

        var state = PrintSessionState_Store.Read_OrNull(
            PrintSessionState_Store.Get_StateFile(harness.Paths, SessionRoles.Implementer, "repo-1", "imp-1"));

        Assert.NotNull(state);
        Assert.False(state!.DrivesTurns);
    }

    [Fact]
    public void A_session_that_was_bridge_driven_keeps_its_cursors_when_it_moves_to_a_terminal()
    {
        using var harness = LauncherTestHarness.Create(runner: SessionRunners.Terminal);
        harness.Write_ExistingState(SessionRoles.Implementer, "repo-1", "imp-1", deliveredIdentities: ["abc123"]);

        harness.Spawn(SessionRoles.Implementer, "repo-1", "imp-1");

        var state = PrintSessionState_Store.Read_OrNull(
            PrintSessionState_Store.Get_StateFile(harness.Paths, SessionRoles.Implementer, "repo-1", "imp-1"));

        Assert.False(state!.DrivesTurns);
        Assert.Contains("abc123", state.Cursors[0].Delivered);
    }
}
```

> **Note for the implementer:** `LauncherTestHarness` may not exist under that name. Read `AIOrchestratorCoreLib.Tests/Launching/` first and use whatever harness `LauncherRunnerSelectionTests` uses; extend it rather than creating a parallel one.

- [ ] **Step 2: Run it and watch it fail**

Run: `dotnet test AIOrchestratorCoreLib.Tests/AIOrchestratorCoreLib.Tests.csproj --filter "FullyQualifiedName~TerminalSpawnKeepsItsCursorTests"`
Expected: FAIL — `Read_OrNull` returns null, the file was deleted.

- [ ] **Step 3: Replace the delete with a write**

At `OrchestrationLauncherModel.cs:554`, replace the `Delete_IfExists` branch with:

```csharp
        // A TERMINAL SESSION KEEPS THIS FILE NOW, and it means something different in it: not "the
        // dispatcher runs my turns" (Task 4 screens on DrivesTurns for that) but "here are my cursors
        // and my state pack". Deleting it was right while the file had one meaning; it is what left a
        // terminal session with no cursor, and therefore with no digest, no riding notes and no pack.
        if (runner.Kind == SessionRunners.Terminal)
            Demote_ToTerminal(launch);
```

and add:

```csharp
    /// <summary>
    /// Writes the session's state with <c>DrivesTurns = false</c>, PRESERVING the cursors of a session
    /// that was bridge-driven a moment ago — losing them would re-deliver every live entry to the
    /// window as new.
    /// </summary>
    void Demote_ToTerminal(ISessionLaunch launch)
    {
        var stateFile = PrintSessionState_Store.Get_StateFile(_paths, launch.Role, launch.OrchId, launch.MemberId);
        var existing = PrintSessionState_Store.Read_OrNull(stateFile);

        if (existing is { DrivesTurns: false })
            return;

        if (existing != null)
            _log.Log_Warning(launch.OrchId, $"'{launch.MemberId}' was registered as print-run but its role is now runner: terminal — it keeps its cursors and stops driving turns");

        PrintSessionState_Store.Write(stateFile, PrintSessionState_Factory.Create_ForTerminal(existing, launch, _paths));
    }
```

Add `Create_ForTerminal(IPrintSessionState? existing, ISessionLaunch launch, ISupervisionPaths paths)` to `PrintSessionState_Factory`: when `existing` is null it builds a fresh state with an empty cursor list and `drivesTurns: false`; when it is not null it copies every field and sets `drivesTurns: false`.

- [ ] **Step 4: Run the test and watch it pass**

Run: `dotnet test AIOrchestratorCoreLib.Tests/AIOrchestratorCoreLib.Tests.csproj --filter "FullyQualifiedName~TerminalSpawnKeepsItsCursorTests"`
Expected: PASS, 2 tests.

- [ ] **Step 5: Run the launcher suite**

Run: `dotnet test AIOrchestratorCoreLib.Tests/AIOrchestratorCoreLib.Tests.csproj --filter "FullyQualifiedName~Launching"`
Expected: green.

- [ ] **Step 6: Commit**

```bash
git add AIOrchestratorCoreLib/Launching/ AIOrchestratorCoreLib/Running/PrintSessionState/ AIOrchestratorCoreLib.Tests/Launching/
git commit -F /tmp/cm.txt   # "feat(launching): a terminal session keeps its cursors instead of having its state deleted"
```

---

## Task 6: `WakeDecision_Resolver` — the question gets one home

**Files:**
- Create: `AIOrchestratorCoreLib/Running/WakeDecision/{IWakeDecision,WakeDecisionModel,WakeDecision_Factory,WakeDecision_Resolver}.cs`
- Modify: `AIOrchestratorCoreLib/Running/PrintTurnDispatcher/PrintTurnDispatcherModel.cs` (the block at ~836)
- Test: `AIOrchestratorCoreLib.Tests/Running/WakeDecisionResolverTests.cs`

**Interfaces:**
- Consumes: `TurnSources_Resolver.Resolve`, `PrintTurn_Trigger.Select_Pending`, `PrintTurn_Trigger.Select_AgentNotes`, `WakeUp_Policy.Resolve_WakeReason_OrNull`, `PendingEntry`, `ITurnSource`, `ITurnCursor`.
- Produces:

```csharp
public interface IWakeDecision
{
    string Reason { get; }
    IReadOnlyList<PendingEntry> Pending { get; }      // ordered, agent notes already ridden in front
    IReadOnlyList<ITurnSource> Sources { get; }
}

public static class WakeDecision_Resolver
{
    public static IWakeDecision? Resolve_OrNull(
        ISupervisionPaths paths, IOrchestrationSessionStore store,
        IPrintSessionState state, IRunnerConfigs configs,
        DateTime? digestHeldSince, DateTime nowLocal);
}
```

Tasks 8, 9 and 11 call `Resolve_OrNull`.

**The whole point:** after this task the answer exists in exactly one place, and Task 8 can ask it about a session the dispatcher will never run.

- [ ] **Step 1: Write the failing test**

```csharp
// AIOrchestratorCoreLib.Tests/Running/WakeDecisionResolverTests.cs
using AIOrchestratorCoreLib.Channels;
using AIOrchestratorCoreLib.Running;
using AIOrchestratorCoreLib.Running.WakeDecision;
using Xunit;

namespace AIOrchestratorCoreLib.Tests.Running;

/// <summary>
/// THE SAME FOUR RULES, ASKED FROM OUTSIDE THE DISPATCHER. These cases restate what
/// <see cref="WakeUpDigestReviewFixTests"/> already pins through the dispatcher — deliberately, because
/// the extraction is only correct if both callers get the same answers. If one of these disagrees with
/// its dispatcher twin, the extraction changed behaviour and the extraction is wrong.
/// </summary>
public class WakeDecisionResolverTests
{
    static readonly DateTime T0 = new(2026, 9, 15, 10, 0, 0);

    [Fact]
    public void The_owner_wakes_it_now()
    {
        var fixture = WakeFixture.ForSupervisor("repo-1").With_OwnerEntry("do the merge");

        var decision = fixture.Resolve(digestHeldSince: null, nowLocal: T0);

        Assert.NotNull(decision);
        Assert.Contains("the owner wrote", decision!.Reason);
    }

    [Fact]
    public void An_ordinary_member_report_is_held_for_the_digest()
    {
        var fixture = WakeFixture.ForSupervisor("repo-1").With_SeenMemberEntry("imp-1", "TASK 1 committed abc1234");

        var decision = fixture.Resolve(digestHeldSince: T0, nowLocal: T0.AddMinutes(1));

        Assert.Null(decision);
    }

    [Fact]
    public void The_same_report_wakes_it_once_the_window_has_passed()
    {
        var fixture = WakeFixture.ForSupervisor("repo-1").With_SeenMemberEntry("imp-1", "TASK 1 committed abc1234");

        var decision = fixture.Resolve(digestHeldSince: T0, nowLocal: T0.AddMinutes(6));

        Assert.NotNull(decision);
    }

    [Fact]
    public void An_app_note_never_produces_a_decision_and_rides_the_next_one()
    {
        var fixture = WakeFixture.ForSupervisor("repo-1").With_AppNote("[agent] PLAN.md is behind your verdicts", T0);

        Assert.Null(fixture.Resolve(digestHeldSince: null, nowLocal: T0.AddMinutes(1)));

        fixture.With_OwnerEntry("status?");
        var decision = fixture.Resolve(digestHeldSince: null, nowLocal: T0.AddMinutes(2));

        Assert.NotNull(decision);
        Assert.Contains(decision!.Pending, p => p.Entry.Author == ChannelAuthors.App);
    }
}
```

> **Note for the implementer:** `WakeFixture` is a new test helper you write in the same file (or beside it in `TestSupport/`). It must build a real `ISupervisionPaths` over a temp directory, write real channel files with `ChannelAppender`, and build a real `IPrintSessionState` — not a mock. The existing `PrintRunnerTestHarness` shows how; reuse its temp-root and paths setup.

- [ ] **Step 2: Run it and watch it fail**

Run: `dotnet test AIOrchestratorCoreLib.Tests/AIOrchestratorCoreLib.Tests.csproj --filter "FullyQualifiedName~WakeDecisionResolverTests"`
Expected: compile error — the namespace does not exist.

- [ ] **Step 3: Write the triple**

```csharp
// IWakeDecision.cs
using AIOrchestratorCoreLib.Running.PendingTraffic;
using AIOrchestratorCoreLib.Running.TurnSource;

namespace AIOrchestratorCoreLib.Running.WakeDecision;

/// <summary>
/// THE ANSWER TO "DOES THIS SESSION TAKE A TURN NOW, AND WITH WHAT?" — the question this system used
/// to answer twice: in C# for a bridge-driven session and, independently, in the bash monitor of
/// <c>kit/skills/*/reference/watcher.md</c> for a terminal one. The bash copy had no cursor, no digest
/// and no notion of an entry that should wake nobody, so every mitigation built during the 2026-09-08
/// token-efficiency work reached only half the machines (2026-09-15 one-wake-model spec §1).
///
/// <para>
/// A null decision is not an error: it is "not yet" — nothing pending, or a member's ordinary report
/// still inside its digest window.
/// </para>
/// </summary>
public interface IWakeDecision
{
    /// <summary>Why the turn is starting, in the words the log and the wake ticket carry.</summary>
    string Reason { get; }

    /// <summary>Everything the turn is handed, ordered, with the app's riding notes already in front.</summary>
    IReadOnlyList<PendingEntry> Pending { get; }

    /// <summary>Every channel this session is woken by — the cursors to advance once the turn is admitted.</summary>
    IReadOnlyList<ITurnSource> Sources { get; }
}
```

`WakeDecisionModel.cs` is the immutable carrier; `WakeDecision_Factory.Create(reason, pending, sources)` builds it. Follow `PendingTraffic/` for style.

- [ ] **Step 4: Write the resolver by MOVING the dispatcher's block**

`WakeDecision_Resolver.Resolve_OrNull` performs, in order, exactly what `PrintTurnDispatcherModel` does today between resolving sources and calling `Start_Turn`: resolve sources, read each channel, `Select_Pending` per source, order with `PendingTraffic_Orderer`, compute `firstContactSources`, call `WakeUp_Policy.Resolve_WakeReason_OrNull` (or the boot-turn reason when `Needs_BootTurn`), return null when the reason is null, otherwise ride the agent notes via the existing `With_AgentNotes` logic and return the decision.

**Move the code, do not retype it.** Every comment block in that region is load-bearing history (the restart/digest finding of 2026-09-09, the boot-turn carve-out, the notes-after-the-rules rule) and must travel with it.

- [ ] **Step 5: Make the dispatcher a consumer**

The block at ~836 becomes:

```csharp
        var decision = WakeDecision_Resolver.Resolve_OrNull(_paths, _store, state, configs, digestHeldSince, nowLocal);

        if (decision == null)
            return;

        if (digestHeldSince != null)
            _log.Log_Info(orchId, $"'{memberId}': {Describe_Traffic(decision.Pending)} — {decision.Reason}");

        Start_Turn(key, stateFile, state, decision.Pending, decision.Sources, tracker, configs);
```

- [ ] **Step 6: Run the new test and watch it pass**

Run: `dotnet test AIOrchestratorCoreLib.Tests/AIOrchestratorCoreLib.Tests.csproj --filter "FullyQualifiedName~WakeDecisionResolverTests"`
Expected: PASS, 4 tests.

- [ ] **Step 7: Run the EXISTING digest oracles — this is the real gate**

Run:
```bash
dotnet test AIOrchestratorCoreLib.Tests/AIOrchestratorCoreLib.Tests.csproj \
  --filter "FullyQualifiedName~WakeUpDigest|FullyQualifiedName~AgentNotesRideTheNextTurn|FullyQualifiedName~MemberTrafficRidesOneDigestedTurn|FullyQualifiedName~BootTurn|FullyQualifiedName~MultiSourceSupervisor|FullyQualifiedName~OwnerTrafficSkipsTheCoalesceWindow"
```
Expected: PASS, all of them, with **no edits to any of those files**. If any one of them needed an edit, the extraction changed behaviour — revert and redo Step 4.

- [ ] **Step 8: Run the whole suite, then commit**

```bash
dotnet test AIOrchestratorCoreLib.Tests/AIOrchestratorCoreLib.Tests.csproj
git add AIOrchestratorCoreLib/Running/WakeDecision/ AIOrchestratorCoreLib/Running/PrintTurnDispatcher/PrintTurnDispatcherModel.cs AIOrchestratorCoreLib.Tests/Running/WakeDecisionResolverTests.cs
git commit -F /tmp/cm.txt   # "refactor(running): one home for 'does this session wake now', reachable from outside the dispatcher"
```

---

## Task 7: The wake ticket

**Files:**
- Create: `AIOrchestratorCoreLib/Running/WakeTicket/{IWakeTicket,WakeTicketModel,WakeTicket_Factory,WakeTicket_Store}.cs`
- Test: `AIOrchestratorCoreLib.Tests/Running/WakeTicketStoreTests.cs`

**Interfaces:**
- Produces:

```csharp
public interface IWakeTicket
{
    int Number { get; }              // strictly increasing per session; the watcher compares on it
    string Reason { get; }
    string? StatePackFile { get; }   // null until Task 11
    DateTime StampedUtc { get; }
}

public static class WakeTicket_Store
{
    public static string Get_File(ISupervisionPaths paths, SessionRoles role, string orchId, string memberId);
    public static IWakeTicket? Read_OrNull(string ticketFile);
    public static void Write(string ticketFile, IWakeTicket ticket);
}
```

Task 8 writes tickets; Task 9's script reads the file.

**Format** — one line of JSON, written whole with a temp-file-and-rename so a watcher never reads a half-written ticket:

```json
{"number":7,"reason":"the owner wrote in 'owner'","statePackFile":null,"stampedUtc":"2026-09-15T08:12:44Z"}
```

`Get_File` returns `<orchestration folder>/<memberId>/.wake` for a member, `<orchestration folder>/.wake-supervisor` for the supervisor, and `<general folder>/.wake` for the general supervisor — mirroring how `PrintSessionState_Store.Get_StateFile` already names those three cases. Read that method and follow it exactly.

- [ ] **Step 1: Write the failing test**

```csharp
// AIOrchestratorCoreLib.Tests/Running/WakeTicketStoreTests.cs
using System.IO;
using AIOrchestratorCoreLib.Running.WakeTicket;
using Xunit;

namespace AIOrchestratorCoreLib.Tests.Running;

/// <summary>
/// THE TICKET IS THE WHOLE PROTOCOL BETWEEN THE APP AND A TERMINAL SESSION'S MONITOR, so it has to
/// survive being read at the worst moment: the monitor polls on its own clock and will sometimes read
/// while the app writes. Hence write-temp-then-rename, and a number rather than a timestamp to compare
/// on — two wakes inside one clock tick must still be two wakes.
/// </summary>
public class WakeTicketStoreTests
{
    [Fact]
    public void A_ticket_round_trips()
    {
        var file = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        var written = WakeTicket_Factory.Create(number: 7, reason: "the owner wrote in 'owner'", statePackFile: null, stampedUtc: new DateTime(2026, 9, 15, 8, 12, 44, DateTimeKind.Utc));

        WakeTicket_Store.Write(file, written);
        var read = WakeTicket_Store.Read_OrNull(file);

        Assert.Equal(7, read!.Number);
        Assert.Equal("the owner wrote in 'owner'", read.Reason);
        Assert.Null(read.StatePackFile);
        File.Delete(file);
    }

    [Fact]
    public void A_missing_ticket_reads_as_null_rather_than_throwing()
    {
        Assert.Null(WakeTicket_Store.Read_OrNull(Path.Combine(Path.GetTempPath(), Path.GetRandomFileName())));
    }

    [Fact]
    public void A_corrupt_ticket_reads_as_null_rather_than_throwing()
    {
        var file = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        File.WriteAllText(file, "{not json");

        Assert.Null(WakeTicket_Store.Read_OrNull(file));
        File.Delete(file);
    }

    [Fact]
    public void Writing_is_atomic_from_a_reader_point_of_view()
    {
        var file = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        WakeTicket_Store.Write(file, WakeTicket_Factory.Create(1, "first", null, DateTime.UtcNow));
        WakeTicket_Store.Write(file, WakeTicket_Factory.Create(2, "second", null, DateTime.UtcNow));

        Assert.Equal(2, WakeTicket_Store.Read_OrNull(file)!.Number);
        Assert.False(File.Exists(file + ".tmp"));
        File.Delete(file);
    }
}
```

- [ ] **Step 2: Run it and watch it fail**

Run: `dotnet test AIOrchestratorCoreLib.Tests/AIOrchestratorCoreLib.Tests.csproj --filter "FullyQualifiedName~WakeTicketStoreTests"`
Expected: compile error.

- [ ] **Step 3: Write the triple and the store**

`Write` serialises with `System.Text.Json` to `<file>.tmp`, then `File.Move(tmp, file, overwrite: true)`. `Read_OrNull` returns null on a missing file, on a `JsonException`, and on an `IOException` — a monitor that cannot read its ticket must not crash the app, and the app that cannot read its own last ticket falls back to number 0 in Task 8.

- [ ] **Step 4: Run the test and watch it pass**

Run: `dotnet test AIOrchestratorCoreLib.Tests/AIOrchestratorCoreLib.Tests.csproj --filter "FullyQualifiedName~WakeTicketStoreTests"`
Expected: PASS, 4 tests.

- [ ] **Step 5: Commit**

```bash
git add AIOrchestratorCoreLib/Running/WakeTicket/ AIOrchestratorCoreLib.Tests/Running/WakeTicketStoreTests.cs
git commit -F /tmp/cm.txt   # "feat(running): the wake ticket — how the app tells a terminal session it is time"
```

---

## Task 8: The engine writes tickets for ticket-mode terminal sessions

**Files:**
- Modify: `AIOrchestratorCoreLib/Bridge/BridgeEngine/BridgeEngineModel.cs` (new private `Sweep_WakeTickets_Async`, called from the existing tick beside the mirror sweep)
- Test: `AIOrchestratorCoreLib.Tests/Bridge/WakeTicketSweepTests.cs`

**Interfaces:**
- Consumes: `WakeDecision_Resolver.Resolve_OrNull` (Task 6), `WakeTicket_Store` (Task 7), `IRoleRunnerConfig.Wake` (Task 2), `IPrintSessionState.DrivesTurns` (Task 3).
- Produces: nothing new. This is where the two halves meet.

**The rule:** for every session whose runner is `Terminal` AND whose `Wake` is `Ticket` AND whose state says `DrivesTurns == false`, ask the resolver; on a non-null decision, write a ticket numbered one above the last and **advance that session's cursors over the decision's pending entries**. On a null decision, do nothing.

**Why the cursor advances here:** in this plan the ticket means only "something changed, go and read" — the session still reads its channels itself, exactly as it does today. Advancing on write is therefore correct and idempotent: a session that is mid-turn and misses the ticket still finds the entries when it reads. Task 11 changes what the ticket carries, not this rule.

- [ ] **Step 1: Write the failing test**

```csharp
// AIOrchestratorCoreLib.Tests/Bridge/WakeTicketSweepTests.cs
using AIOrchestratorCoreLib.Running;
using AIOrchestratorCoreLib.Running.WakeTicket;
using Xunit;

namespace AIOrchestratorCoreLib.Tests.Bridge;

/// <summary>
/// THE STEP THAT REPAIRS THE MACHINE ON THE DEFAULT RUNNER. A terminal supervisor in ticket mode gets
/// the SAME policy a bridge one gets: the owner wakes it now, a member's ordinary report waits for the
/// digest, and the app's own bookkeeping wakes nobody — measured on the VPS, 247 of ~400 supervisor
/// wake-ups were member traffic at ~1 M input tokens each, and zero were triggered by app entries alone.
/// </summary>
public class WakeTicketSweepTests
{
    [Fact]
    public async Task An_owner_message_writes_a_ticket()
    {
        using var harness = BridgeTestHarness.Create();
        harness.Register_TerminalSession(SessionRoles.Supervisor, "repo-1", "supervisor", wake: WakeModes.Ticket);
        harness.Append_OwnerEntry("repo-1", "do the merge");

        await harness.Tick_Async();

        var ticket = WakeTicket_Store.Read_OrNull(harness.TicketFile("repo-1", "supervisor"));
        Assert.NotNull(ticket);
        Assert.Contains("the owner wrote", ticket!.Reason);
    }

    [Fact]
    public async Task An_app_note_writes_no_ticket()
    {
        using var harness = BridgeTestHarness.Create();
        harness.Register_TerminalSession(SessionRoles.Supervisor, "repo-1", "supervisor", wake: WakeModes.Ticket);
        harness.Append_AppEntry("repo-1", "[agent] PLAN.md is behind your verdicts");

        await harness.Tick_Async();

        Assert.Null(WakeTicket_Store.Read_OrNull(harness.TicketFile("repo-1", "supervisor")));
    }

    [Fact]
    public async Task A_session_still_on_the_watcher_gets_no_ticket()
    {
        using var harness = BridgeTestHarness.Create();
        harness.Register_TerminalSession(SessionRoles.Supervisor, "repo-1", "supervisor", wake: WakeModes.Watcher);
        harness.Append_OwnerEntry("repo-1", "do the merge");

        await harness.Tick_Async();

        Assert.Null(WakeTicket_Store.Read_OrNull(harness.TicketFile("repo-1", "supervisor")));
    }

    [Fact]
    public async Task The_same_entry_does_not_write_a_second_ticket()
    {
        using var harness = BridgeTestHarness.Create();
        harness.Register_TerminalSession(SessionRoles.Supervisor, "repo-1", "supervisor", wake: WakeModes.Ticket);
        harness.Append_OwnerEntry("repo-1", "do the merge");

        await harness.Tick_Async();
        await harness.Tick_Async();

        Assert.Equal(1, WakeTicket_Store.Read_OrNull(harness.TicketFile("repo-1", "supervisor"))!.Number);
    }
}
```

> **Note for the implementer:** use the bridge harness the existing `Bridge/` tests use — read two of them first and follow their setup. Do not create a second harness.

- [ ] **Step 2: Run it and watch it fail**

Run: `dotnet test AIOrchestratorCoreLib.Tests/AIOrchestratorCoreLib.Tests.csproj --filter "FullyQualifiedName~WakeTicketSweepTests"`
Expected: FAIL — no ticket is ever written.

- [ ] **Step 3: Write the sweep**

```csharp
    /// <summary>
    /// THE APP TELLS A TERMINAL SESSION WHEN TO TAKE A TURN, with the same policy that opens a
    /// bridge-driven one (<see cref="WakeDecision_Resolver"/>). Before this, that session's bash
    /// monitor decided for itself by fingerprinting its channels — a second implementation with no
    /// cursor, no digest and no way to tell an app note from a member's report, on the runner that is
    /// the DEFAULT (2026-09-15 one-wake-model spec).
    ///
    /// <para>
    /// THE CURSOR ADVANCES HERE, and that is safe because in this shape the ticket means only "go and
    /// read". The session still reads its own channels, so a ticket it misses while mid-turn costs it
    /// nothing: the entries are still there when it looks.
    /// </para>
    /// </summary>
    async Task Sweep_WakeTickets_Async(CancellationToken cancellationToken)
    {
        foreach (var registered in Discover_TicketModeSessions())
        {
            var state = PrintSessionState_Store.Read_OrNull(registered.StateFile);

            if (state is not { DrivesTurns: false })
                continue;

            var decision = WakeDecision_Resolver.Resolve_OrNull(
                _paths, _store, state, _configProvider.Get_Current().Runners,
                Get_DigestHeldSince(registered.Key), DateTime.Now);

            if (decision == null)
                continue;

            var ticketFile = WakeTicket_Store.Get_File(_paths, state.Role, state.OrchId, state.MemberId);
            var previous = WakeTicket_Store.Read_OrNull(ticketFile);

            WakeTicket_Store.Write(ticketFile, WakeTicket_Factory.Create(
                number: (previous?.Number ?? 0) + 1, reason: decision.Reason,
                statePackFile: null, stampedUtc: DateTime.UtcNow));

            PrintSessionState_Store.Write(registered.StateFile, Advance_Cursors(state, decision));
            _log.Log_Info(state.OrchId, $"'{state.MemberId}': wake ticket {(previous?.Number ?? 0) + 1} — {decision.Reason}");
        }

        await Task.CompletedTask;
    }
```

`Discover_TicketModeSessions` mirrors the dispatcher's `Discover_RegisteredSessions` but screens on `Runner == Terminal && Wake == Ticket`. `Advance_Cursors` reuses the dispatcher's existing cursor-advance helper — find it and call it, do not write a second one (CLAUDE.md decision 12).

- [ ] **Step 4: Call it from the tick**

Add `await Sweep_WakeTickets_Async(cancellationToken);` to the engine tick, immediately after the mirror sweep and before the periodic-status work.

- [ ] **Step 5: Run the test and watch it pass**

Run: `dotnet test AIOrchestratorCoreLib.Tests/AIOrchestratorCoreLib.Tests.csproj --filter "FullyQualifiedName~WakeTicketSweepTests"`
Expected: PASS, 4 tests.

- [ ] **Step 6: Run the whole suite, then commit**

```bash
dotnet test AIOrchestratorCoreLib.Tests/AIOrchestratorCoreLib.Tests.csproj
git add AIOrchestratorCoreLib/Bridge/BridgeEngine/BridgeEngineModel.cs AIOrchestratorCoreLib.Tests/Bridge/WakeTicketSweepTests.cs
git commit -F /tmp/cm.txt   # "feat(bridge): the app decides when a terminal session wakes, with the policy the bridge already uses"
```

---

## Task 9: The 10-line watcher

**Files:**
- Modify: `kit/skills/{supervisor,implementer,reviewer,solo,general-supervisor,communicator}/reference/watcher.md`
- Modify: `kit/hooks/watcher-behaviour-check.sh`
- Test: `kit/hooks/watcher-behaviour-check.sh` itself (bash), run from the repo root

**Interfaces:**
- Consumes: the ticket file written by Task 8.
- Produces: the monitor script each role command arms at boot.

**The script**, added to each `watcher.md` as a second section titled `## Ticket mode — when the app decides` while the existing fingerprint script stays put under `## Watcher mode — when you decide`:

```bash
# TICKET MODE. The app decides whether you take a turn, with the same policy it uses for a headless
# session, and says so by writing this file. You carry the message; you do not decide.
#
# Everything the old script did — fingerprinting every channel, proving a change was not your own
# write, the blind-alarm strike counter — is GONE, because none of it was ever your question. It was a
# second implementation of a decision the app already makes, and it had no cursor, no digest, and no
# way to tell the app's own bookkeeping from a member's report.
ticket="${AIORCH_SUPERVISION_ROOT:-$HOME/.claude/supervision}/$ARGUMENTS/.wake-supervisor"
sup="${AIORCH_SUPERVISION_ROOT:-$HOME/.claude/supervision}/$ARGUMENTS"
last=""
while true; do
  sleep 2
  # MEETING: the owner is at your terminal (/pc). Stay armed, say nothing, and do NOT advance `last`
  # — the first tick after the flag is gone delivers the ticket you were holding, exactly once.
  [ -f "$sup/.meeting" ] && continue
  now="$(cat "$ticket" 2>/dev/null)" || continue
  [ -z "$now" ] && continue
  [ "$now" = "$last" ] && continue
  last="$now"
  echo "WAKE — $now. Read every channel from your last entry down, act on it, append your entries."
done
```

(For a member the `ticket` line is `.../$ARGUMENTS/.wake`; `$ARGUMENTS` already carries `<orch-id>/<member-id>` in those role commands. Check each role command's own `$ARGUMENTS` shape before editing its `watcher.md`.)

- [ ] **Step 1: Write the failing harness case**

Add to `kit/hooks/watcher-behaviour-check.sh`, following the file's existing case style:

```bash
case_ticket_mode_fires_once_per_ticket() {
  local root; root="$(mktemp -d)"
  mkdir -p "$root/repo-1"
  printf '{"number":1,"reason":"the owner wrote in '"'"'owner'"'"'"}' > "$root/repo-1/.wake-supervisor"
  local out; out="$(AIORCH_SUPERVISION_ROOT="$root" ARGUMENTS=repo-1 timeout 6 bash "$SCRIPT_UNDER_TEST" 2>&1 | head -5)"
  assert_contains "$out" "WAKE" "a ticket wakes the session"
  assert_line_count "$out" 1 "one ticket is one wake, not a stream"
  rm -rf "$root"
}

case_ticket_mode_is_silent_during_a_meeting() {
  local root; root="$(mktemp -d)"
  mkdir -p "$root/repo-1"
  touch "$root/repo-1/.meeting"
  printf '{"number":1,"reason":"x"}' > "$root/repo-1/.wake-supervisor"
  local out; out="$(AIORCH_SUPERVISION_ROOT="$root" ARGUMENTS=repo-1 timeout 6 bash "$SCRIPT_UNDER_TEST" 2>&1)"
  assert_empty "$out" "a meeting silences the monitor"
  rm -rf "$root"
}
```

**And keep the harness's own refusal rule** (CLAUDE.md decision 20): if `$SCRIPT_UNDER_TEST` does not resolve to a real file, the harness must **exit non-zero with a message**, never report passes. Verify that guard still fires before you trust any green here.

- [ ] **Step 2: Run the harness and watch it fail**

Run: `bash kit/hooks/watcher-behaviour-check.sh`
Expected: FAIL — the ticket cases have no script to run.

- [ ] **Step 3: Add the ticket script to all six `watcher.md` files**

Additive only: the fingerprint script stays, under its own heading, with a line above both saying which one to arm — *"arm the one your `runners.<role>.wake` names; if you do not know, arm the watcher-mode script."*

- [ ] **Step 4: Run the harness and watch it pass**

Run: `bash kit/hooks/watcher-behaviour-check.sh`
Expected: PASS, including the pre-existing cases.

- [ ] **Step 5: Commit**

```bash
git add kit/skills/*/reference/watcher.md kit/hooks/watcher-behaviour-check.sh
git commit -F /tmp/cm.txt   # "feat(kit): a monitor that carries the app's decision instead of making its own"
```

---

## Task 10: The liveness assertion

**Files:**
- Modify: `AIOrchestratorCoreLib/Bridge/BridgeEngine/BridgeEngineModel.cs`
- Test: `AIOrchestratorCoreLib.Tests/Bridge/WakeTicketLivenessTests.cs`

**Interfaces:**
- Consumes: `WakeTicket_Store.Read_OrNull`, the existing stall path (`STALL_ALERT_MINUTES`, `Append_SupervisorAttention_UnlessMeeting`).

**The rule:** if a ticket-mode session has had a ticket written and has filed no entry of its own for longer than `MemberDigestWindow × 2`, log it and raise the existing stall path once. **A session that stops waking is a session that silently does nothing** — that is the one failure mode this change introduces, and it must be loud.

- [ ] **Step 1: Write the failing test**

```csharp
// AIOrchestratorCoreLib.Tests/Bridge/WakeTicketLivenessTests.cs
using Xunit;

namespace AIOrchestratorCoreLib.Tests.Bridge;

/// <summary>
/// THE FAILURE THIS SERIES INTRODUCES, MADE LOUD. Under the old monitor a session that stopped waking
/// was still fingerprinting and would catch up on the next change. Under a ticket it will not: a
/// monitor that died, or a ticket file nobody is polling, is indistinguishable from a quiet
/// orchestration — which is exactly the shape of a silent failure.
/// </summary>
public class WakeTicketLivenessTests
{
    [Fact]
    public async Task A_ticket_that_produces_no_entry_within_two_windows_raises_the_stall_path()
    {
        using var harness = BridgeTestHarness.Create();
        harness.Register_TerminalSession(SessionRoles.Supervisor, "repo-1", "supervisor", wake: WakeModes.Ticket);
        harness.Append_OwnerEntry("repo-1", "do the merge");
        await harness.Tick_Async();

        harness.Advance_Clock(TimeSpan.FromMinutes(11));
        await harness.Tick_Async();

        Assert.Contains(harness.SupervisorAttentionEntries, e => e.Subject.Contains("has not taken a turn"));
    }

    [Fact]
    public async Task It_is_raised_once_not_every_tick()
    {
        using var harness = BridgeTestHarness.Create();
        harness.Register_TerminalSession(SessionRoles.Supervisor, "repo-1", "supervisor", wake: WakeModes.Ticket);
        harness.Append_OwnerEntry("repo-1", "do the merge");
        await harness.Tick_Async();

        harness.Advance_Clock(TimeSpan.FromMinutes(11));
        await harness.Tick_Async();
        await harness.Tick_Async();
        await harness.Tick_Async();

        Assert.Single(harness.SupervisorAttentionEntries, e => e.Subject.Contains("has not taken a turn"));
    }
}
```

- [ ] **Step 2: Run it and watch it fail**

Run: `dotnet test AIOrchestratorCoreLib.Tests/AIOrchestratorCoreLib.Tests.csproj --filter "FullyQualifiedName~WakeTicketLivenessTests"`
Expected: FAIL — nothing is raised.

- [ ] **Step 3: Implement the assertion inside the sweep**

Follow `BudgetAlert_Planner`'s one-shot-token discipline for the "raised once" half — and its lesson: the token is spent only when the entry was actually written.

- [ ] **Step 4: Run the test and watch it pass, then the suite, then commit**

```bash
dotnet test AIOrchestratorCoreLib.Tests/AIOrchestratorCoreLib.Tests.csproj --filter "FullyQualifiedName~WakeTicketLivenessTests"
dotnet test AIOrchestratorCoreLib.Tests/AIOrchestratorCoreLib.Tests.csproj
git add AIOrchestratorCoreLib/Bridge/BridgeEngine/BridgeEngineModel.cs AIOrchestratorCoreLib.Tests/Bridge/WakeTicketLivenessTests.cs
git commit -F /tmp/cm.txt   # "feat(bridge): a ticket nobody acts on is a stall, and it says so"
```

---

## Task 11: The ticket carries the state pack (spec step 2)

**Files:**
- Modify: `AIOrchestratorCoreLib/Bridge/BridgeEngine/BridgeEngineModel.cs` (`Sweep_WakeTickets_Async`)
- Modify: `kit/skills/*/reference/watcher.md` (ticket script echoes the pack path), `kit/skills/*/SKILL.md` (boot sequence)
- Test: `AIOrchestratorCoreLib.Tests/Bridge/WakeTicketCarriesThePackTests.cs`

**Interfaces:**
- Consumes: `StatePack_Builder.Build`, `StatePack_Writer.Write`, `StatePack_Locator.Get_File`, `StatePackInputs_Reader` — all existing, used today only by `PrintTurnExecutorModel`.

**This is the payoff.** The three capabilities the terminal runner lacks are now all reachable: the digest (Task 8), the riding app-notes (Task 8, via the resolver), and the pack (here).

- [ ] **Step 1: Write the failing test**

```csharp
// AIOrchestratorCoreLib.Tests/Bridge/WakeTicketCarriesThePackTests.cs
using System.IO;
using AIOrchestratorCoreLib.Running.WakeTicket;
using Xunit;

namespace AIOrchestratorCoreLib.Tests.Bridge;

public class WakeTicketCarriesThePackTests
{
    [Fact]
    public async Task The_ticket_names_a_pack_that_exists_and_holds_the_brief()
    {
        using var harness = BridgeTestHarness.Create();
        harness.Register_TerminalSession(SessionRoles.Implementer, "repo-1", "imp-1", wake: WakeModes.Ticket);
        harness.Append_SupervisorEntry("repo-1", "imp-1", subject: "TASK 1 — build the thing");

        await harness.Tick_Async();

        var ticket = WakeTicket_Store.Read_OrNull(harness.TicketFile("repo-1", "imp-1"))!;
        Assert.NotNull(ticket.StatePackFile);
        Assert.True(File.Exists(ticket.StatePackFile));
        Assert.Contains("TASK 1 — build the thing", File.ReadAllText(ticket.StatePackFile!));
    }
}
```

- [ ] **Step 2: Run it and watch it fail**

Expected: FAIL — `StatePackFile` is null (Task 7 wrote it so deliberately).

- [ ] **Step 3: Build and write the pack in the sweep**

In `Sweep_WakeTickets_Async`, before writing the ticket, do exactly what `PrintTurnExecutorModel.cs:113` does — build the inputs, `StatePack_Builder.Build`, `StatePack_Writer.Write` to `StatePack_Locator.Get_File(...)` — and pass that path as `statePackFile`. **Call the existing methods; do not reimplement the pack.**

- [ ] **Step 4: Teach the monitor and the boot sequence about it**

In each ticket-mode script the wake line becomes:

```bash
  echo "WAKE — $now. Read the state pack named in this ticket FIRST; it holds your brief, your last report, your channel's new entries and your repo state. Then act and append your entries."
```

In each `SKILL.md` boot sequence, add one line under the existing boot steps: *"If a wake ticket names a state pack, read it instead of going to look for your brief — it is assembled by the app and cannot go stale."*

- [ ] **Step 5: Run the test and watch it pass, run the suite, run the kit harness**

```bash
dotnet test AIOrchestratorCoreLib.Tests/AIOrchestratorCoreLib.Tests.csproj --filter "FullyQualifiedName~WakeTicketCarriesThePack"
dotnet test AIOrchestratorCoreLib.Tests/AIOrchestratorCoreLib.Tests.csproj
bash kit/hooks/watcher-behaviour-check.sh
```

- [ ] **Step 6: Commit**

```bash
git add AIOrchestratorCoreLib/Bridge/BridgeEngine/BridgeEngineModel.cs kit/skills/ AIOrchestratorCoreLib.Tests/Bridge/WakeTicketCarriesThePackTests.cs
git commit -F /tmp/cm.txt   # "feat(bridge,kit): a terminal session is handed its state pack, like a headless one"
```

---

## Task 12: Re-register the marketplace, and measure

**Files:**
- Modify: `docs/superpowers/plans/2026-09-15-one-wake-model-01-report.md` (create)

**Why the marketplace is here and not earlier:** `known_marketplaces.json` registers `aiorch-local` at
`/Users/nvene/Visual Studio/AIOrchestrator-integration/kit` — **a directory that does not exist**. Until
it points at a real checkout, no corrected skill can reach any session, and Tasks 9 and 11 are
unverifiable in the only way that counts (CLAUDE.md decision 17: verify against a RESTARTED session,
not against a `git diff`).

- [ ] **Step 1: Re-register and reinstall**

```bash
claude plugin marketplace remove aiorch-local
claude plugin marketplace add "/Users/nvene/Visual Studio/AIOrchestrator-relations/kit"
claude plugin install aiorch@aiorch-local
```

- [ ] **Step 2: Verify against a RESTARTED session, not against the source**

Confirm the installed `watcher.md` contains the ticket script:

```bash
grep -c "TICKET MODE" ~/.claude/plugins/cache/aiorch-local/aiorch/*/skills/supervisor/reference/watcher.md
```
Expected: `1`.

- [ ] **Step 3: Run the baseline before flipping the gate**

```bash
python3 tools/wake-baseline/baseline.py --root ~/.claude/supervision
```

Record the output in the report file. **Then** set `"wake": "ticket"` for one role on one machine, run for two days, and run it again.

- [ ] **Step 4: Write the report**

`docs/superpowers/plans/2026-09-15-one-wake-model-01-report.md`: the before and after figures, which tests were red and why, which copy of each file was read, and what is left open.

- [ ] **Step 5: Commit**

```bash
git add docs/superpowers/plans/2026-09-15-one-wake-model-01-report.md
git commit -F /tmp/cm.txt   # "docs(plans): the one-wake-model report — the figures, the reds, and the copies read"
```

---

## Acceptance for the whole plan

- `dotnet test` green apart from the two known-red families named in Global Constraints.
- `bash kit/hooks/watcher-behaviour-check.sh` green, **with its own not-found guard verified to still fire.**
- Every one of the six existing digest/notes/boot-turn oracles listed in Task 6 Step 7 passes **with no edit to those files**.
- On a ticket-mode terminal orchestration: zero tickets written for `FROM app` entries; member reports inside the digest window produce one ticket, not one each; the ticket names a pack that exists and holds the brief.
- `baseline.py` run before and after, both recorded in the report.
- Nothing deleted: the fingerprint watcher still ships, and `runners.<role>.wake` defaults to `watcher`.

## What is deliberately NOT in this plan

Spec steps 3 (bookkeeping out of the channels), 4 (the routed report), 5 (the skill diet) and 6 (the
supervisor goes fresh) each get their own plan. Steps 3 and 6 depend on interfaces this plan creates
and cannot be written truthfully until it lands. Steps 4 and 5 depend on nothing here and may be
planned and executed in parallel with it.
