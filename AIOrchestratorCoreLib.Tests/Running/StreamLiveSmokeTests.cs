using System.Diagnostics;
using System.Runtime.InteropServices;
using AIOrchestratorCoreLib.Channels;
using AIOrchestratorCoreLib.Logging.OrchestrationLog;
using AIOrchestratorCoreLib.Running;
using AIOrchestratorCoreLib.Running.PrintTurnDispatcher;
using AIOrchestratorCoreLib.Running.TurnLog;
using AIOrchestratorCoreLib.Sessions;
using Xunit;
using Xunit.Abstractions;

namespace AIOrchestratorCoreLib.Tests.Running;

/// <summary>
/// THE REAL THING, END TO END: a supervisor in the stream and an implementer in print, in a
/// throwaway orchestration under its own supervision root, driven by the real <c>claude</c> on
/// <c>--model haiku</c>. The owner writes twice; both supervisor turns must run in ONE process, the
/// supervisor must brief the implementer, and the implementer must answer in its own spoke.
///
/// Skipped unless <c>AIORCH_LIVE=1</c> — it costs tokens and minutes. The skip says so, because a
/// silent one would let a CLI upgrade pass the suite without ever being measured (the same rule the
/// contract tests' Live category states).
/// </summary>
public class StreamLiveSmokeTests(ITestOutputHelper output)
{
    public const string ENABLE_ENV = "AIORCH_LIVE";

    readonly ITestOutputHelper _output = output;

    sealed class LiveFactAttribute : FactAttribute
    {
        public LiveFactAttribute()
        {
            if (Environment.GetEnvironmentVariable(ENABLE_ENV) != "1")
                Skip = $"Live smoke — set {ENABLE_ENV}=1 to run a real supervisor + implementer on --model haiku";
        }
    }

