# Fork Merge — Plan 01: Prepare, Merge, Re-port — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Integrate `nathanthegrey/AIOrchestrator` (`ours/integration` @ `dbb6e4e`) into master on one integration branch, resolving toward the fork's structure, then re-port every master feature since the merge base so that both test suites are green on Windows and Linux and both hosts run.

**Architecture:** One `git merge` with a per-file resolution rule (the fork's version wins in every conflicted file; master's intent is re-added by hand in the tasks that follow), then one task per master feature, each carrying its own tests back from a temporary `_pending-reports` folder that keeps the solution compiling in between. The fork's runner seam (`ISessionRunner` / `ISessionLaunch`) is the attachment point for master's terminal-only features; the fork's engine is the attachment point for master's bridge features.

**Tech Stack:** .NET 10 (`net10.0`, WPF app `net10.0-windows`), xUnit, git 2.39+, bash (msys on Windows), `jq`, GitHub Actions.

**Spec:** `docs/superpowers/specs/2026-09-11-fork-merge-and-per-user-profiles-design.md` — sections 5 (the merge), 4.4 and 9 (test health), 10 (phases 0 and 1). This plan implements phases 0 and 1 only. Phases 2–6 (catalogue, seams, renderers, kit, language) are separate plans, written after this one lands.

## Global Constraints

