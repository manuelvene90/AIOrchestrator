using System.Diagnostics;
using System.Runtime.InteropServices;
using AIOrchestratorCoreLib.Channels;
using AIOrchestratorCoreLib.Channels.ChannelEntry;
using AIOrchestratorCoreLib.Running;
using AIOrchestratorCoreLib.Running.PrintTurnDispatcher;
using AIOrchestratorCoreLib.Sessions;
using Xunit;
using Xunit.Abstractions;

namespace AIOrchestratorCoreLib.Tests.Running;

/// <summary>
/// THE ROUND THIS STAGE EXISTS FOR, against the real <c>claude</c> on <c>--model haiku</c>: the owner
/// asks for one trivial thing, the supervisor briefs imp-1, imp-1 answers in its own spoke — and then,
/// WITH NO FURTHER OWNER MESSAGE, the supervisor takes a turn on that spoke entry and closes the loop
/// back to the owner. Before this stage the last step did not happen: the report sat in the spoke.
///
/// It also measures the thing the design is judged on — how long a member's entry waits before its
/// supervisor is woken by it.
///
/// Skipped unless <c>AIORCH_LIVE=1</c>: it costs tokens and minutes. The skip says so, because a silent
/// one would let a CLI upgrade pass the suite without ever being measured.
/// </summary>
public class MultiSourceLiveSmokeTests(ITestOutputHelper output)
{
    readonly ITestOutputHelper _output = output;

    sealed class LiveFactAttribute : FactAttribute
    {
        public LiveFactAttribute()
        {
            if (Environment.GetEnvironmentVariable(StreamLiveSmokeTests.ENABLE_ENV) != "1")
                Skip = $"Live smoke — set {StreamLiveSmokeTests.ENABLE_ENV}=1 to run a real supervisor woken by a real implementer's spoke, on --model haiku";
        }
    }