    [LiveFact]
    public async Task TwoOwnerMessages_TwoSupervisorTurns_OneProcess_AndTheImplementerIsBriefed()
    {
        using var harness = new PrintRunnerTestHarness("supervisor:stream,implementer", turnTimeoutMinutes: 4);
        var evidence = Path.Combine(Path.GetTempPath(), "aiorchestrator-stage-1b", "live");
        Directory.CreateDirectory(evidence);

        Write_ProbeRepo(harness);

        var orchId = "repo-1";
        harness.Register_Supervisor(orchId);
        var (_, implementerId) = harness.Register_Member(MemberKinds.Implementer, orchId);

        List<string> logged = [];
        harness.Log.EntryLogged += entry => logged.Add($"{entry.Level} {entry.Message}");

        var dispatcher = PrintTurnDispatcher_Factory.Create(
            harness.Paths, harness.Store, harness.ConfigProvider,
            AIOrchestratorCoreLib.Running.TurnExecutor.TurnExecutor_Factory.Create_All(harness.Paths, AIOrchestratorCoreLib.Running.ClaudeInvocation.ClaudeInvocation_Resolver.Resolve_ForThisOs(), harness.Log, harness.ConfigProvider),
            harness.Log,
            TimeSpan.FromSeconds(5));

        try
        {
            var firstTurn = Drive_OwnerMessage(harness, dispatcher, orchId, "Brief imp-1 to write the word PLATANO into a file called fruit.txt, then tell me you have done it.", expectedTurns: 1);
            var secondTurn = Drive_OwnerMessage(harness, dispatcher, orchId, "Thank you. In one short sentence, what did you ask imp-1 to do?", expectedTurns: 2);

            _output.WriteLine($"supervisor turn 1: {firstTurn.TotalSeconds:F2} s (cold: process start + boot with the role command + the owner's message)");
            _output.WriteLine($"supervisor turn 2: {secondTurn.TotalSeconds:F2} s (warm: one message on a living process's stdin)");

            // ONE PROCESS FOR BOTH TURNS — the whole claim of this runner. The executor logs the
            // command line every time it starts one, so two turns and one launch line is the proof.
            var launches = logged.Where(line => line.Contains("Stream session 'sup' starting:", StringComparison.Ordinal)).ToList();
            Assert.Single(launches);
            _output.WriteLine(launches[0]);

            var supervisorState = harness.Read_State(SessionRoles.Supervisor, orchId, "sup");
            Assert.Equal(2, supervisorState.ExecutedTurns.Count);

            // The supervisor briefed the implementer, and the implementer answered in its own spoke.
            Assert.True(PrintRunnerTestHarness.Drive_Until(dispatcher, () => harness.Read_State(SessionRoles.Implementer, orchId, implementerId).ExecutedTurns.Count >= 1, TimeSpan.FromMinutes(4)),
                $"the implementer never ran. Owner channel:\n{Read_OwnerChannel(harness, orchId)}\nLog:\n{string.Join("\n", logged)}");

            var spoke = ChannelEntry_Parser.Parse_All(harness.Read_Channel(orchId, implementerId));
            Assert.Contains(spoke, entry => entry.Author == ChannelAuthors.Supervisor);
            // EXACTLY ONE report, not two: the bridge writes the entry, the session must not also.
            Assert.Single(spoke, entry => entry.Author == ChannelAuthors.Implementer);

            // THE WORK ITSELF, not just the traffic about it: the owner asked the supervisor for a
            // file, the supervisor briefed the implementer, and the implementer wrote it. Without
            // this the test would pass on two sessions politely talking to each other.
            var fruit = Path.Combine(harness.RepoPath, "fruit.txt");
            Assert.True(File.Exists(fruit), $"imp-1 never wrote fruit.txt. Its channel:\n{harness.Read_Channel(orchId, implementerId)}");
            Assert.Contains("PLATANO", File.ReadAllText(fruit), StringComparison.OrdinalIgnoreCase);

            // /tail, for both — the owner's window into two headless sessions.
            var supervisorTail = TurnLog_Formatter.Format_Tail("sup", TurnLog_Store.Read_LastRecords(TurnLog_Store.Get_File(harness.Paths, SessionRoles.Supervisor, orchId, "sup"), TurnLog_Formatter.DEFAULT_TAIL_EVENTS));
            var implementerTail = TurnLog_Formatter.Format_Tail(implementerId, TurnLog_Store.Read_LastRecords(TurnLog_Store.Get_File(harness.Paths, SessionRoles.Implementer, orchId, implementerId), TurnLog_Formatter.DEFAULT_TAIL_EVENTS));

            Assert.Contains("turn ok", supervisorTail);
            Assert.Contains("turn ok", implementerTail);

            File.WriteAllText(Path.Combine(evidence, "REPORT.txt"), string.Join('\n',
            [
                $"supervisor turn 1: {firstTurn.TotalSeconds:F3} s",
                $"supervisor turn 2: {secondTurn.TotalSeconds:F3} s",
                $"stream launches: {launches.Count}",
                launches[0],
                string.Empty,
                "--- owner channel ---",
                Read_OwnerChannel(harness, orchId),
                string.Empty,
                $"--- {implementerId} channel ---",
                harness.Read_Channel(orchId, implementerId),
                string.Empty,
                "--- /tail sup ---",
                supervisorTail,
                string.Empty,
                $"--- /tail {implementerId} ---",
                implementerTail,
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

    static TimeSpan Drive_OwnerMessage(PrintRunnerTestHarness harness, IPrintTurnDispatcher dispatcher, string orchId, string text, int expectedTurns)
    {
        Assert.True(ChannelAppender.Append_OwnerEntry(harness.Paths.Get_OwnerChannelFile(orchId), text, DateTime.Now));

        var stopwatch = Stopwatch.StartNew();

        Assert.True(PrintRunnerTestHarness.Drive_Until(dispatcher, () => harness.Read_State(SessionRoles.Supervisor, orchId, "sup").ExecutedTurns.Count >= expectedTurns, TimeSpan.FromMinutes(4)),
            $"supervisor turn {expectedTurns} never completed. Owner channel:\n{Read_OwnerChannel(harness, orchId)}");

        stopwatch.Stop();
        return stopwatch.Elapsed;
    }

    static string Read_OwnerChannel(PrintRunnerTestHarness harness, string orchId)
    {
        var file = harness.Paths.Get_OwnerChannelFile(orchId);
        return File.Exists(file) ? File.ReadAllText(file) : "(no owner channel)";
    }

    /// <summary>
    /// The smallest repo that can host this: two PROJECT-level role commands (so nothing outside the
    /// temp folder is touched) and one script that appends a correctly numbered channel entry — the
    /// job the kit's channel-append.sh does, cut down to what this measure needs.
    /// </summary>
    static void Write_ProbeRepo(PrintRunnerTestHarness harness)
    {
        var commands = Path.Combine(harness.RepoPath, ".claude", "commands");
        Directory.CreateDirectory(commands);

        var append = Path.Combine(harness.RepoPath, "append.sh");

        File.WriteAllText(append,
            "#!/bin/sh\n" +
            "# append.sh CHANNEL SUBJECT BODY — one channel entry, numbered from the file itself.\n" +
            "ch=\"$1\"; subject=\"$2\"; body=\"$3\"\n" +
            "next=$(grep -o '^## \\[[0-9]*\\]' \"$ch\" | grep -o '[0-9]*' | sort -n | tail -1)\n" +
            "next=$((${next:-0}+1))\n" +
            "printf '\\n## [%s] FROM supervisor — %s — %s\\n%s\\n' \"$next\" \"$(date '+%Y-%m-%d %H:%M')\" \"$subject\" \"$body\" >> \"$ch\"\n");

        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            File.SetUnixFileMode(append, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);

        File.WriteAllText(Path.Combine(commands, "supervisor.md"),
            "---\ndescription: probe supervisor\n---\n" +
            "You are the SUPERVISOR of orchestration $ARGUMENTS. You are run by a bridge: you have no watcher and no Monitor, " +
            "you are woken by the bridge when the owner writes, and your FINAL MESSAGE IS your channel entry (first line the subject, " +
            "then a blank line, then the body). A question ends the turn exactly as an answer does.\n\n" +
            "To brief an implementer, use the Bash tool exactly once:\n" +
            "`sh append.sh \"$AIORCH_SUPERVISION_ROOT/$ARGUMENTS/imp-1/channel.md\" \"BRIEF\" \"<what imp-1 must do>\"`\n\n" +
            "Do not write to your own channel yourself — the bridge does that from your final message.\n");

        File.WriteAllText(Path.Combine(commands, "implementer.md"),
            "---\ndescription: probe implementer\n---\n" +
            "You are IMPLEMENTER $ARGUMENTS, run by a bridge: no watcher, no Monitor.\n\n" +
            "FIRST, resolve your environment with ONE Bash call and read your channel — the root and the mode travel as\n" +
            "environment variables, which the Read tool does not expand, so this call is the only way you can see them:\n" +
            "`cat \"${AIORCH_SUPERVISION_ROOT}/${AIORCH_ID}/${AIORCH_MEMBER}/channel.md\"`\n\n" +
            "Then do exactly what the newest BRIEF entry says, using the Bash tool, in the working directory you were started in.\n" +
            "Your FINAL MESSAGE IS your channel entry: first line the subject, then a blank line, then the body. Nothing else.\n" +
            "DO NOT append to your channel yourself — the bridge writes your entry from that message, and a session that also\n" +
            "writes one produces the same report twice (measured live on 2026-09-06, when this line was missing).\n");
    }
}