- Work on branch `integration/fork-merge`, created from `master` in a git worktree at `../AIOrchestrator-merge`. Never commit to `master`; the owner merges.
- The fork is fetched into the main repo as remote `fork`; merge base is `50a6d8d`; the fork tip is `fork/ours/integration` = `dbb6e4e`.
- `AIOrchestratorCoreLib` is strict (triples `IXxx` / `XxxModel` / `Xxx_Factory`, `Verb_Object` methods, `_OrNull` for anything that may not resolve, no `record` types, XML docs argue the why with dated incidents). The WPF project is UI-relaxed. See `.claude/rules/code-conventions.md` on the fork (it becomes the repo's rule after the merge).
- Stage by explicit path; never `git add -A` / `.` / `commit -a`. Multi-line commit messages via `git commit -F <tempfile>`. Every commit ends with `Co-Authored-By: Claude Fable 5.1 <noreply@anthropic.com>`.
- One suite at a time on this machine (the fork's rule: reds under load are isolated with `--filter` before being believed). Compare the SET OF NAMES of red tests before and after, never the count.
- The running app is a fourth copy (`Get-Process AIOrchestrator | Select Path`); nothing in this plan touches it. Do not build inside the main checkout while the app holds `AIOrchestratorCoreLib.dll`; build in the worktree.
- `python3` on this machine is native Windows Python: hand it Windows paths. Bash heredocs over ~6 KB fail; write scripts with the Write tool and run the file.
- Say which copy you read in every report: branch source, build output, or installed.

---

## File Structure

**Created on the integration branch**

- `AIOrchestratorCoreLib.Tests/_pending-reports/` — master-only test files that do not compile against the fork's engine, parked with `<Compile Remove>` until their feature is re-ported (Task 2 creates it, Tasks 3–9 empty it, Task 14 deletes it).
- `docs/superpowers/plans/2026-09-11-fork-merge-01-report.md` — the re-port ledger written by Task 2: one row per master change that the merge dropped, ticked by the task that restores it.

**Modified (fork structure, master intent re-added)**

| file | responsibility after the merge |
|---|---|
| `AIOrchestratorCoreLib/Running/SessionLaunch/ISessionLaunch.cs`, `SessionLaunchModel.cs`, `SessionLaunch_Factory.cs` | everything a runner needs to start one session — gains `ResumeSessionId` and `Effort` |
| `AIOrchestratorCoreLib/Running/SessionRunner/TerminalRunnerModel.cs` | the terminal runner; passes effort and resume id into `SpawnCommand_Builder` |
| `AIOrchestratorCoreLib/Spawning/SpawnCommand_Builder.cs` | the one chokepoint that emits `claude` flags: `--resume`, `--model`, `--effort` (master's signature, the fork's `Validate_Model`) |
| `AIOrchestratorCoreLib/Spawning/ResumableSession_Resolver.cs` | master's file, verbatim |
| `AIOrchestratorCoreLib/Launching/OrchestrationLauncher/OrchestrationLauncherModel.cs` | resolves runner per role; on respawn resolves the resume id for terminal supervisor/solo |
| `AIOrchestratorCoreLib/Sessions/OrchestrationSession/*`, `Sessions/SessionJson_Serializer.cs`, `Sessions/OrchestrationSessionStore/*` | session.json gains `paused`, `supervisorEffortOverride`, `implementerEffortOverride` beside the fork's topic-delete and per-member model fields |
| `AIOrchestratorCoreLib/Bridge/BridgeEngine/BridgeEngineModel.cs` | the fork's engine, plus master's hold, credit, pause, dial handlers |
| `AIOrchestratorCoreLib/Bridge/OwnerPush_Policy.cs` | the fork's push policy plus master's `Is_TurnEndDeclaration` |
| `AIOrchestratorCoreLib/Mirroring/MirrorText_Formatter.cs` | one `Format_Parts` |
| `AIOrchestratorCoreLib/Telegram/BotCommandMenu.cs` | the command list gains `/model`, `/effort`, `/pause` |
| `AIOrchestratorCoreLib/Status/PausedFlag_Marker.cs` | master's file, verbatim |
| `kit/skills/{solo,supervisor,general-supervisor}/SKILL.md` | the fork's skills plus master's ONE-QUESTION and RESUMED paragraphs |
| `kit/hooks/supervisor-awaiting-answer-check.sh`, `run-to-the-end-check.sh`, `supervisor-ledger-check.sh` | master's role gate (solo), the fork's root variable, the `.paused` exit |
| `kit/statusline/statusline.sh`, `statusline.ps1` | effort beside the model on both; UTF-8 output on Windows |
| `.github/workflows/windows-build.yml` | builds AND tests on `windows-latest` and `ubuntu-latest`, with `jq` |
| `AIOrchestratorCoreLib.Tests/TestSupport/ChannelAppendTool.cs` | scrubs `AIORCH_*` from the child environment |
| `AIOrchestratorCoreLib/Running/PrintSessionState/*` (reader) | tolerates a concurrent writer on Windows |
| `AIOrchestratorCoreLib/Bridge/ChannelChangeWaker/ChannelChangeWakerModel.cs` | a deleted-root Error event on Windows is the same fact as "root gone" |
| `CLAUDE.md` | decisions 8, 11, 17 rewritten for the merged repo |

---

### Task 1: Windows prerequisites, two-OS CI, and the environment scrub

**Files:**
- Modify: `.github/workflows/windows-build.yml`
- Modify: `AIOrchestratorCoreLib.Tests/TestSupport/ChannelAppendTool.cs` (the `ProcessStartInfo` at ~line 49)
- Test: `AIOrchestratorCoreLib.Tests/Channels/ChannelAppendHelperInteropTests.cs` (exists; 13 tests)

**Interfaces:**
- Consumes: nothing from earlier tasks. This task runs on the FORK tree first (the worktree is created in Task 2), so do it on a throwaway branch `prep/windows-ci` off `fork/ours/integration` and cherry-pick it onto the integration branch in Task 2 Step 9.
- Produces: a CI that runs `dotnet test` on both OSes; a fixture that cannot inherit this session's `AIORCH_*`.

- [ ] **Step 1: Install jq on this machine and prove the typed-entry gate opens**

Run (PowerShell): `winget install --id jqlang.jq -e --accept-source-agreements --accept-package-agreements`
Then (bash): `which jq && jq --version`
Expected: a path under `/c/Users/Gianpiero/AppData/Local/Microsoft/WinGet/...` or `/c/Program Files/...` and `jq-1.7` or newer. If `which jq` prints nothing, add the WinGet links folder to the PATH bash sees (`~/.bashrc`: `export PATH="$PATH:/c/Users/Gianpiero/AppData/Local/Microsoft/WinGet/Links"`) and open a fresh shell.

- [ ] **Step 2: Write the failing fixture test — the child must not see the parent's AIORCH_ROLE**

Add to `AIOrchestratorCoreLib.Tests/Channels/ChannelAppendHelperInteropTests.cs`:

```csharp
[Fact]
public void TheTool_DoesNotInheritTheParentsOrchestrationIdentity()
{
    // The interop fixture drives the real bash tool. On 2026-09-11 the suite ran inside a solo
    // session whose shell carried AIORCH_ROLE=solo / AIORCH_MEMBER=solo-1, the child inherited
    // them, and eleven tests were REFUSED with "--author 'implementer' is not this session's
    // identity". A fixture that lets the parent's identity leak tests the parent, not the tool.
    Environment.SetEnvironmentVariable("AIORCH_ROLE", "solo");
    Environment.SetEnvironmentVariable("AIORCH_MEMBER", "solo-1");
    try
    {
        using var folder = new TemporaryFolder();
        var channel = Path.Combine(folder.Path, "channel.md");
        File.WriteAllText(channel, "# CHANNEL\n\n---\n");
        var result = ChannelAppendTool.Run(channel, author: "implementer", subject: "hello", body: "body",
            environment: new Dictionary<string, string> { ["AIORCH_ROLE"] = "implementer", ["AIORCH_MEMBER"] = "imp-1" });
        Assert.Equal(0, result.ExitCode);
        Assert.Contains("## [1] FROM implementer", File.ReadAllText(channel));
    }
    finally
    {
        Environment.SetEnvironmentVariable("AIORCH_ROLE", null);
        Environment.SetEnvironmentVariable("AIORCH_MEMBER", null);
    }
}
```

If `ChannelAppendTool.Run` has a different parameter list, keep its existing parameters and add an optional `IReadOnlyDictionary<string,string>? environment = null`; read the existing signature at the top of `TestSupport/ChannelAppendTool.cs` before editing.

- [ ] **Step 3: Run the test to verify it fails**

Run: `dotnet test AIOrchestratorCoreLib.Tests --filter "FullyQualifiedName~TheTool_DoesNotInheritTheParentsOrchestrationIdentity"`
Expected: FAIL — exit code 4 and the REFUSED line in stderr (the child saw `solo`).

- [ ] **Step 4: Scrub the child environment in the tool**

In `TestSupport/ChannelAppendTool.cs`, where `new ProcessStartInfo` is built, before applying the test's own variables:

```csharp
// The tool derives the author from AIORCH_ROLE / AIORCH_MEMBER. A test host started from inside
// an orchestrated shell inherits that shell's identity, so every AIORCH_* is removed first and only
// what the TEST states is exported — measured 2026-09-11, eleven refusals from one leaked pair.
foreach (var key in startInfo.Environment.Keys.Where(k => k.StartsWith("AIORCH_", StringComparison.Ordinal)).ToList())
    startInfo.Environment.Remove(key);
if (environment is not null)
    foreach (var (key, value) in environment)
        startInfo.Environment[key] = value;
```

- [ ] **Step 5: Run the interop class to verify it passes**

Run: `dotnet test AIOrchestratorCoreLib.Tests --filter "FullyQualifiedName~ChannelAppendHelperInteropTests"`
Expected: 14 passed, 0 failed (13 existing + the new one), with `AIORCH_ROLE=solo` still set in the parent shell.

- [ ] **Step 6: Extend the workflow to test on both OSes**

Replace the `jobs:` section of `.github/workflows/windows-build.yml` with:

```yaml
jobs:
  wpf:
    runs-on: windows-latest
    steps:
      - uses: actions/checkout@v4
      - uses: actions/setup-dotnet@v4
        with:
          dotnet-version: '10.0.x'
      # The app first, alone, so a failure names the project that cannot be built anywhere else.
      - name: Build the WPF app
        run: dotnet build AIOrchestrator/AIOrchestrator.csproj -c Debug -warnaserror:CS
      - name: Build the solution
        run: dotnet build AIOrchestrator.slnx -c Debug

  # The suite on both OSes. Until 2026-09-11 it had never run on Windows in CI and the first local
  # run there was 42 red; a suite that runs on one OS certifies one OS.
  tests:
    strategy:
      fail-fast: false
      matrix:
        os: [windows-latest, ubuntu-latest]
    runs-on: ${{ matrix.os }}
    steps:
      - uses: actions/checkout@v4
      - uses: actions/setup-dotnet@v4
        with:
          dotnet-version: '10.0.x'
      - name: Install jq (Windows)
        if: runner.os == 'Windows'
        run: choco install jq -y --no-progress
      - name: Install jq (Linux)
        if: runner.os == 'Linux'
        run: sudo apt-get update && sudo apt-get install -y jq
      - name: Build
        run: dotnet build AIOrchestrator.slnx -c Debug
      - name: Test CoreLib
        run: dotnet test AIOrchestratorCoreLib.Tests/AIOrchestratorCoreLib.Tests.csproj --no-build -c Debug --logger "trx;LogFileName=corelib-${{ matrix.os }}.trx"
      - name: Test claude-contract
        run: dotnet test tools/claude-contract/ClaudeContract.Tests/ClaudeContract.Tests.csproj --no-build -c Debug
      - uses: actions/upload-artifact@v4
        if: always()
        with:
          name: test-results-${{ matrix.os }}
          path: '**/TestResults/*.trx'
```

Also update the header comment: delete the sentence "CoreLib, the daemon and the suite are built and run continuously on the machines people work from, so repeating them here would buy nothing" and replace it with "The suite runs here on both OSes because a suite that runs on one OS certifies one OS (2026-09-11: 42 red on the first Windows run)."

- [ ] **Step 7: Validate the YAML**

Run: `python3 -c "import yaml,sys; yaml.safe_load(open(r'C:\path\to\.github\workflows\windows-build.yml')); print('ok')"` (use the Windows path of the file; install `pyyaml` with `python3 -m pip install pyyaml` if missing).
Expected: `ok`.

- [ ] **Step 8: Commit**

```bash
git add .github/workflows/windows-build.yml AIOrchestratorCoreLib.Tests/TestSupport/ChannelAppendTool.cs AIOrchestratorCoreLib.Tests/Channels/ChannelAppendHelperInteropTests.cs
git commit -F <tempfile>   # "ci(tests): the suite runs on both OSes, and the interop fixture scrubs AIORCH_*"
```

---

### Task 2: The merge — resolve toward the fork, keep the solution compiling, ledger what was dropped

**Files:**
- Create: worktree `../AIOrchestrator-merge` on branch `integration/fork-merge` from `master`
- Create: `AIOrchestratorCoreLib.Tests/_pending-reports/` (parked tests) and the `<Compile Remove>` item in `AIOrchestratorCoreLib.Tests/AIOrchestratorCoreLib.Tests.csproj`
- Create: `docs/superpowers/plans/2026-09-11-fork-merge-01-report.md` (the re-port ledger)
- Modify: `AIOrchestratorCoreLib/Mirroring/MirrorText_Formatter.cs` (the CS0111)
- Modify: every conflicted file (list in Step 3)

**Interfaces:**
- Consumes: Task 1's commit on `prep/windows-ci` (cherry-picked in Step 9).
- Produces: a compiling merge commit whose engine, launcher, sessions, Telegram and kit are the fork's; a ledger of every master change dropped by the resolution, consumed by Tasks 3–11.

- [ ] **Step 1: Create the worktree and branch**

```bash
cd /c/Users/Gianpiero/source/repos/AIOrchestrator
git fetch fork
git worktree add -b integration/fork-merge ../AIOrchestrator-merge master
cd ../AIOrchestrator-merge
git log --oneline -1          # a58ef7e
git merge-base master fork/ours/integration   # 50a6d8d
```

- [ ] **Step 2: Start the merge without committing**

```bash
git merge --no-commit --no-ff fork/ours/integration
git status --short | grep -E '^(UU|AA|DU|UD|AU|UA)' | sort
```
Expected: 19 paths (Appendix B of the spec). If the list differs, stop and record the difference in the report before continuing — a different list means the branches moved since the spec was written.

- [ ] **Step 3: Resolve every conflicted file toward the fork, with the three exceptions**

For each of these, take the fork's version whole:

```bash
for f in \
  AIOrchestratorCoreLib/Bridge/BridgeEngine/BridgeEngineModel.cs \
  AIOrchestratorCoreLib/Launching/OrchestrationLauncher/OrchestrationLauncherModel.cs \
  AIOrchestratorCoreLib/Sessions/OrchestrationSession/OrchestrationSession_Factory.cs \
  AIOrchestratorCoreLib/Sessions/OrchestrationSession/OrchestrationSessionModel.cs \
  AIOrchestratorCoreLib/Sessions/OrchestrationSessionStore/IOrchestrationSessionStore.cs \
  AIOrchestratorCoreLib/Sessions/SessionJson_Serializer.cs \
  AIOrchestratorCoreLib/Spawning/SpawnCommand_Builder.cs \
  AIOrchestratorCoreLib/Bridge/OwnerPush_Policy.cs \
  AIOrchestratorCoreLib/Telegram/TelegramDeliveryModes.cs \
  AIOrchestratorCoreLib/Telegram/TopicStatusLine_Builder.cs \
  AIOrchestratorCoreLib/Telegram/TopicStatusLine_Planner.cs \
  AIOrchestratorCoreLib/Telegram/TelegramApiClient/TelegramApiClientModel.cs \
  AIOrchestratorCoreLib/Telegram/TopicCommandButtons.cs \
  AIOrchestratorCoreLib.Tests/Telegram/TopicCommandButtonsTests.cs \
  AIOrchestratorCoreLib.Tests/Bridge/AwaySuppressesAppAlertsScanTests.cs \
  AIOrchestratorCoreLib.Tests/Bridge/OwnerAnswerSurvivesFailedSendTests.cs \
  AIOrchestratorCoreLib.Tests/Configuration/OrchestratorConfigFactoryTests.cs \
  AIOrchestratorCoreLib.Tests/Spawning/SpawnCommandBuilderTests.cs ; do
  git checkout --theirs -- "$f" && git add "$f"
done
```

The three exceptions, resolved by hand (open the file, keep BOTH sides as described, delete the markers):

1. `kit/skills/solo/SKILL.md` — keep the fork's file; inside the conflict hunk keep master's **ONE OPEN QUESTION AT A TIME** block (from `kit/commands/solo.md` on master) in the position the fork's text would have it, right after the brevity rules. Do NOT bring master's `QUESTION:`/`OPTION:` hand-written templates — the fork's skills say "call the tool with `--question --option`", and that wording stays.
2. `kit/hooks/run-to-the-end-check.sh` and `kit/hooks/supervisor-awaiting-answer-check.sh` — if they conflict (they auto-merged in the trial): keep the fork's `SUPERVISION_ROOT="${AIORCH_SUPERVISION_ROOT:-$HOME/.claude/supervision}"` lines AND master's additions (`.paused` exit; `case "$AIORCH_ROLE" in supervisor|solo)`). Task 5 and Task 6 verify the result.
3. `CLAUDE.md` — auto-merged in the trial; if it conflicts keep both sides' decision lists; Task 13 rewrites the merged decisions.

- [ ] **Step 4: Record what the resolution dropped — the re-port ledger**

Create `docs/superpowers/plans/2026-09-11-fork-merge-01-report.md`:

```markdown
# Re-port ledger — master changes dropped by the 2026-09-11 merge resolution

Every conflicted file took the fork's version whole (Task 2). Each row below is a master change that
was in that file and is restored by the named task. A row is ticked by the task that restores it,
with the commit hash. Nothing here is optional: an unticked row at the end of plan 01 is a
regression the owner will meet on the phone.

| master change (commit) | file(s) | restored by | done |
|---|---|---|---|
| `--resume` on respawn for terminal supervisor/solo (5e7ed6b) | SpawnCommand_Builder, OrchestrationLauncherModel, ResumableSession_Resolver | Task 3 | [ ] |
| effort flag + role default xhigh for supervisor/solo (60671ac, 39fc6b3) | SpawnCommand_Builder, session model/serializer/store | Task 3, Task 4 | [ ] |
| `/model` `/effort` from the phone, Apply_Dial, role picker (7bdc55e, 81038e4, 7f658e0) | BridgeEngineModel, Telegram/ModelEffort*, BotCommandMenu | Task 4 | [ ] |
| owner `/pause` and 💤 (a2c9a3d) | session model/serializer/store, PausedFlag_Marker, TelegramDeliveryModes, run-to-the-end hook | Task 5 | [ ] |
| question hold in the mirror loop (e637cb2) | BridgeEngineModel (MirrorOutcomes, heldChannels), QuestionHold_Policy, awaiting-answer hook (solo) | Task 6 | [ ] |
| owner-answer credit: Raise_OwnerWait, Is_TurnEndDeclaration, /merge opens the tracker (58ff547) | BridgeEngineModel, OwnerPush_Policy | Task 7 | [ ] |
| model + effort reading types and formatter (7f658e0, 411fa21) | Status/SessionModelReading, Formatting/ModelReading_Formatter, statusline effort | Task 8 | [ ] |
| reply keyboard markup (65107e9) | ReplyKeyboard_Markup, TopicCommandButtons rows | Task 9 (compiles, not wired — spec §7.6) | [ ] |
| kit prose: ONE QUESTION (solo, general-supervisor), "the app holds the channel" (supervisor), RESUMED paragraphs (supervisor, solo) | kit/skills | Task 10 | [ ] |
| pictures as pictures (da8f66c) | superseded by the fork's EntryAttachment_Policy; master's tests re-homed | Task 11 | [ ] |
| Fable 5.1 default (60671ac) | moved into the `classic` preset by plan 02; the shipped default is Opus (spec §11.4) | plan 02 | n/a |
| collapsing command bar / button catalogue (65107e9) | superseded by `pulse.buttons` in plan 03 | plan 03 | n/a |
```

- [ ] **Step 5: Fix the CS0111 in `MirrorText_Formatter`**

Open `AIOrchestratorCoreLib/Mirroring/MirrorText_Formatter.cs` (merged blob has two `Format_Parts`). Keep the fork's `(string Speaker, string Content) Format_Parts(IDiscoveredChannel channel, IChannelEntry entry)`; delete master's `(string Prefix, string Content)` overload. Then diff master's body against the fork's (`git show master:AIOrchestratorCoreLib/Mirroring/MirrorText_Formatter.cs`) and fold in any `IMAGE:` handling the fork's version lacks — the fork's already lifts `QUESTION:`/`OPTION:`/`IMAGE:` marker lines (verified 2026-09-11), so expect nothing to fold; record "nothing folded" in the commit body if so.

- [ ] **Step 6: Build; park every master-only file that does not compile**

Run: `dotnet build AIOrchestrator.slnx -c Debug 2>&1 | grep -E "error CS" | sort -u`

For each error in a file that is **master-only since the base** (the list: `Bridge/QuestionHold_Policy.cs`, `Formatting/ModelReading_Formatter.cs`, `Spawning/ResumableSession_Resolver.cs`, `Status/PausedFlag_Marker.cs`, `Status/SessionModelReading/*`, `Telegram/EffortLevels.cs`, `Telegram/ModelChoices.cs`, `Telegram/ModelEffortButton_Data.cs`, `Telegram/ModelEffortCommand_Parser.cs`, `Telegram/ModelEffortPrompt_Builder.cs`, `Telegram/ReplyKeyboard_Markup.cs`; and the 19 master-only test files listed in Task 2 Step 7): production files stay in place and are FIXED (they are small and their dependencies exist — e.g. `ResumableSession_Resolver` needs `RateLimits_Reader.Read_SessionId_OrNull`, which arrived with the merge because master alone changed `Limits/RateLimits_Reader.cs`); test files are PARKED (Step 7). For an error in any other file, it is a resolution mistake: fix it in place and add a row to the ledger saying what it was.

Repeat until `dotnet build` reports 0 errors for the production projects.

- [ ] **Step 7: Park the master-only tests that do not compile**

```bash
mkdir -p AIOrchestratorCoreLib.Tests/_pending-reports
for f in $(dotnet build AIOrchestratorCoreLib.Tests -c Debug 2>&1 | grep -oE 'AIOrchestratorCoreLib\.Tests[\\/][^(]+\.cs' | sort -u); do
  git mv "$f" "AIOrchestratorCoreLib.Tests/_pending-reports/$(basename "$f")"
done
```
Add to `AIOrchestratorCoreLib.Tests/AIOrchestratorCoreLib.Tests.csproj` inside an `<ItemGroup>`:
```xml
<!-- Master-only tests parked by the 2026-09-11 merge until their feature is re-ported (plan 01,
     Tasks 3-11). Each task moves its files back; Task 14 deletes this folder and this item. -->
<Compile Remove="_pending-reports\**" />
```
Run: `dotnet build AIOrchestrator.slnx -c Debug`
Expected: 0 errors. Record the parked file names in the ledger's rows (each row names the tests it must un-park).

- [ ] **Step 8: Run the suite once, record the red set (do not fix anything)**

Run: `dotnet test AIOrchestratorCoreLib.Tests --no-build -c Debug 2>&1 | tee ../merge-baseline-tests.log | tail -5`
Then: `grep -E "^\s+Failed " ../merge-baseline-tests.log | sort > ../merge-baseline-red.txt; wc -l ../merge-baseline-red.txt`
Expected: some reds — the Windows set of spec §4.4 plus whatever the resolution broke. Paste the red NAMES into the ledger under a heading `## Red after the merge commit (Windows, <date>)`. This list is the baseline every later task compares against.

- [ ] **Step 9: Commit the merge, then cherry-pick Task 1**

```bash
git add docs/superpowers/plans/2026-09-11-fork-merge-01-report.md AIOrchestratorCoreLib.Tests/AIOrchestratorCoreLib.Tests.csproj AIOrchestratorCoreLib.Tests/_pending-reports AIOrchestratorCoreLib/Mirroring/MirrorText_Formatter.cs
git commit -F <tempfile>   # "merge: fork/ours/integration @ dbb6e4e into master — resolved toward the fork's structure; master's intent ledgered for re-port"
git cherry-pick <Task 1 commit>
dotnet build AIOrchestrator.slnx -c Debug   # 0 errors
```

---

### Task 3: Re-port `--resume` and effort for terminal sessions onto the runner seam

**Files:**
- Modify: `AIOrchestratorCoreLib/Running/SessionLaunch/ISessionLaunch.cs`, `SessionLaunchModel.cs`, `SessionLaunch_Factory.cs`
- Modify: `AIOrchestratorCoreLib/Running/SessionRunner/TerminalRunnerModel.cs`
- Modify: `AIOrchestratorCoreLib/Spawning/SpawnCommand_Builder.cs`
- Modify: `AIOrchestratorCoreLib/Running/ResumeModes.cs` (docstring)
- Modify: `AIOrchestratorCoreLib/Launching/OrchestrationLauncher/OrchestrationLauncherModel.cs` (`Respawn_Supervisor`, `Respawn_Member`, the `SessionLaunch_Factory.Create` call sites)
- Un-park: `_pending-reports/ResumableSessionResolverTests.cs` → `Spawning/`; the resume/effort cases of master's `SpawnCommandBuilderTests.cs` and `OrchestrationLauncherTests.cs` (both files exist on the fork — add master's cases to them; `git show master:AIOrchestratorCoreLib.Tests/Spawning/SpawnCommandBuilderTests.cs`)

**Interfaces:**
- Consumes: `ResumableSession_Resolver.Resolve_ForSupervisor_OrNull(ISupervisionPaths, string orchId)`, `Resolve_ForMember_OrNull(ISupervisionPaths, string orchId, string memberId)` (master's file, now in the tree); `RoleRunnerConfig.Resume` (`ResumeModes.Transcript | Fresh`); `Runner_Support.Is_BridgeDriven(SessionRunners)`.
- Produces: `ISessionLaunch.ResumeSessionId : string?`, `ISessionLaunch.Effort : string?`; `SessionLaunch_Factory.Create(SessionRoles role, string orchId, string memberId, string workingDirectory, string? model, string pidFilePath, string? displayName, string? effort, string? resumeSessionId)`; `SpawnCommand_Builder.Build_ClaudeInvocation(string? resumeSessionId, string? model, string? effort)` (public, as on master); `SpawnCommand_Builder.SUPERVISION_EFFORT_LEVEL = "xhigh"` (until plan 02 moves it into the catalogue); `Build_ForSupervisor(orchId, repoPath, model, effort, resumeSessionId, pidFilePath, displayName)`, `Build_ForSolo(orchId, memberId, repoPath, model, effort, resumeSessionId, pidFilePath, displayName)`, `Build_ForReviewer(orchId, memberId, repoPath, model, effort, pidFilePath, displayName)`, `Build_ForImplementer(orchId, memberId, repoPath, model, effort, pidFilePath, displayName)`.

- [ ] **Step 1: Write the failing builder test — the invocation carries resume, model and effort in that order**

Add to `AIOrchestratorCoreLib.Tests/Spawning/SpawnCommandBuilderTests.cs` (copy master's cases: `git show master:AIOrchestratorCoreLib.Tests/Spawning/SpawnCommandBuilderTests.cs | grep -n "resume\|effort" ` lists them; bring every `[Fact]` whose name mentions Resume or Effort). The two that pin the contract:

```csharp
[Fact]
public void ASupervisorRespawn_ResumesItsOwnConversation_AndCarriesTheRoleEffort()
{
    var command = SpawnCommand_Builder.Build_ForSupervisor("repo-3", @"C:\repo", "claude-fable-5-1", effort: null,
        resumeSessionId: "0f1e2d3c-4b5a-6978-8a9b-0c1d2e3f4a5b", pidFilePath: @"C:\pid", displayName: null);
    var script = SpawnCommand_Builder.Decode_SessionScript(command);
    Assert.Contains("claude --resume 0f1e2d3c-4b5a-6978-8a9b-0c1d2e3f4a5b --model claude-fable-5-1 --effort xhigh '/supervisor repo-3'", script);
}

[Fact]
public void AnImplementer_NeverResumes_AndHasNoEffortUnlessOverridden()
{
    var command = SpawnCommand_Builder.Build_ForImplementer("repo-3", "imp-1", @"C:\repo", "sonnet", effort: null, pidFilePath: @"C:\pid", displayName: null);
    var script = SpawnCommand_Builder.Decode_SessionScript(command);
    Assert.DoesNotContain("--resume", script);
    Assert.DoesNotContain("--effort", script);
    Assert.Contains("--model sonnet", script);
}
```

- [ ] **Step 2: Run to verify they fail**

Run: `dotnet test AIOrchestratorCoreLib.Tests --filter "FullyQualifiedName~SpawnCommandBuilderTests"`
Expected: compile errors — the fork's `Build_ForSupervisor` has no `effort`/`resumeSessionId` parameters.

- [ ] **Step 3: Widen `SpawnCommand_Builder` to master's signatures, keeping the fork's `Validate_Model`**

Take master's versions of these members verbatim (`git show master:AIOrchestratorCoreLib/Spawning/SpawnCommand_Builder.cs`): `SUPERVISION_EFFORT_LEVEL`, `Build_ForSupervisor`, `Build_ForReviewer`, `Build_ForSolo`, `Build_ForImplementer`, `Build_ClaudeInvocation(string? resumeSessionId, string? model, string? effort)`, `Validate_ResumeSessionId`, `Resolve_Effort_OrDefault`, and the XML docs that argue them. Inside `Build_ClaudeInvocation`, keep the fork's `Validate_Model(model);` call as the first line of the model branch — master has no escape check and the fork's must survive. The emitted order is `claude --resume <id> --model <m> --effort <e> <CLAUDE_LAUNCH_FLAGS>` and the reviewer's `--disallowedTools` list stays after the flags, as on master.

- [ ] **Step 4: Widen `ISessionLaunch`**

`ISessionLaunch.cs` — add after `DisplayName`:

```csharp
/// <summary>
/// `--effort` for this session, or null for the CLI's own default. A role default (xhigh for the
/// supervisor and the solo) and a per-orchestration override both arrive here already resolved;
/// the runner never decides effort, it only carries it. Bridge-driven runners ignore it until the
/// dispatcher's turn command learns the flag.
/// </summary>
string? Effort { get; }

/// <summary>
/// The Claude session id to `--resume`, or null for a fresh conversation. Set ONLY for a terminal
/// supervisor or solo whose previous transcript still exists (ResumableSession_Resolver) — a
/// `--resume` of an unknown id prints "No conversation found" and exits, which under the watchdog
/// is a respawn loop (owner request 2026-09-10). Never `--continue`.
/// </summary>
string? ResumeSessionId { get; }
```

`SessionLaunchModel.cs` — two get-only properties set from the primary constructor. `SessionLaunch_Factory.Create` — two trailing parameters `string? effort = null, string? resumeSessionId = null` so every existing call site compiles unchanged.

- [ ] **Step 5: Pass them through the terminal runner**

`TerminalRunnerModel.Build_Command`:

```csharp
SessionRoles.Supervisor  => SpawnCommand_Builder.Build_ForSupervisor(launch.OrchId, launch.WorkingDirectory, launch.Model, launch.Effort, launch.ResumeSessionId, launch.PidFilePath, launch.DisplayName),
SessionRoles.Reviewer    => SpawnCommand_Builder.Build_ForReviewer(launch.OrchId, launch.MemberId, launch.WorkingDirectory, launch.Model, launch.Effort, launch.PidFilePath, launch.DisplayName),
SessionRoles.Solo        => SpawnCommand_Builder.Build_ForSolo(launch.OrchId, launch.MemberId, launch.WorkingDirectory, launch.Model, launch.Effort, launch.ResumeSessionId, launch.PidFilePath, launch.DisplayName),
SessionRoles.Implementer => SpawnCommand_Builder.Build_ForImplementer(launch.OrchId, launch.MemberId, launch.WorkingDirectory, launch.Model, launch.Effort, launch.PidFilePath, launch.DisplayName),
```
(Communicator and General unchanged.)

- [ ] **Step 6: Run the builder tests to verify they pass**

Run: `dotnet test AIOrchestratorCoreLib.Tests --filter "FullyQualifiedName~SpawnCommandBuilderTests"`
Expected: all pass, including the fork's `Validate_Model` cases.

- [ ] **Step 7: Write the failing launcher test — a terminal supervisor respawn resolves the id, a print one does not**

Bring master's respawn cases from `git show master:AIOrchestratorCoreLib.Tests/Launching/OrchestrationLauncherTests.cs` (the ones named `*Resume*`), adapting the harness to the fork's `IReadOnlyList<ISessionRunner>` constructor (read the fork's test file first for the harness shape). Add one fork-specific case:

```csharp
[Fact]
public void ASupervisorConfiguredForThePrintRunner_IsNeverResumedByTheLauncher()
{
    // The bridge-driven runners keep their own ResumeModes (print-session.json); the launcher's
    // --resume is the TERMINAL runner's only. Writing a probe file for a print supervisor must not
    // make the launcher hand a resume id to a runner that ignores it.
    var harness = LauncherHarness.WithRunnerFor(SessionRoles.Supervisor, SessionRunners.Print);
    harness.WriteProbe("repo-3", sessionId: "0f1e2d3c-4b5a-6978-8a9b-0c1d2e3f4a5b", transcriptExists: true);
    harness.Launcher.Respawn_Supervisor("repo-3");
    Assert.Null(harness.LastLaunch.ResumeSessionId);
}
```
(`LauncherHarness` is whatever the fork's `OrchestrationLauncherTests` already uses to inject runners and paths — reuse it; add `WriteProbe` if absent, writing `.usage.json` with `session_id` and creating the transcript file the resolver checks.)

- [ ] **Step 8: Run to verify it fails**

Run: `dotnet test AIOrchestratorCoreLib.Tests --filter "FullyQualifiedName~OrchestrationLauncherTests"`
Expected: the new cases fail (no resolution happens yet, or `LastLaunch` has no such property).

- [ ] **Step 9: Resolve the resume id in the launcher, before the stale-pid delete, gated on the runner**

In `OrchestrationLauncherModel.Respawn_Supervisor`:

```csharp
var runner = Resolve_Runner(SessionRoles.Supervisor, orchId);
var resumeSessionId = runner.Kind == SessionRunners.Terminal
    && _configProvider.Get_Current().Runners.Get_ForRole(SessionRoles.Supervisor).Resume == ResumeModes.Transcript
    ? ResumableSession_Resolver.Resolve_ForSupervisor_OrNull(_paths, orchId)
    : null;
Delete_StalePidFile_BestEffort(...);   // the existing call, AFTER the resolve — the probe is read before anything is deleted
var effort = Resolve_Effort_ForSupervisor(session);   // master's: session.SupervisorEffortOverride ?? null (Task 4 adds the override; until then pass null and let the builder apply the role default)
runner.Start(SessionLaunch_Factory.Create(SessionRoles.Supervisor, orchId, "sup", session.RepoPath, model, pidFile, displayName, effort, resumeSessionId));
```
Same in `Respawn_Member` for `MemberKinds.Solo` only (master resumes solo, never reviewer/implementer): `Resolve_ForMember_OrNull(_paths, orchId, memberId)`. Log the decision with master's `Describe_Spawn` wording (`git show master:AIOrchestratorCoreLib/Launching/OrchestrationLauncher/OrchestrationLauncherModel.cs | grep -n Describe_Spawn`).

- [ ] **Step 10: Widen the `ResumeModes` docstring**

`Running/ResumeModes.cs`: replace "what a print-run session remembers between turns" with: "what a session remembers across its own restarts, whichever runner drives it: `Transcript` — a bridge-driven session `--resume`s its print session; a terminal supervisor or solo `--resume`s its own conversation when the probe names a transcript that still exists; `Fresh` — start empty. Implementers and reviewers in a terminal always start fresh regardless (the channel is their durable state, CLAUDE.md decision 8)."

- [ ] **Step 11: Un-park and run**

```bash
git mv AIOrchestratorCoreLib.Tests/_pending-reports/ResumableSessionResolverTests.cs AIOrchestratorCoreLib.Tests/Spawning/ResumableSessionResolverTests.cs
dotnet test AIOrchestratorCoreLib.Tests --filter "FullyQualifiedName~ResumableSessionResolverTests|FullyQualifiedName~OrchestrationLauncherTests|FullyQualifiedName~SpawnCommandBuilderTests|FullyQualifiedName~TerminalRunner"
```
Expected: all pass.

- [ ] **Step 12: Commit and tick the ledger rows**

```bash
git add AIOrchestratorCoreLib/Running/SessionLaunch AIOrchestratorCoreLib/Running/SessionRunner/TerminalRunnerModel.cs AIOrchestratorCoreLib/Spawning/SpawnCommand_Builder.cs AIOrchestratorCoreLib/Running/ResumeModes.cs AIOrchestratorCoreLib/Launching/OrchestrationLauncher/OrchestrationLauncherModel.cs AIOrchestratorCoreLib.Tests/Spawning AIOrchestratorCoreLib.Tests/Launching/OrchestrationLauncherTests.cs docs/superpowers/plans/2026-09-11-fork-merge-01-report.md
git commit -F <tempfile>   # "feat(spawn): terminal supervisor and solo resume their own conversation on the fork's runner seam; effort rides the launch"
```

---

### Task 4: Re-port the effort override, `/model` and `/effort` from the phone, `Apply_Dial`

**Files:**
- Modify: `AIOrchestratorCoreLib/Sessions/OrchestrationSession/IOrchestrationSession.cs`, `OrchestrationSessionModel.cs`, `OrchestrationSession_Factory.cs`; `Sessions/SessionJson_Serializer.cs`; `Sessions/OrchestrationSessionStore/IOrchestrationSessionStore.cs`, `OrchestrationSessionStoreModel.cs`
- Modify: `AIOrchestratorCoreLib/Launching/OrchestrationLauncher/OrchestrationLauncherModel.cs` (the effort resolution left as null in Task 3)
- Modify: `AIOrchestratorCoreLib/Bridge/BridgeEngine/BridgeEngineModel.cs` (command dispatch chain, `Handle_CallbackTap_Async`, the request-file `set-model` handler)
- Modify: `AIOrchestratorCoreLib/Telegram/BotCommandMenu.cs`
- Keep: `Telegram/ModelChoices.cs`, `EffortLevels.cs`, `ModelEffortButton_Data.cs`, `ModelEffortCommand_Parser.cs`, `ModelEffortPrompt_Builder.cs` (master's, in the tree since Task 2)
- Un-park: `ModelChoicesTests`, `EffortLevelsTests`, `ModelEffortButtonDataTests`, `ModelEffortCommandParserTests`, `ModelEffortPromptBuilderTests`, `SessionJsonSerializerTests` → their folders

**Interfaces:**
- Consumes: Task 3's `SessionLaunch_Factory.Create(..., effort, resumeSessionId)`; the fork's `IOrchestrationSession.{Supervisor,Implementer}ModelOverride`, `IOrchestrationMember.Model` (per-member card), `IOrchestratorConfig.Get_ModelForRole`; the fork's `Handle_CallbackTap_Async` ordering (close-confirm → hold → topic bar → generic `opt-`); `Runner_Support.Is_BridgeDriven`.
- Produces: `IOrchestrationSession.SupervisorEffortOverride : string?`, `ImplementerEffortOverride : string?`; `IOrchestrationSessionStore.Set_SupervisorEffortOverride(string orchId, string? effort)`, `Set_ImplementerEffortOverride(string orchId, string? effort)`; `OrchestrationSession_Factory.CreateFrom_Existing_WithSupervisorEffortOverride(IOrchestrationSession existing, string? effort)` and the implementer twin; session.json keys `supervisorEffortOverride`, `implementerEffortOverride`; engine `void Apply_Dial(string orchId, Telegram.ModelEffortKinds kind, string role, string value, string reason)`.

- [ ] **Step 1: Un-park the pure tests and run them**

```bash
for t in ModelChoicesTests EffortLevelsTests ModelEffortButtonDataTests ModelEffortCommandParserTests ModelEffortPromptBuilderTests; do git mv AIOrchestratorCoreLib.Tests/_pending-reports/$t.cs AIOrchestratorCoreLib.Tests/Telegram/$t.cs; done
git mv AIOrchestratorCoreLib.Tests/_pending-reports/SessionJsonSerializerTests.cs AIOrchestratorCoreLib.Tests/Sessions/SessionJsonSerializerTests.cs
dotnet test AIOrchestratorCoreLib.Tests --filter "FullyQualifiedName~ModelEffort|FullyQualifiedName~ModelChoices|FullyQualifiedName~EffortLevels|FullyQualifiedName~SessionJsonSerializerTests"
```
Expected: the five Telegram classes pass (pure); `SessionJsonSerializerTests` fails to compile on `SupervisorEffortOverride` / `Paused` — the serializer test also covers Task 5's `paused`; leave the `Paused` assertions in and let Task 5 make them green (until then run this class with `--filter "FullyQualifiedName~SessionJsonSerializerTests&FullyQualifiedName!~Paused"`).

- [ ] **Step 2: Add the effort override to the session triple, serializer and store**

Take master's members verbatim from `git show master:<path>` for each of the six files: the two properties on the interface (with their XML docs: "the effort override lives per role in session.json … reaches `claude --effort` ONLY when set — null means no flag"), the two constructor parameters and properties on the model, `CreateFrom_Existing_WithSupervisorEffortOverride` / `…WithImplementerEffortOverride` on the factory (master uses `*WasSet` flags to tell "clear" from "keep" — keep that shape), the two keys in `SessionJson_Serializer` (`Write` and `Read`), the two `Set_*EffortOverride` methods on the store interface and model. The fork's factory signature has three extra trailing parameters of its own (topic-delete fields, per-member model); add master's after them, both nullable with defaults, so the fork's call sites compile unchanged.

- [ ] **Step 3: Run the serializer tests to verify the effort cases pass**

Run: `dotnet test AIOrchestratorCoreLib.Tests --filter "FullyQualifiedName~SessionJsonSerializerTests&FullyQualifiedName!~Paused"`
Expected: PASS.

- [ ] **Step 4: Resolve effort in the launcher**

Replace the `null` effort left by Task 3 Step 9:

```csharp
// Owner override > (no member card effort yet — plan 02 adds it) > role default applied in the builder.
var supervisorEffort = session.SupervisorEffortOverride;   // null = the builder's SUPERVISION_EFFORT_LEVEL for supervisor/solo, no flag otherwise
```
and for members `session.ImplementerEffortOverride` (a solo sits on the implementer slot, as it does for the model — master's comment, keep it).

- [ ] **Step 5: Write the failing engine test — `/effort xhigh` on a print supervisor stores the override and does NOT kill a pid file**

Add `AIOrchestratorCoreLib.Tests/Bridge/EffortDialOnABridgeDrivenSupervisorTests.cs`, using the fork's engine harness (the same one `ThePhoneRingsOnlyForTheSupervisorTests` uses — read its constructor first):

```csharp
[Fact]
public async Task SlashEffort_OnAPrintSupervisor_StoresTheOverride_AndDoesNotTouchAPidFile()
{
    using var h = await EngineHarness.StartAsync(runnerFor: (SessionRoles.Supervisor, SessionRunners.Print));
    await h.OwnerSays("repo-3", "/effort xhigh");
    await h.OwnerTaps("repo-3", ModelEffortButton_Data.Build(ModelEffortKinds.Effort, "repo-3", ModelEffortButton_Data.SUPERVISOR_ROLE, "xhigh"));
    Assert.Equal("xhigh", h.Store.Get("repo-3").SupervisorEffortOverride);
    Assert.False(h.KillWasRequested("repo-3", "sup"));   // a print session has no pid file; Apply_Dial must not try
    Assert.Contains("effort", h.LastOwnerFacingAppEntry("repo-3"), StringComparison.OrdinalIgnoreCase);
}
```
If the harness has no `KillWasRequested`, add a recording stub around the `ISessionTerminator` (or whatever the fork's launcher uses to kill by pid) — read `OrchestrationLauncherModel`'s kill path first and stub that interface.

- [ ] **Step 6: Run to verify it fails**

Run: `dotnet test AIOrchestratorCoreLib.Tests --filter "FullyQualifiedName~EffortDialOnABridgeDrivenSupervisorTests"`
Expected: FAIL — `/effort` is not a command on the fork's engine.

- [ ] **Step 7: Re-port the engine members**

From `git show master:AIOrchestratorCoreLib/Bridge/BridgeEngine/BridgeEngineModel.cs`, copy: `Apply_Dial`, `Store_SupervisorDial`, `Store_ImplementerDial`, `Try_HandleModelEffortTap_Async`, `Dial_Verb`, `Describe_DialValue`, `Send_DialPrompt_Async`, the `/model` and `/effort` branches of the command dispatch (`ModelEffortCommand_Parser.Is_Command`), and the `set-model` request-file branch. Wire them into the fork's structure:
  - text commands: one `else if (ModelEffortCommand_Parser.Is_Command(command))` branch in the fork's dispatch chain (the chain of literal comparisons that ends near `"pending"` → `Send_PendingDecisions_Async`);
  - taps: `if (await Try_HandleModelEffortTap_Async(...)) return;` inserted in `Handle_CallbackTap_Async` **after** `Try_HandleTopicCommandTap_Async` and **before** the generic `opt-` path (the fork's comment there explains why permanent buttons go first — quote it in the new call's comment);
  - `Apply_Dial`: replace master's unconditional kill+respawn with
    ```csharp
    var kind = _launcher.Resolve_Runner(role, orchId).Kind;   // expose Resolve_Runner's Kind if private — one internal accessor
    if (!Runner_Support.Is_BridgeDriven(kind))
        _launcher.Kill_AndRespawn(orchId, role);            // terminal: the flag reaches claude only at spawn
    else
        _log.Log_Info(orchId, "override stored; a bridge-driven session picks it up at its next turn");
    ```
    (the bridge-driven turn command does not read effort yet — plan 02 adds the flag to `PrintTurnCommand_Builder`; say so in the log line).
  - sends go through the fork's `TelegramProse_Sender` with `TelegramSendSounds.Silent` (app prompts do not ring, spec §6.4 "phone.appMessagesRing" is not yet a setting — the fork's rule applies).

- [ ] **Step 8: Register the commands**

`Telegram/BotCommandMenu.cs`: add `("model", "model for this orchestration — buttons or a value")` and `("effort", "effort for this orchestration — buttons or a value")` to `ALL`, after `limits`. Update `BotCommandMenuTests` expected order (the test pins the list; that is its job).

- [ ] **Step 9: Run the new test and the touched classes**

Run: `dotnet test AIOrchestratorCoreLib.Tests --filter "FullyQualifiedName~EffortDial|FullyQualifiedName~BotCommandMenuTests|FullyQualifiedName~ModelEffort|FullyQualifiedName~OrchestrationLauncherTests"`
Expected: PASS.

- [ ] **Step 10: Commit and tick the ledger**

```bash
git add AIOrchestratorCoreLib/Sessions AIOrchestratorCoreLib/Launching/OrchestrationLauncher/OrchestrationLauncherModel.cs AIOrchestratorCoreLib/Bridge/BridgeEngine/BridgeEngineModel.cs AIOrchestratorCoreLib/Telegram/BotCommandMenu.cs AIOrchestratorCoreLib.Tests/Telegram AIOrchestratorCoreLib.Tests/Sessions AIOrchestratorCoreLib.Tests/Bridge/EffortDialOnABridgeDrivenSupervisorTests.cs docs/superpowers/plans/2026-09-11-fork-merge-01-report.md
git commit -F <tempfile>   # "feat(bridge): /model and /effort from the phone on the merged engine; the dial never kills a bridge-driven session"
```

---

### Task 5: Re-port the owner `/pause`

**Files:**
- Modify: the six session files of Task 4 (`Paused` field, `paused` key, `Set_Paused`)
- Keep: `AIOrchestratorCoreLib/Status/PausedFlag_Marker.cs` (master's)
- Modify: `AIOrchestratorCoreLib/Telegram/TelegramDeliveryModes.cs` (`TopicNameFlags` gains `IsPausedByOwner`; `Compose_TopicName`, `Strip_Glyph`)
- Modify: `AIOrchestratorCoreLib/Bridge/BridgeEngine/BridgeEngineModel.cs` (`/pause` command, `Wake_PausedTopic_IfNeeded`, the paused gates on every waker, the `.paused` reconcile each tick, `EffectiveMode_Resolver` deferral)
- Modify: `AIOrchestratorCoreLib/Bridge/EffectiveMode_Resolver.cs` (paused → Deferred ahead of presence)
- Modify: `kit/hooks/run-to-the-end-check.sh`, `kit/hooks/supervisor-ledger-check.sh` (the `.paused` exit)
- Modify: `AIOrchestratorCoreLib/Telegram/BotCommandMenu.cs` (`pause`)
- Un-park: `PausedFlagMarkerTests` → `Status/`; the `Paused` cases of `SessionJsonSerializerTests` now run; master's `RunToTheEndHookTests` additions (the fork's file exists — add master's `.paused` case from `git show master:AIOrchestratorCoreLib.Tests/Kit/RunToTheEndHookTests.cs`)

**Interfaces:**
- Consumes: the fork's `TopicNameFlags(OwnerReply, IsPausedForUsageLimit, IsClosed, IsAwaitingTest, IsDone)` and `Compose_TopicName(string baseName, TopicNameFlags flags)`; `EffectiveMode_Resolver.Resolve(...)`; the fork's waker methods named in CLAUDE.md's PAUSE decision (`Append_SupervisorAttention_UnlessMeeting`, `Nudge_IdleImplementers_Async`, `Flag_IdleMembers`, `Check_LedgerHealth_Async`, `Push_AwayDigests_Async` (the fork's name for the periodic pass), `Resume_AllSessions_Async`, `SessionWatchdog` respawn, `Break_SilentDeadlock_Async` — grep each on the fork; the fork has no `Break_SilentDeadlock_Async`, note it in the report).
- Produces: `IOrchestrationSession.Paused : bool`; `IOrchestrationSessionStore.Set_Paused(string orchId, bool paused)`; session.json `paused`; `TopicNameFlags.IsPausedByOwner`; `PausedFlag_Marker.Reconcile(...)` called every tick; `/pause` in the menu and the dispatch chain; `EffectiveMode_Resolver` answering `Deferred` when paused.

- [ ] **Step 1: Un-park the pure tests and add the record test**

```bash
git mv AIOrchestratorCoreLib.Tests/_pending-reports/PausedFlagMarkerTests.cs AIOrchestratorCoreLib.Tests/Status/PausedFlagMarkerTests.cs
```
Add to `AIOrchestratorCoreLib.Tests/Telegram/TelegramDeliveryModeGlyphsTests.cs`:

```csharp
[Fact]
public void ATopicPausedByTheOwner_WearsTheSleepGlyph_AboveDoneAndTest_BelowClosed()
{
    // 💤 is the OWNER's pause (they walked away); ⏸ is a usage-limit pause the app detected. Two
    // facts, two fields — they used to compete for one slot (spec 2026-09-11 §7.4).
    Assert.StartsWith("💤", TelegramDeliveryModes.Compose_TopicName("SL · picker", new TopicNameFlags(OwnerReplyStates.None, isPausedByOwner: true, isPausedForUsageLimit: true, isClosed: false, isAwaitingTest: true, isDone: true)));
    Assert.StartsWith("🏁", TelegramDeliveryModes.Compose_TopicName("SL · picker", new TopicNameFlags(OwnerReplyStates.None, isPausedByOwner: true, isPausedForUsageLimit: false, isClosed: true, isAwaitingTest: false, isDone: false)));
    Assert.Equal("SL · picker", TelegramDeliveryModes.Strip_Glyph("💤 SL · picker"));
}
```
(Match the record's constructor style to the fork's — it is a positional record-like class per the fork's conventions; if it is a `record`, keep it so, the fork's rules note that drift.)

- [ ] **Step 2: Run to verify it fails**

Run: `dotnet test AIOrchestratorCoreLib.Tests --filter "FullyQualifiedName~ATopicPausedByTheOwner"`
Expected: compile error — no `isPausedByOwner`.

- [ ] **Step 3: Add the field, the precedence and the strip**

`TelegramDeliveryModes.cs`: add `bool IsPausedByOwner` to `TopicNameFlags` between `OwnerReply` and `IsPausedForUsageLimit`; add `public const string PAUSED_BY_OWNER = "\U0001F4A4";` (master's `PAUSED`, with master's XML doc); in `Compose_TopicName` draw exactly one state glyph in the order 🏁 → 💤 → ✅ → 🧪 → ⏸; add 💤 to the glyph list `Strip_Glyph` walks. Update the fork's `Build_WantedTopicName` call site in the engine to pass `session.Paused`.

- [ ] **Step 4: Run to verify it passes**

Run: `dotnet test AIOrchestratorCoreLib.Tests --filter "FullyQualifiedName~TelegramDeliveryModeGlyphsTests"`
Expected: PASS (all the fork's existing glyph cases too).

- [ ] **Step 5: Session state**

As in Task 4 Step 2, take master's `Paused` property, constructor parameter, `CreateFrom_Existing_WithPaused`, the `paused` key in the serializer, and `Set_Paused` on the store, verbatim from `git show master:<path>`.

Run: `dotnet test AIOrchestratorCoreLib.Tests --filter "FullyQualifiedName~SessionJsonSerializerTests|FullyQualifiedName~OrchestrationSessionStoreTests"` → PASS.

- [ ] **Step 6: Write the failing engine test — a paused orchestration is dormant on both halves**

Add `AIOrchestratorCoreLib.Tests/Bridge/APausedOrchestrationIsDormantTests.cs` with the fork's engine harness:

```csharp
[Fact]
public async Task WhilePaused_NothingIsPushedToTheSupervisor_AndOutboundIsDeferred_UntilTheOwnerWrites()
{
    using var h = await EngineHarness.StartAsync();
    await h.OwnerSays("repo-3", "/pause");
    Assert.True(h.Store.Get("repo-3").Paused);
    Assert.True(File.Exists(Path.Combine(h.Paths.Get_OrchestrationFolder("repo-3"), ".paused")));
    h.SupervisorWrites("repo-3", "## [9] FROM supervisor — 2026-09-11 15:00 — status\n\nstill here\n");
    await h.Tick(minutes: 10);                               // idle nudges, ledger checks, periodic pass would all fire
    Assert.Empty(h.AppEntriesWrittenTo("repo-3", since: "[9]"));   // nothing woke the supervisor
    Assert.Empty(h.Telegram.SentSince("/pause"));                    // outbound deferred
    await h.OwnerSays("repo-3", "go on");
    Assert.False(h.Store.Get("repo-3").Paused);
    Assert.False(File.Exists(Path.Combine(h.Paths.Get_OrchestrationFolder("repo-3"), ".paused")));
    Assert.NotEmpty(h.Telegram.SentSince("/pause"));                 // the backlog replays
}
```

- [ ] **Step 7: Run to verify it fails**

Run: `dotnet test AIOrchestratorCoreLib.Tests --filter "FullyQualifiedName~APausedOrchestrationIsDormantTests"`
Expected: FAIL — `/pause` unknown.

- [ ] **Step 8: Re-port the engine pause**

From master's engine copy `/pause` handling (the 60-second re-assert rule: "a second /pause inside 60 s re-asserts rather than toggling"), `Wake_PausedTopic_IfNeeded(IOrchestrationSession)` (called where inbound owner messages are routed), the per-tick `PausedFlag_Marker.Reconcile` beside the fork's `.meeting` reconcile, and the `if (session.Paused) continue;` gates in each waker the CLAUDE.md PAUSE decision lists. `EffectiveMode_Resolver.Resolve`: return `Deferred` when `session.Paused` **before** the presence check (master's ordering; copy its test `EffectiveModeResolverTests` cases for paused into the fork's file). Add `("pause", "pause this orchestration — asleep, not closed")` to `BotCommandMenu.ALL`.

- [ ] **Step 9: The hooks**

`kit/hooks/run-to-the-end-check.sh` and `supervisor-ledger-check.sh`: right after `SUPERVISION_ROOT=...` and the orchestration id resolution, add master's exit (copy from `git show master:kit/hooks/run-to-the-end-check.sh`, the block that tests `-f "$orch/.paused"` and exits 0 with the "paused by the owner — sleep" message), using `$SUPERVISION_ROOT` instead of `$HOME/.claude/supervision`. Add master's `.paused` case to `RunToTheEndHookTests` (and the same shape for the ledger hook in `SupervisorLedgerHookTests` if the fork has one; otherwise in `RunToTheEndHookTests` with a note).

- [ ] **Step 10: Run the touched classes**

Run: `dotnet test AIOrchestratorCoreLib.Tests --filter "FullyQualifiedName~APausedOrchestration|FullyQualifiedName~PausedFlagMarker|FullyQualifiedName~EffectiveModeResolver|FullyQualifiedName~RunToTheEndHook|FullyQualifiedName~BotCommandMenu"`
Expected: PASS.

- [ ] **Step 11: Commit and tick the ledger**

```bash
git add AIOrchestratorCoreLib/Sessions AIOrchestratorCoreLib/Telegram/TelegramDeliveryModes.cs AIOrchestratorCoreLib/Telegram/BotCommandMenu.cs AIOrchestratorCoreLib/Bridge AIOrchestratorCoreLib/Status/PausedFlag_Marker.cs kit/hooks AIOrchestratorCoreLib.Tests/Status AIOrchestratorCoreLib.Tests/Telegram/TelegramDeliveryModeGlyphsTests.cs AIOrchestratorCoreLib.Tests/Bridge/APausedOrchestrationIsDormantTests.cs AIOrchestratorCoreLib.Tests/Bridge/EffectiveModeResolverTests.cs AIOrchestratorCoreLib.Tests/Kit docs/superpowers/plans/2026-09-11-fork-merge-01-report.md
git commit -F <tempfile>   # "feat(bridge): owner /pause on the merged engine — 💤 beside ⏸, dormant on both halves, .paused honoured by the hooks"
```

---

### Task 6: Re-port the question hold (the only way — owner ruling)

**Files:**
- Keep: `AIOrchestratorCoreLib/Bridge/QuestionHold_Policy.cs` (master's)
- Modify: `AIOrchestratorCoreLib/Bridge/BridgeEngine/BridgeEngineModel.cs` — `Mirror_Append_Async` return type, the tick loop, `Find_ActiveChannels` comment, `Would_BeASecondOpenQuestion` coaching
- Modify: `kit/hooks/supervisor-awaiting-answer-check.sh` (role gate)
- Un-park: `QuestionHoldPolicyTests` → `Bridge/`; `QuestionWaterfallProbeTests` → `Bridge/` (adapt to the fork's harness)

**Interfaces:**
- Consumes: `QuestionHold_Policy.Should_Hold(bool isOwnerChannel, bool questionOutstanding)`; the fork's `Settle_MirrorAttempt_Async(ICompletedChannelAppend append, bool delivered, CancellationToken ct)` and `_tailer.Confirm_Append`; `AwaitingAnswerFlag_Marker` (byte-identical both sides); the fork's `Expire_StaleAwaitingAnswerFlags` (the 10-minute cap) and `Apply_Presence_ToAwaitingAnswerFlag`.
- Produces: `enum MirrorOutcomes { Delivered, Failed, Held }` (nested in the engine as on master); `Task<MirrorOutcomes> Mirror_Append_Async(...)`; `_deliveredEntriesOfHeldAppend : Dictionary<string,int>`; the loop's `heldChannels` set; `const int QUESTION_HOLD_CAP_MINUTES = 10` (the fork has the expiry; align the constant name).

- [ ] **Step 1: Un-park the policy test and run it**

```bash
git mv AIOrchestratorCoreLib.Tests/_pending-reports/QuestionHoldPolicyTests.cs AIOrchestratorCoreLib.Tests/Bridge/QuestionHoldPolicyTests.cs
dotnet test AIOrchestratorCoreLib.Tests --filter "FullyQualifiedName~QuestionHoldPolicyTests"
```
Expected: PASS (pure predicate).

- [ ] **Step 2: Un-park the waterfall probe, adapt, run to verify it fails**

`git mv AIOrchestratorCoreLib.Tests/_pending-reports/QuestionWaterfallProbeTests.cs AIOrchestratorCoreLib.Tests/Bridge/QuestionWaterfallProbeTests.cs`. Adapt its harness calls to the fork's engine harness (the fork's `ThePhoneRingsOnlyForTheSupervisorTests` shows the shape). The probe's contract, unchanged: the supervisor writes two typed questions in one append; the phone receives ONE; the owner answers; the second is then sent; the cursor never advanced past the held entry (a restart between the two re-emits the second, not the first).

Run: `dotnet test AIOrchestratorCoreLib.Tests --filter "FullyQualifiedName~QuestionWaterfallProbeTests"`
Expected: FAIL — both questions reach the phone (the fork lets them out).

- [ ] **Step 3: Restore the tri-state outcome and the hold**

In the fork's engine:
1. Add master's `enum MirrorOutcomes { Delivered, Failed, Held }` and the field `readonly Dictionary<string, int> _deliveredEntriesOfHeldAppend = [];` with master's XML doc (copy from `git show master:...BridgeEngineModel.cs`, the block before `Mirror_Append_Async`).
2. `Mirror_Append_Async` returns `Task<MirrorOutcomes>`: every `return true` → `Delivered`, `return false` → `Failed`. Before the marker extraction of each entry (the fork's `Extract_MarkerLines` calls), insert master's block: skip entries `< alreadyDelivered` of a previously held append; `if (QuestionHold_Policy.Should_Hold(append.Channel.IsOwnerChannel, Is_AwaitingAnswer(orchId))) { _deliveredEntriesOfHeldAppend[append.Channel.FilePath] = deliveredHere; return MirrorOutcomes.Held; }`; remove the dictionary entry on `Delivered`. Copy `Is_AwaitingAnswer(string orchId)` from master (reads `AwaitingAnswerFlag_Marker`).
3. The tick loop: `var outcome = await Mirror_Append_Async(append, ct);` → `if (outcome == MirrorOutcomes.Held) { heldChannels.Add(append.Channel.FilePath); continue; }` (no settle — the cursor stays); `await Settle_MirrorAttempt_Async(append, outcome == MirrorOutcomes.Delivered, ct);`. Skip any append whose channel is already in `heldChannels` this tick (master's guard: confirming a later append would confirm the held question with it).
4. `Find_ActiveChannels`: rewrite the comment that says a pending question does NOT freeze the channel: "A pending owner question HOLDS the owner channel at the mirror (QuestionHold_Policy, owner ruling 2026-09-11: one question at a time is the only way). The hold is not an offset freeze — the file is still polled, the held append is simply not settled — so the fork's 'the supervisor is STOPPED instead' remains true as well: both halves apply."
5. Delete `Would_BeASecondOpenQuestion` and its coaching entry (its text is false under the hold); keep `Find_QuestionsToSupersede` and the SUPERSEDED path untouched (spec §7.2 explains why they remain reachable and correct).

- [ ] **Step 4: Run the probe and the fork's question tests**

Run: `dotnet test AIOrchestratorCoreLib.Tests --filter "FullyQualifiedName~QuestionWaterfallProbeTests|FullyQualifiedName~AnswerBindingDeciderTests|FullyQualifiedName~QuestionClosureWordingTests|FullyQualifiedName~DecisionStateSurvivesARestartTests|FullyQualifiedName~TypedAnswerClearsTheQuestionProbeTests|FullyQualifiedName~TheBridgeNeverLiesAboutDeliveryTests"`
Expected: all PASS. `DecisionStateSurvivesARestartTests` opens two questions deliberately — read how (it must go through a route the hold does not cover: terminal presence or an expired flag); if it now fails because both were Remote and the second is held, change the test to open the second under terminal presence and record why in its comment.

- [ ] **Step 5: The hook covers solo**

`kit/hooks/supervisor-awaiting-answer-check.sh`: replace `if [ "${AIORCH_ROLE:-}" != "supervisor" ]` with master's

```bash
case "${AIORCH_ROLE:-}" in
  supervisor|solo) ;;   # both talk to the owner; solo was missing until 2026-09-09 and put nine questions on the phone in five minutes
  *) exit 0 ;;
esac
```
keeping the fork's `SUPERVISION_ROOT` lines. Add master's solo case to the hook's behaviour harness (`kit/hooks/hook-behaviour-check.sh` cases or the `Kit` tests — find where the fork tests this hook: `grep -rl "supervisor-awaiting-answer" AIOrchestratorCoreLib.Tests kit`).

- [ ] **Step 6: Commit and tick the ledger**

```bash
git add AIOrchestratorCoreLib/Bridge kit/hooks/supervisor-awaiting-answer-check.sh AIOrchestratorCoreLib.Tests/Bridge AIOrchestratorCoreLib.Tests/Kit kit/hook-behaviour-check.sh docs/superpowers/plans/2026-09-11-fork-merge-01-report.md
git commit -F <tempfile>   # "feat(bridge): one question at a time is the only way — the hold returns to the merged mirror loop, and the hook covers solo"
```

---

### Task 7: Re-port the owner-answer credit's protective half

**Files:**
- Modify: `AIOrchestratorCoreLib/Bridge/OwnerPush_Policy.cs` (`Is_TurnEndDeclaration`)
- Modify: `AIOrchestratorCoreLib/Bridge/BridgeEngine/BridgeEngineModel.cs` (`Raise_OwnerWait`, the two inlined adds, the credit consumption, `/merge` opening `Track_OwnerReply`)
- Un-park: `AStatusLineDoesNotSpendTheOwnersWaitTests`, `MergeCommandOpensTheReplyTrackerTests`, `BusyNoticeRespectsAnAnswerScanTests` → `Bridge/` (adapt to the fork's harness)

**Interfaces:**
- Consumes: the fork's `_ownerAwaitingAnswer` (persisted in the engine snapshot), the two sites that add to it (owner message routed; task filed at orchestration start), the consumption after a successful send in `Mirror_Append_Async`; the fork's `Track_OwnerReply` equivalent (`PendingOwnerReply`).
- Produces: `OwnerPush_Policy.Is_TurnEndDeclaration(string? subject) : bool` (master's, subject only); `void Raise_OwnerWait(string orchId)` as the ONE place the credit is raised; the credit NOT consumed by an entry whose subject is a turn-end declaration; `/merge` raising the credit and opening the reply tracker. NOT in this task: `_suppressedEntries` and the turn-end digest — they are the `phone.push = filtered` half and belong to plan 03 (who rings). Say so in the ledger row.

- [ ] **Step 1: Un-park and adapt the three tests; run to verify they fail**

```bash
for t in AStatusLineDoesNotSpendTheOwnersWaitTests MergeCommandOpensTheReplyTrackerTests BusyNoticeRespectsAnAnswerScanTests; do git mv AIOrchestratorCoreLib.Tests/_pending-reports/$t.cs AIOrchestratorCoreLib.Tests/Bridge/$t.cs; done
dotnet test AIOrchestratorCoreLib.Tests --filter "FullyQualifiedName~AStatusLineDoesNotSpendTheOwnersWait|FullyQualifiedName~MergeCommandOpensTheReplyTracker|FullyQualifiedName~BusyNoticeRespectsAnAnswerScan"
```
Expected: `AStatusLineDoesNotSpendTheOwnersWait` FAILS (a `WAITING ON …` subject consumes the fork's credit); `MergeCommandOpensTheReplyTracker` FAILS (`/merge` does not raise); `BusyNoticeRespectsAnAnswerScan` — read it: if it depends on the suppression digest, move it back to `_pending-reports` with a note "plan 03"; if it only needs the credit, keep it.

- [ ] **Step 2: `Is_TurnEndDeclaration`**

Copy master's `Is_TurnEndDeclaration(string? subject)` and its XML doc into the fork's `OwnerPush_Policy` (the fork's `WAITING_ON_MARKER` may not exist — add `public const string WAITING_ON_MARKER = "WAITING ON";` beside `IMAGE_MARKER`, which on the fork is `ChannelGrammar.IMAGE`; keep that). In `Mirror_Append_Async`, at the consumption site (the `_ownerAwaitingAnswer.Remove(orchId)` after a successful send), guard it: `if (!OwnerPush_Policy.Is_TurnEndDeclaration(entry.Subject)) { remove; Persist_EngineState(); }`.

- [ ] **Step 3: `Raise_OwnerWait`**

Add master's `void Raise_OwnerWait(string orchId)` (without the `_suppressedEntries.Remove` line — that list does not exist yet; leave a one-line comment "plan 03 adds the suppression clear here") and route the two inlined adds through it. In the `/merge` handler, call `Raise_OwnerWait(session.OrchId)` and open the same reply tracker an owner message does (master's `Track_OwnerReply(orchId, threadId, receiptMessageId, ownerAnswerCountAtDelivery)`; the fork's equivalent takes the same facts — read `PendingOwnerReply`'s constructor).

- [ ] **Step 4: Run to verify they pass**

Run: `dotnet test AIOrchestratorCoreLib.Tests --filter "FullyQualifiedName~AStatusLineDoesNotSpendTheOwnersWait|FullyQualifiedName~MergeCommandOpensTheReplyTracker|FullyQualifiedName~OwnerAnswerSurvivesFailedSendTests|FullyQualifiedName~ThePhoneRingsOnlyForTheSupervisorTests"`
Expected: PASS (the last two are the fork's; they must stay green).

- [ ] **Step 5: Commit and tick the ledger**

```bash
git add AIOrchestratorCoreLib/Bridge AIOrchestratorCoreLib.Tests/Bridge docs/superpowers/plans/2026-09-11-fork-merge-01-report.md
git commit -F <tempfile>   # "fix(bridge): the owner's answer credit is raised in one place, never spent on a WAITING ON line, and /merge opens the tracker"
```

---

### Task 8: Re-port the model and effort reading types, and effort on both statuslines

**Files:**
- Keep: `AIOrchestratorCoreLib/Status/SessionModelReading/*`, `Formatting/ModelReading_Formatter.cs` (master's)
- Modify: `AIOrchestratorCoreLib/Usage/UsageTotals_Reader.cs` (`Read_ModelReading_OrNull` — the trivial both-sides file; verify it merged)
- Modify: `kit/statusline/statusline.sh` (effort beside the model)
- Un-park: `SessionModelReadingFactoryTests` → `Status/`; `ModelReadingFormatterTests` → `Formatting/`; `ModelOnTheStatusLineTests` STAYS parked (its subject is the PULSE field — plan 03, `pulse.fields`), record in the ledger.
- Test: `AIOrchestratorCoreLib.Tests/Kit/StatusLineScriptParityTests.cs` + a new fixture `kit/statusline/fixtures/supervisor-with-effort.json`

**Interfaces:**
- Consumes: `.usage.json` fields `model.display_name`, `effort.level`.
- Produces: `ISessionModelReading { DisplayName, EffortLevel }`, `SessionModelReading_Factory.Create_FromStatuslineJson_OrNull(string rawJson)`, `ModelReading_Formatter.Describe_OrNull(ISessionModelReading?)` → `"Fable 5.1 · xhigh"`; both statusline scripts render `<model> · <effort>` when `effort.level` is present.

- [ ] **Step 1: Un-park the two pure test classes and run**

```bash
git mv AIOrchestratorCoreLib.Tests/_pending-reports/SessionModelReadingFactoryTests.cs AIOrchestratorCoreLib.Tests/Status/SessionModelReadingFactoryTests.cs
git mv AIOrchestratorCoreLib.Tests/_pending-reports/ModelReadingFormatterTests.cs AIOrchestratorCoreLib.Tests/Formatting/ModelReadingFormatterTests.cs
dotnet test AIOrchestratorCoreLib.Tests --filter "FullyQualifiedName~SessionModelReadingFactoryTests|FullyQualifiedName~ModelReadingFormatterTests"
```
Expected: PASS.

- [ ] **Step 2: Write the failing parity fixture**

Create `kit/statusline/fixtures/supervisor-with-effort.json` by copying the existing supervisor fixture (list: `ls kit/statusline/fixtures/`) and adding `"effort": {"level": "xhigh"}` to `stdin`; set `expected` to the same line with ` · xhigh` after the model name (the `.ps1` already renders it: copy the exact spacing from `git show master:kit/statusline/statusline.ps1`, the line "The effort level rides NEXT TO THE MODEL").

Run: `dotnet test AIOrchestratorCoreLib.Tests --filter "FullyQualifiedName~StatusLineScriptParityTests"`
Expected: the new fixture FAILS for the bash script (no effort) and, on Windows, PASSES for the PowerShell reference only if the encoding fix of Task 12 has landed — until then the `·` fixtures fail on Windows for the known reason; compare NAMES against the baseline.

- [ ] **Step 3: Render effort in `statusline.sh`**

After `model=$(json_get '.model.display_name')` add `effort=$(json_get '.effort.level')` and, where `${model}` is printed, use `${model}${effort:+ · $effort}` in every role line (there are several `printf` lines — one per role; change each).

- [ ] **Step 4: Run parity**

Run: `dotnet test AIOrchestratorCoreLib.Tests --filter "FullyQualifiedName~StatusLineScriptParityTests"`
Expected: the bash side of every fixture passes (with `jq` installed, Task 1); the PowerShell `·` cases are Task 12's.

- [ ] **Step 5: Commit and tick the ledger**

```bash
git add kit/statusline AIOrchestratorCoreLib.Tests/Status AIOrchestratorCoreLib.Tests/Formatting docs/superpowers/plans/2026-09-11-fork-merge-01-report.md
git commit -F <tempfile>   # "feat(statusline): effort beside the model on both scripts; the reading types are back"
```

---

### Task 9: Reply keyboard — compiles, tested, not wired

**Files:**
- Keep: `AIOrchestratorCoreLib/Telegram/ReplyKeyboard_Markup.cs` (master's)
- Un-park: `ReplyKeyboardMarkupTests` → `Telegram/`

**Interfaces:**
- Produces: `ReplyKeyboard_Markup.Build(IReadOnlyList<IReadOnlyList<string>> rows)` compiling and pinned by its tests; NOT called anywhere (spec §7.6: `phone.replyKeyboard` default off; wiring is plan 03's, and it must not delete the carrier).

- [ ] **Step 1: Un-park and run**

```bash
git mv AIOrchestratorCoreLib.Tests/_pending-reports/ReplyKeyboardMarkupTests.cs AIOrchestratorCoreLib.Tests/Telegram/ReplyKeyboardMarkupTests.cs
dotnet test AIOrchestratorCoreLib.Tests --filter "FullyQualifiedName~ReplyKeyboardMarkupTests"
```
Expected: PASS. If the class references `TopicCommandButtons.Build_ReplyKeyboardRows` (deleted by the fork), add that one static back to `TopicCommandButtons` verbatim from master, with a doc line "unused until plan 03 wires `phone.replyKeyboard`; the carrier must never be deleted (measured 2026-09-06)".

- [ ] **Step 2: Add the guard test that nothing installs it yet**

In `ReplyKeyboardMarkupTests.cs`:
```csharp
[Fact]
public void NothingInstallsTheKeyboardYet_BecauseTheCarrierDeleteRemovesTheBar()
{
    // 75abab7 on the fork measured live that deleting the carrier deletes the bar. Until plan 03
    // wires phone.replyKeyboard with the carrier kept, no production code may call the installer.
    var engine = File.ReadAllText(Path.Combine(RepoRoot.Find(), "AIOrchestratorCoreLib", "Bridge", "BridgeEngine", "BridgeEngineModel.cs"));
    Assert.DoesNotContain("Install_CommandKeyboard", engine);
}
```
(`RepoRoot.Find()` — use whatever helper the fork's `RoleHooksAreShippedTests` uses to locate the checkout.)

- [ ] **Step 3: Commit and tick the ledger**

```bash
git add AIOrchestratorCoreLib/Telegram AIOrchestratorCoreLib.Tests/Telegram/ReplyKeyboardMarkupTests.cs docs/superpowers/plans/2026-09-11-fork-merge-01-report.md
git commit -F <tempfile>   # "chore(telegram): reply keyboard markup kept and pinned, deliberately unwired"
```

---

### Task 10: Re-port master's kit prose into the fork's skills

**Files:**
- Modify: `kit/skills/solo/SKILL.md`, `kit/skills/supervisor/SKILL.md`, `kit/skills/general-supervisor/SKILL.md`
- Test: the fork's normative-sentence test (`grep -rl "normative" AIOrchestratorCoreLib.Tests/Kit`) and `RoleHooksAreShippedTests`

**Interfaces:**
- Consumes: master's paragraphs in `git show master:kit/commands/{solo,supervisor,general-supervisor}.md`.
- Produces: the four blocks present in the fork's skills: (1) solo — `ONE OPEN QUESTION AT A TIME` (done in Task 2 Step 3 if the conflict was resolved that way; verify); (2) general-supervisor — its `ONE OPEN QUESTION AT A TIME` section; (3) supervisor — the "the APP HOLDS THE CHANNEL as well" extension of its hard rule; (4) supervisor and solo — the "You may also have been RESUMED … trust the CHANNEL over your memory" paragraph (master's `5e7ed6b`).

- [ ] **Step 1: Write the failing kit test**

Add to the fork's kit tests (the class that reads `kit/skills/*/SKILL.md`; create `KitProseCarriesTheOwnersRulesTests.cs` in `AIOrchestratorCoreLib.Tests/Kit/` if none reads prose):

```csharp
[Theory]
[InlineData("solo", "ONE OPEN QUESTION AT A TIME")]
[InlineData("general-supervisor", "ONE OPEN QUESTION AT A TIME")]
[InlineData("supervisor", "the app HOLDS this channel")]
[InlineData("supervisor", "You may also have been RESUMED")]
[InlineData("solo", "You may also have been RESUMED")]
public void TheSkill_CarriesTheOwnersRule(string role, string sentence)
{
    var skill = File.ReadAllText(Path.Combine(RepoRoot.Find(), "kit", "skills", role, "SKILL.md"));
    Assert.Contains(sentence, skill, StringComparison.OrdinalIgnoreCase);
}
```

- [ ] **Step 2: Run to verify which fail**

Run: `dotnet test AIOrchestratorCoreLib.Tests --filter "FullyQualifiedName~KitProseCarriesTheOwnersRulesTests"`
Expected: the RESUMED and "holds this channel" cases fail; the solo ONE-QUESTION case depends on Task 2 Step 3.

- [ ] **Step 3: Insert the paragraphs**

For each failing case, copy master's paragraph from `git show master:kit/commands/<role>.md` and insert it at the position the fork's skill has the surrounding section (the fork "reorganises, never rewrites a rule"; find the neighbouring heading by its title). Where master's paragraph says "QUESTION: + OPTION: lines", rewrite that clause to "a typed question through `channel-append.sh --question --option …`" so it matches the fork's protocol; leave every other word.

- [ ] **Step 4: Run the kit tests**

Run: `dotnet test AIOrchestratorCoreLib.Tests --filter "FullyQualifiedName~Kit"`
Expected: PASS, including the fork's normative-sentence counter (it counts before/after — if it complains that a sentence count changed, that is expected: record the new count in its expectation with a note naming this task).

- [ ] **Step 5: Commit and tick the ledger**

```bash
git add kit/skills AIOrchestratorCoreLib.Tests/Kit docs/superpowers/plans/2026-09-11-fork-merge-01-report.md
git commit -F <tempfile>   # "docs(kit): master's ONE QUESTION, held-channel and RESUMED paragraphs live in the merged skills"
```

---

### Task 11: Pictures — re-home master's tests onto the fork's attachment policy

**Files:**
- Un-park: `PicturesReachTheOwnerTests` → `Bridge/` (adapt) or retire with reason
- Read: `AIOrchestratorCoreLib/Bridge/EntryAttachment_Policy.cs`, `Telegram/TelegramFileCaps.cs`, `Mirroring/ImageDimensions_Reader.cs` (fork)

**Interfaces:**
- Consumes: the fork's `EntryAttachment_Policy` (IMAGE: and ATTACH: through one gate; refusal written to the agent's channel with the remedy).
- Produces: master's contract kept as tests: an `IMAGE:` line in an owner-channel entry reaches the phone as a photo, immediately, even when the entry is not an answer; a photo the fork's caps refuse is reported to the agent, never silently dropped.

- [ ] **Step 1: Un-park, read both, decide per test**

`git mv AIOrchestratorCoreLib.Tests/_pending-reports/PicturesReachTheOwnerTests.cs AIOrchestratorCoreLib.Tests/Bridge/PicturesReachTheOwnerTests.cs`. For each `[Fact]`: if the fork's `EntryAttachmentPolicyTests` already pins the same behaviour, delete master's duplicate and cite the fork's test name in the commit body; otherwise adapt it to the fork's harness.

- [ ] **Step 2: Run**

Run: `dotnet test AIOrchestratorCoreLib.Tests --filter "FullyQualifiedName~PicturesReachTheOwner|FullyQualifiedName~EntryAttachmentPolicy"`
Expected: PASS.

- [ ] **Step 3: Commit and tick the ledger**

```bash
git add AIOrchestratorCoreLib.Tests/Bridge docs/superpowers/plans/2026-09-11-fork-merge-01-report.md
git commit -F <tempfile>   # "test(bridge): master's picture contract pinned against the fork's attachment gate"
```

---

### Task 12: The Windows reds — encoding, the watcher, the concurrent writer

**Files:**
- Modify: `kit/statusline/statusline.ps1` (output encoding)
- Modify: `AIOrchestratorCoreLib/Bridge/ChannelChangeWaker/ChannelChangeWakerModel.cs`
- Modify: the reader of `print-session.json` (`AIOrchestratorCoreLib/Running/PrintSessionState/*` — find `File.ReadAllText` on that file name)
- Test: `Kit/StatusLineScriptParityTests`, `Bridge/ChannelChangeWakerTests`, the five print-runner tests named in spec §4.4

**Interfaces:**
- Produces: `statusline.ps1` writes UTF-8 to stdout; `ChannelChangeWakerModel` reports a deleted-and-recreated root ONCE on every OS; `PrintSessionState` reads tolerate a writer holding the file (Windows sharing semantics).

- [ ] **Step 1: Encoding — run the failing parity cases on Windows**

Run: `dotnet test AIOrchestratorCoreLib.Tests --filter "FullyQualifiedName~ThePowerShellReference_RendersTheSameLine"`
Expected: 7 FAIL, all fixtures whose line contains `·` (expected `PB · equity`, actual `PB � equity`).

- [ ] **Step 2: Fix it in the script**

At the top of `kit/statusline/statusline.ps1`, before any output:
```powershell
# stdout leaves powershell.exe in the console's OEM code page unless told otherwise; the host reads
# it as UTF-8 and '·' arrives as '�' (measured 2026-09-11, the first Windows run of the suite).
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8
$OutputEncoding = [System.Text.Encoding]::UTF8
```
Run the filter again → 0 FAIL.

- [ ] **Step 3: The watcher — make the test state the Windows fact**

Run: `dotnet test AIOrchestratorCoreLib.Tests --filter "FullyQualifiedName~ARootDeletedAndRecreated_IsNoticedOnceAndTheWatchComesBack"`
Expected: FAIL on Windows (`Assert.Single` sees two lines: the Error event and the re-arm).

In `ChannelChangeWakerModel`, in the `FileSystemWatcher.Error` handler: if the watched root no longer exists, treat the event as the "root gone" fact the deletion path already handles (do not log a second line); log the error only when the root still exists. Then the test passes unchanged on both OSes. Add one line to its comment: "Windows raises Error when the root is deleted; inotify does not — one fact, one line, on both."

- [ ] **Step 4: The concurrent writer — reproduce, then tolerate**

Run the five tests of spec §4.4 in a loop: `for i in 1 2 3 4 5; do dotnet test AIOrchestratorCoreLib.Tests --no-build --filter "FullyQualifiedName~StreamTurnDispatcherTests|FullyQualifiedName~PrintTurnLimitResetTests|FullyQualifiedName~ClosingTurnReviewFixTests|FullyQualifiedName~WakeUpDigestReviewFixTests" 2>&1 | grep -E "Failed |Passed!|Failed!"; done`
Expected: at least one `IOException: The process cannot access the file '…print-session.json'` across the five runs.

Find the reader: `grep -rn "print-session.json" AIOrchestratorCoreLib/Running | grep -v "//"`. Where it reads, open with sharing and retry:
```csharp
// Windows honours FileShare: a reader that opens while the atomic writer holds the file gets
// IOException, where Linux simply reads the old bytes. Retry a few times before giving up — the
// writer holds it for a rename, not a computation (measured 2026-09-11: ~1 red per run, wandering).
static string Read_AllText_TolerantOfAWriter(string path)
{
    IOException? last = null;
    for (var attempt = 0; attempt < 5; attempt++)
    {
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var reader = new StreamReader(stream);
            return reader.ReadToEnd();
        }
        catch (IOException e) { last = e; Thread.Sleep(20 * (attempt + 1)); }
    }
    throw last!;
}
```
Put it in `Storage/` beside `Atomic_FileWriter` as `Tolerant_FileReader.Read_AllText(path)` (a triple is not needed for a static utility; follow `Atomic_FileWriter`'s shape) and use it for `print-session.json` and for the `.usage.json` probe reads (`UsageTotals_Reader.Read_Text_Safe` — check whether it already swallows; if it returns empty on IOException, that is a silent wrong answer on Windows — route it through the tolerant reader and keep its swallow as the last resort).

Rerun the loop → 0 IOExceptions in five runs.

- [ ] **Step 5: The three persistent reds — diagnose, bounded**

For `MultiSourceSupervisorTests.AfterABridgeRestart_NoEntryIsDeliveredTwice_AndNoneIsLost` and `AnEntryWrittenAfterAnAppEntryStillReachesThePhoneTests.AnEntryAppendedAfterTheAppsOwnEntry_IsStillMirrored`: run each alone three times. If green alone, they are load reds — add them to the fork's serial collection for real-time probes (create `[Collection("real-time")]` with `DisableParallelization = true` if the fork has none; the fork's G-light plan names this). If red alone, read the assertion and the code path for at most two hours each; either fix (with a test that pins the fix) or mark `[Fact(Skip = "<the exact observed failure and OS>")]` with the observation in the skip text — never a bare skip.

- [ ] **Step 6: Run the whole CoreLib suite once on Windows and diff the red set against the baseline**

Run: `dotnet test AIOrchestratorCoreLib.Tests --no-build -c Debug 2>&1 | tee ../after-task12.log | tail -5; grep -E "^\s+Failed " ../after-task12.log | sort > ../after-task12-red.txt; comm -13 ../merge-baseline-red.txt ../after-task12-red.txt`
Expected: `comm` prints nothing (no NEW red); the remaining red set is ≤ 3 named tests, each with a skip reason or an open ticket in the ledger.

- [ ] **Step 7: Commit**

```bash
git add kit/statusline/statusline.ps1 AIOrchestratorCoreLib/Bridge/ChannelChangeWaker AIOrchestratorCoreLib/Storage AIOrchestratorCoreLib/Running AIOrchestratorCoreLib/Usage AIOrchestratorCoreLib.Tests docs/superpowers/plans/2026-09-11-fork-merge-01-report.md
git commit -F <tempfile>   # "fix(windows): UTF-8 statusline, one line for a deleted watch root, tolerant reads beside the atomic writer"
```

---

### Task 13: CLAUDE.md and the rules for the merged repo

**Files:**
- Modify: `CLAUDE.md` (decisions 8, 11, 17, 23; the "Repository Structure" block; the Design Spec pointer)
- Keep: `.claude/rules/*.md` (the fork's), `README-daemon.md`, `docs/MODIFICHE-DEL-FORK.md` (its section 7 gets one dated line: "merged into upstream master on <date>, plan 01")

**Interfaces:** none (documentation).

- [ ] **Step 1: Rewrite the four decisions**

- **8 (lifecycle/resume):** keep master's text (resume via `--resume` for terminal supervisor/solo, never `--continue`) and add: "Bridge-driven sessions (`runners.<role>.runner = print|stream`) are driven by the dispatcher: `ResumeModes.Transcript` resumes the print session, `Fresh` starts empty with a state pack. Same key, two runners."
- **11 (translation):** replace with the fork's decision 11 text (the layer is gone; roles write to the owner in the owner's language) **only if the owner confirms §11.1 of the spec**; until then add one line under master's text: "Pending the owner's decision in the 2026-09-11 spec §11.1; the fork deleted the layer."
- **17 (kit delivery):** replace with: "The APP VERIFIES the kit plugin; it never copies commands. `KitAssets_Bootstrapper` runs in both hosts at startup: unwires legacy hooks, moves aside legacy `~/.claude/commands` files, installs only the statusline, and records a `PluginVerdicts` on the `IPluginGate` — spawning is refused on a mismatch, the bridge never is. Delivery = `kit/install.ps1` / `install.sh` (register the checkout as the `aiorch-local` marketplace, install `aiorch@aiorch-local` at user scope, reinstall on content mismatch). A running app built from a stale checkout still verifies against ITS build's commit — decision 23 still applies."
- **23:** unchanged, plus one sentence: "`KitAssets_Bootstrapper` replaces `KitAssets_Installer` in that paragraph."
- Add **26**: "The fork merge of 2026-09-11 (plan 01): the fork's structure won every conflicted file; master's intent was re-ported by hand and ledgered in `docs/superpowers/plans/2026-09-11-fork-merge-01-report.md`. Spec: `docs/superpowers/specs/2026-09-11-fork-merge-and-per-user-profiles-design.md`."

- [ ] **Step 2: Repository structure block**

Replace the stale tree with the merged one: `AIOrchestrator/` (WPF), `AIOrchestrator.Daemon/`, `AIOrchestratorCoreLib/`, `AIOrchestratorCoreLib.Tests/`, `tools/claude-contract/`, `tools/token-gate/`, `tools/ledger-sampler/`, `kit/` (plugin: `.claude-plugin/`, `skills/`, `bin/`, `hooks/`, `grammar/`, `statusline/`, `presets/` (plan 02)), `deploy/`, `docs/`.

- [ ] **Step 3: Commit**

```bash
git add CLAUDE.md docs/MODIFICHE-DEL-FORK.md
git commit -F <tempfile>   # "docs: CLAUDE.md for the merged repo — resume per runner, kit verified not copied, the merge on record"
```

---

### Task 14: The gate — both suites, both hosts, one exchange each way

**Files:**
- Delete: `AIOrchestratorCoreLib.Tests/_pending-reports/` and its `<Compile Remove>` item (must be EMPTY by now except files explicitly deferred to plan 02/03 with a ledger row — move those to `AIOrchestratorCoreLib.Tests/_deferred/` with the same `<Compile Remove>` and a README naming the plan)
- Modify: `docs/superpowers/plans/2026-09-11-fork-merge-01-report.md` (final red sets, both OSes)

- [ ] **Step 1: Every ledger row is ticked or deferred with a plan number**

Open the report; any row neither ticked nor marked `plan 0N` blocks this task.

- [ ] **Step 2: Windows — five green runs of CoreLib, one of claude-contract**

```bash
for i in 1 2 3 4 5; do dotnet test AIOrchestratorCoreLib.Tests --no-build -c Debug 2>&1 | grep -E "Passed!|Failed!"; done
dotnet test tools/claude-contract/ClaudeContract.Tests --no-build -c Debug | tail -3
```
Expected: five `Passed!` lines (skipped allowed: the live smokes, the POSIX-mode-bits case, and any `Skip = "<observed failure>"` from Task 12); `31 passed, 8 skipped` for the contract tests.

- [ ] **Step 3: Linux — push the branch and read CI**

```bash
git push -u origin integration/fork-merge
gh run watch --exit-status   # or open the Actions tab
```
Expected: `wpf` green; `tests` green on both matrix legs. A red on `ubuntu-latest` that is green on Windows is a merge regression of the fork's behaviour — fix it here, it is not deferrable.

- [ ] **Step 4: Both hosts start on Windows, from the branch's build output**

Build: `dotnet build AIOrchestrator.slnx -c Debug` in the worktree. Close the running app first (it holds the DLL; `Get-Process AIOrchestrator | Stop-Process`) and note that the owner's live app runs from `bin\Debug - Copia`, which this does not touch.
1. Start `AIOrchestrator\bin\Debug\net10.0-windows\AIOrchestrator.exe` with `AIORCH_SUPERVISION_ROOT` pointing at a scratch root (`C:\Temp\aiorch-gate`) holding a `config.json` with one repo and no bot token (file-only mode). Expected in its log: `Kit check` line (plugin verdict — `NotInstalled` is acceptable for the gate, spawning refused is not: run `powershell -File kit\install.ps1` first so the verdict is `Ok`), then "Bridge started". Create an orchestration for the repo from the UI; a Windows Terminal window with `/supervisor <id>` opens. Kill it from the UI; the watchdog respawns it with `--resume` visible in the spawned command line only if a probe file exists (first spawn: fresh). Close the app.
2. Start the daemon: `dotnet run --project AIOrchestrator.Daemon -- --root C:\Temp\aiorch-gate-daemon` with a `config.json` whose `runners.supervisor.runner = "print"` and `CLAUDE_CONTRACT_LIVE` unset, pointing the `claude` on PATH at `tools/claude-contract/FakeClaude` (the fork's live tests document the env var that does this — read `ClaudeContract.Tests/Live/*` for the exact name). Write an owner entry into the general channel; expected: a print turn is dispatched (`orchestrator-global.log.jsonl` shows `bridge turn`), the fake CLI answers, the answer lands in the channel. Ctrl-C: "drain complete" in the log.
Record both transcripts (log excerpts) in the report.

- [ ] **Step 5: Remove the parking lot**

```bash
git rm -r AIOrchestratorCoreLib.Tests/_pending-reports   # after moving deferred files to _deferred/
```
Edit the csproj item to `_deferred\**` with the README. Build → 0 errors.

- [ ] **Step 6: Final commit and the handover line**

```bash
git add AIOrchestratorCoreLib.Tests docs/superpowers/plans/2026-09-11-fork-merge-01-report.md
git commit -F <tempfile>   # "chore(merge): plan 01 gate — both suites green on both OSes, both hosts exchanged one message; parking lot removed"
```
Report to the owner: the branch name, the two green run summaries with counts, the deferred list with plan numbers, and the one-line verdict "ready for your merge". Do NOT merge to master.

---

## Self-review (run by the plan's author, 2026-09-11)

**Spec coverage.** §5.1 → Task 2; §5.2 every bullet → Task 2 Steps 5–7 (formatter, csproj, parked tests), Task 5 (two pauses), Task 13 (translator references resolved inside the engine conflict by taking the fork's file — no translator call survives); §5.3 every row → Tasks 3–11; §5.4 → Task 2 (installer gone with the fork's `App.xaml.cs`), Task 6 (coaching dropped), Task 5 (record replaces the 9-arg signature); §4.4 / §9 last bullet → Tasks 1 and 12; §10 phase 0 → Task 1, phase 1 gate → Task 14. Not in this plan, by design: §6–8 (plans 02–04), §7.7–7.8 (plan 05), the `_suppressedEntries` digest (plan 03), the PULSE `modelEffort` field (plan 03), `owner.name` (plan 05). Each is named in the ledger rows that defer it.

**Placeholders.** None of the banned phrases; every code step shows the code or names the exact `git show master:<path>` source and member. Two harness names (`EngineHarness`, `LauncherHarness`) are stand-ins for whatever the fork's existing test harnesses are called — each step that uses one says which existing test file to read for the real name first.

**Type consistency.** `SessionLaunch_Factory.Create(..., effort, resumeSessionId)` (Task 3) is what Task 4 calls; `Apply_Dial(orchId, kind, role, value, reason)` matches master's; `TopicNameFlags(OwnerReply, IsPausedByOwner, IsPausedForUsageLimit, IsClosed, IsAwaitingTest, IsDone)` is used identically in Task 5's test and step 3; `MirrorOutcomes { Delivered, Failed, Held }` in Task 6 matches master's enum; `Tolerant_FileReader.Read_AllText` named once (Task 12).