    [LiveFact]
    public async Task AnImplementersReport_WakesTheSupervisor_WithTheOwnerSayingNothing()
    {
        using var harness = new PrintRunnerTestHarness("supervisor:stream,implementer", coalesceSeconds: 1, turnTimeoutMinutes: 4);
        var evidence = Path.Combine(Path.GetTempPath(), "aiorchestrator-stage-1c", "live");
        Directory.CreateDirectory(evidence);

        Write_ProbeRepo(harness);

        const string ORCH = "repo-1";
        harness.Register_Supervisor(ORCH);
        var (_, implementerId) = harness.Register_Member(MemberKinds.Implementer, ORCH);

        List<string> logged = [];
        harness.Log.EntryLogged += entry => logged.Add($"{entry.Level} {entry.Message}");

        var dispatcher = PrintTurnDispatcher_Factory.Create(
            harness.Paths, harness.Store, harness.ConfigProvider,
            AIOrchestratorCoreLib.Running.TurnExecutor.TurnExecutor_Factory.Create_All(harness.Paths, AIOrchestratorCoreLib.Running.ClaudeInvocation.ClaudeInvocation_Resolver.Resolve_ForThisOs(), harness.Log, harness.ConfigProvider),
            harness.Log,
            TimeSpan.FromSeconds(5));

        try
        {
            // ONE owner message, and it is the only one in the whole round.
            Assert.True(ChannelAppender.Append_OwnerEntry(
                harness.Paths.Get_OwnerChannelFile(ORCH),
                "Brief imp-1 to write the word PLATANO into a file called fruit.txt. When imp-1 reports back, tell me it is done.",
                DateTime.Now));

            Assert.True(PrintRunnerTestHarness.Drive_Until(dispatcher, () => Supervisor_Turns(harness, ORCH) >= 1, TimeSpan.FromMinutes(4)),
                $"the supervisor never took its first turn.\n{Read_All(harness, ORCH, implementerId)}");

            // The brief reached the spoke as an ENTRY THE BRIDGE WROTE from a `TO: imp-1` block — the
            // probe supervisor has no append script and never touches a channel file.
            Assert.True(PrintRunnerTestHarness.Drive_Until(dispatcher, () => Spoke(harness, ORCH, implementerId).Any(entry => entry.Author == ChannelAuthors.Supervisor), TimeSpan.FromMinutes(2)),
                $"the supervisor never briefed {implementerId}.\n{Read_All(harness, ORCH, implementerId)}");

            // imp-1 runs and files its report. THE MEASURE STARTS THE INSTANT THAT ENTRY EXISTS.
            var ownerEntriesBeforeTheReport = Owner(harness, ORCH).Count(entry => entry.Author == ChannelAuthors.Supervisor);
            var stopwatch = new Stopwatch();

            Assert.True(PrintRunnerTestHarness.Drive_Until(dispatcher, () =>
            {
                if (!Spoke(harness, ORCH, implementerId).Any(entry => entry.Author == ChannelAuthors.Implementer))
                    return false;

                if (!stopwatch.IsRunning)
                    stopwatch.Start();

                return true;
            }, TimeSpan.FromMinutes(4)), $"{implementerId} never reported.\n{Read_All(harness, ORCH, implementerId)}");

            // …AND STOPS WHEN THE SUPERVISOR HAS TAKEN A TURN ON IT. No owner message was written in
            // between: this wait is the whole difference between this stage and the last one.
            Assert.True(PrintRunnerTestHarness.Drive_Until(dispatcher, () => Supervisor_Turns(harness, ORCH) >= 2, TimeSpan.FromMinutes(4)),
                $"the supervisor was NOT woken by {implementerId}'s spoke.\n{Read_All(harness, ORCH, implementerId)}");

            stopwatch.Stop();

            var supervisorState = harness.Read_State(SessionRoles.Supervisor, ORCH, "sup");
            var implementerState = harness.Read_State(SessionRoles.Implementer, ORCH, implementerId);
            var cost = supervisorState.ExecutedTurns.Sum(turn => turn.CostUsd ?? 0) + implementerState.ExecutedTurns.Sum(turn => turn.CostUsd ?? 0);

            _output.WriteLine($"spoke entry -> supervisor turn complete: {stopwatch.Elapsed.TotalSeconds:F2} s (test tick 100 ms + {harness.ConfigProvider.Get_Current().Runners.CoalesceWindow.TotalSeconds:F0} s coalesce + the turn itself; the app's own mirror tick is 2 s on top)");
            _output.WriteLine($"round cost: {cost:F4} USD over {supervisorState.ExecutedTurns.Count} supervisor turn(s) and {implementerState.ExecutedTurns.Count} implementer turn(s)");

            // The work itself, not just the traffic about it.
            var fruit = Path.Combine(harness.RepoPath, "fruit.txt");
            Assert.True(File.Exists(fruit), $"{implementerId} never wrote fruit.txt.\n{Read_All(harness, ORCH, implementerId)}");
            Assert.Contains("PLATANO", File.ReadAllText(fruit), StringComparison.OrdinalIgnoreCase);

            // And the loop closed where the owner is looking. COUNTED FROM BEFORE THE REPORT, not against
            // a fixed number: ">= 2" has two routes to it — the turn this test is about, or a single first
            // turn that wrote an unaddressed preamble AND a `TO: owner` block — so it would have passed
            // without the thing it names ever happening.
            var ownerEntries = Owner(harness, ORCH);
            Assert.True(ownerEntries.Count(entry => entry.Author == ChannelAuthors.Supervisor) > ownerEntriesBeforeTheReport,
                $"the supervisor never came back to the owner after the report ({ownerEntriesBeforeTheReport} supervisor entries before, {ownerEntries.Count(entry => entry.Author == ChannelAuthors.Supervisor)} after).\n{Read_All(harness, ORCH, implementerId)}");

            // Exactly one report from the session, not two: the bridge writes the entry, the session
            // must not also (the 1b live round found this the hard way).
            Assert.Single(Spoke(harness, ORCH, implementerId), entry => entry.Author == ChannelAuthors.Implementer);

            File.WriteAllText(Path.Combine(evidence, "REPORT.txt"), string.Join('\n',
            [
                $"spoke entry -> supervisor turn complete: {stopwatch.Elapsed.TotalSeconds:F3} s",
                $"round cost: {cost:F4} USD",
                $"supervisor turns: {supervisorState.ExecutedTurns.Count}, implementer turns: {implementerState.ExecutedTurns.Count}",
                $"supervisor cursors: {string.Join(", ", supervisorState.Cursors.Select(cursor => $"{cursor.SourceKey}={cursor.Delivered.Count} delivered, high water [{cursor.HighWaterIndex}]"))}",
                string.Empty,
                Read_All(harness, ORCH, implementerId),
                string.Empty,
                "--- app log ---",
                string.Join('\n', logged),
            ]));

            _output.WriteLine($"evidence: {Path.Combine(evidence, "REPORT.txt")}");
        }
        finally
        {
            await dispatcher.Stop_Async();
        }
    }

    static int Supervisor_Turns(PrintRunnerTestHarness harness, string orchId)
    {
        return harness.Read_State(SessionRoles.Supervisor, orchId, "sup").ExecutedTurns.Count;
    }

    static IReadOnlyList<IChannelEntry> Spoke(PrintRunnerTestHarness harness, string orchId, string memberId)
    {
        var file = harness.Paths.Get_ImplementerChannelFile(orchId, memberId);
        return File.Exists(file) ? ChannelEntry_Parser.Parse_All(File.ReadAllText(file)) : [];
    }

    static IReadOnlyList<IChannelEntry> Owner(PrintRunnerTestHarness harness, string orchId)
    {
        var file = harness.Paths.Get_OwnerChannelFile(orchId);
        return File.Exists(file) ? ChannelEntry_Parser.Parse_All(File.ReadAllText(file)) : [];
    }

    static string Read_All(PrintRunnerTestHarness harness, string orchId, string memberId)
    {
        var owner = harness.Paths.Get_OwnerChannelFile(orchId);
        var spoke = harness.Paths.Get_ImplementerChannelFile(orchId, memberId);

        return $"--- owner channel ---\n{(File.Exists(owner) ? File.ReadAllText(owner) : "(none)")}\n--- {memberId} channel ---\n{(File.Exists(spoke) ? File.ReadAllText(spoke) : "(none)")}";
    }

    /// <summary>
    /// The smallest repo that can host this — and NOTE WHAT IS NO LONGER IN IT. The 1b probe shipped an
    /// <c>append.sh</c> so its supervisor could write a brief into a spoke with the Bash tool. It is gone:
    /// a supervisor addresses a member with a <c>TO:</c> line in its own final message and the bridge
    /// files it. The session touches no channel file at all, in either direction.
    /// </summary>
    static void Write_ProbeRepo(PrintRunnerTestHarness harness)
    {
        var commands = Path.Combine(harness.RepoPath, ".claude", "commands");
        Directory.CreateDirectory(commands);

        File.WriteAllText(Path.Combine(commands, "supervisor.md"),
            "---\ndescription: probe supervisor\n---\n" +
            "You are the SUPERVISOR of orchestration $ARGUMENTS. You are run by a bridge: you have no watcher and no Monitor.\n\n" +
            "You are woken by EVERY channel you listen to — the owner's, and each member's spoke. The prompt names the channel\n" +
            "each message came from. Your FINAL MESSAGE IS your channel entries: address each part with a line reading\n" +
            "`TO: <channel>` on its own, then the subject, a blank line, and the body. Text before the first `TO:` goes to the owner.\n\n" +
            "To brief or answer imp-1, write `TO: imp-1` and the text. NEVER use Bash to append to a channel file — the bridge\n" +
            "writes every entry from your final message, and a session that also writes one produces it twice.\n\n" +
            "A question ends the turn exactly as an answer does.\n");

        File.WriteAllText(Path.Combine(commands, "implementer.md"),
            "---\ndescription: probe implementer\n---\n" +
            "You are IMPLEMENTER $ARGUMENTS, run by a bridge: no watcher, no Monitor.\n\n" +
            "FIRST, resolve your environment with ONE Bash call and read your channel — the root and the mode travel as\n" +
            "environment variables, which the Read tool does not expand, so this call is the only way you can see them:\n" +
            "`cat \"${AIORCH_SUPERVISION_ROOT}/${AIORCH_ID}/${AIORCH_MEMBER}/channel.md\"`\n\n" +
            "Then do exactly what the newest BRIEF entry says, using the Bash tool, in the working directory you were started in.\n" +
            "Your FINAL MESSAGE IS your channel entry: first line the subject, then a blank line, then the body. Nothing else.\n" +
            "DO NOT append to your channel yourself — the bridge writes your entry from that message.\n");

        // Kept out of the probe deliberately: nothing here writes a channel file, so there is no shell
        // script to make executable and no platform to special-case.
        _ = RuntimeInformation.IsOSPlatform(OSPlatform.Windows);
    }
}
