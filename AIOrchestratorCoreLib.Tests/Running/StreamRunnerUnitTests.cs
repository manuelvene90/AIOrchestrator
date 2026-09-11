using System.Text.Json.Nodes;
using AIOrchestratorCoreLib.Limits;
using AIOrchestratorCoreLib.Running;
using AIOrchestratorCoreLib.Running.RunnerConfigs;
using AIOrchestratorCoreLib.Running.StreamTurn;
using AIOrchestratorCoreLib.Running.TurnLog;
using AIOrchestratorCoreLib.SupervisionPaths;
using Xunit;

namespace AIOrchestratorCoreLib.Tests.Running;

/// <summary>
/// The decisions around the stream transport, each in isolation: which runner may run which role,
/// the fallback ladder and its missing rung, the one rule that governs <c>bg</c>, the translation of
/// a headless session's limits into the shape this app already reads, and the tolerant reading of an
/// undocumented event format.
/// </summary>
public class StreamRunnerUnitTests
{
    // ----- Runner_Support -----

    /// <summary>
    /// The supervisor is runnable on BOTH bridge-driven transports now. It was print-unrunnable only
    /// because the trigger was single-channel, which was never a property of the transport: the
    /// dispatcher owns the trigger and hands both executors the same pending entries. Stream stays the
    /// recommended one on latency (p50 1.27 s against 5.77 s), which is a different question from what
    /// is supported.
    /// </summary>
    [Fact]
    public void TheSupervisorIsRunnableOnBothBridgeDrivenTransports()
    {
        Assert.True(Runner_Support.Supports(SessionRunners.Stream, SessionRoles.Supervisor));
        Assert.True(Runner_Support.Supports(SessionRunners.Print, SessionRoles.Supervisor));

        // The communicator's input is the supervisor's transcript rather than a channel, so no
        // bridge-driven runner can wake it at all.
        Assert.False(Runner_Support.Supports(SessionRunners.Stream, SessionRoles.Communicator));
        Assert.False(Runner_Support.Supports(SessionRunners.Print, SessionRoles.Communicator));

        // bg is a word with no transport behind it in this stage.
        foreach (var role in SessionRole_Names.ALL)
            Assert.False(Runner_Support.Supports(SessionRunners.Bg, role));

        Assert.True(Runner_Support.Is_BridgeDriven(SessionRunners.Stream));
        Assert.True(Runner_Support.Is_BridgeDriven(SessionRunners.Print));
        Assert.False(Runner_Support.Is_BridgeDriven(SessionRunners.Terminal));
    }

    /// <summary>
    /// The single-source limit is GONE, and this is the test that used to pin it. A supervisor is woken
    /// by the owner channel AND every open spoke, on both bridge-driven transports — so what used to be
    /// a warning is now a resolved source list, and the two runners must not differ about it.
    /// </summary>
    [Fact]
    public void BothBridgeDrivenRunners_RunTheSupervisor_AndNeitherIsSingleSourceAnyMore()
    {
        Assert.True(Runner_Support.Supports(SessionRunners.Stream, SessionRoles.Supervisor));
        Assert.True(Runner_Support.Supports(SessionRunners.Print, SessionRoles.Supervisor));

        foreach (var role in SessionRole_Names.ALL)
            Assert.Equal(Runner_Support.Supports(SessionRunners.Stream, role), Runner_Support.Supports(SessionRunners.Print, role));

        // The one role neither can run, and not for want of channels: its input is the supervisor's
        // transcript, which is not a channel at all.
        Assert.False(Runner_Support.Supports(SessionRunners.Stream, SessionRoles.Communicator));
    }

    // ----- the ladder -----

    [Fact]
    public void TheLadderStepsOverTheRungItCannotRun_AndSaysSo()
    {
        Assert.Equal(SessionRunners.Bg, RunnerFallback_Ladder.Next_OrNull(SessionRunners.Stream));
        Assert.Equal(SessionRunners.Print, RunnerFallback_Ladder.Next_Implemented_OrNull(SessionRunners.Stream));
        Assert.Null(RunnerFallback_Ladder.Next_OrNull(SessionRunners.Print));

        // Terminal is not a rung: falling into it automatically would spawn a window nobody asked
        // for, on a machine that may have no display.
        Assert.Null(RunnerFallback_Ladder.Next_OrNull(SessionRunners.Terminal));

        Assert.Contains("over 'bg'", RunnerFallback_Ladder.Describe_Fallback("imp-1", SessionRunners.Stream, SessionRunners.Print, "it died"));
    }

    // ----- bg is never run with Remote Control on -----

    [Fact]
    public void SilenceAboutSettings_GetsTheCanonicalOnes_AndAContradictionIsRefused()
    {
        Assert.Equal(BgSettings_Rule.CANONICAL_SETTINGS, BgSettings_Rule.Resolve(null).Settings);
        Assert.Null(BgSettings_Rule.Resolve(null).RefusalReason);

        Assert.Contains(BgSettings_Rule.DISABLE_REMOTE_CONTROL_KEY, BgSettings_Rule.CANONICAL_SETTINGS);

        var explicitlyOn = BgSettings_Rule.Resolve("""{"disableRemoteControl":false}""");
        Assert.Null(explicitlyOn.Settings);
        Assert.Contains("register with the Anthropic bridge", explicitlyOn.RefusalReason);

        var silentAboutIt = BgSettings_Rule.Resolve("""{"model":"haiku"}""");
        Assert.Null(silentAboutIt.Settings);
        Assert.NotNull(silentAboutIt.RefusalReason);

        // A settings FILE path cannot be shown to disable it, so it is refused rather than trusted.
        Assert.NotNull(BgSettings_Rule.Resolve("/etc/claude/settings.json").RefusalReason);
    }

    [Fact]
    public void TheLoaderRefusesABgRole_AndTheRefusalIsReadableByTheOwner()
    {
        var configs = RunnerConfigs_Json.Parse(JsonNode.Parse("""
        {"runners":{"supervisor":{"runner":"bg","settings":"{\"disableRemoteControl\":false}"}}}
        """) as JsonObject);

        // Refused back to TERMINAL — a window the owner can see — not down the ladder: the ladder is
        // for a transport that broke, this is one that was never allowed to start.
        Assert.Equal(SessionRunners.Terminal, configs.Get_ForRole(SessionRoles.Supervisor).Runner);

        var rejection = Assert.Single(configs.Rejections);
        Assert.Contains("supervisor", rejection);
        Assert.Contains("refused", rejection);
    }

    [Fact]
    public void ABgRoleThatSaysNothing_RunsWithRemoteControlDisabled_NotWithout()
    {
        var configs = RunnerConfigs_Json.Parse(JsonNode.Parse("""{"runners":{"supervisor":{"runner":"bg"}}}""") as JsonObject);
        var role = configs.Get_ForRole(SessionRoles.Supervisor);

        Assert.Equal(SessionRunners.Bg, role.Runner);
        Assert.Equal(BgSettings_Rule.CANONICAL_SETTINGS, role.Settings);
        Assert.Empty(configs.Rejections);
    }

    [Fact]
    public void TheSilenceLimitIsConfigurable_AndRoundTrips()
    {
        var configs = RunnerConfigs_Json.Parse(JsonNode.Parse("""{"printRunner":{"streamSilenceSeconds":45}}""") as JsonObject);
        Assert.Equal(TimeSpan.FromSeconds(45), configs.SilenceLimit);

        var written = new JsonObject();
        RunnerConfigs_Json.Write(written, configs);
        Assert.Equal(TimeSpan.FromSeconds(45), RunnerConfigs_Json.Parse(written).SilenceLimit);

        // Absent, zero and negative all mean the default: a hand-edited file must not be able to
        // switch the heartbeat off by mistake.
        Assert.Equal(RunnerConfigs_Factory.DEFAULT_SILENCE_LIMIT, RunnerConfigs_Json.Parse(JsonNode.Parse("{}") as JsonObject).SilenceLimit);
        Assert.Equal(RunnerConfigs_Factory.DEFAULT_SILENCE_LIMIT, RunnerConfigs_Json.Parse(JsonNode.Parse("""{"printRunner":{"streamSilenceSeconds":0}}""") as JsonObject).SilenceLimit);
    }

    // ----- the command line -----

    [Fact]
    public void TheStreamCommandLine_CarriesVerbose_AndNoPositionalPrompt()
    {
        var paths = SupervisionPaths_Factory.Create(Path.Combine(Path.GetTempPath(), $"aiorch-stream-args-{Guid.NewGuid():N}"));
        var state = AIOrchestratorCoreLib.Running.PrintSessionState.PrintSessionState_Factory.Create_New(
            "11111111-1111-1111-1111-111111111111", SessionRoles.Supervisor, "repo-1", "sup", "/repo", "haiku", paths.Get_OwnerChannelFile("repo-1"), []);

        var arguments = StreamTurnCommand_Builder.Build_Arguments(state, AIOrchestratorCoreLib.Running.RoleRunnerConfig.RoleRunnerConfig_Factory.Create_Default(SessionRoles.Supervisor), state.SessionId, resumeTranscript: false, null);

        Assert.Contains("--verbose", arguments);
        Assert.Contains("--include-hook-events", arguments);
        Assert.Equal("stream-json", arguments[arguments.ToList().IndexOf("--input-format") + 1]);
        Assert.Equal("stream-json", arguments[arguments.ToList().IndexOf("--output-format") + 1]);

        // There is no positional prompt in this mode — the role command is sent as a MESSAGE.
        Assert.DoesNotContain(arguments, argument => argument.StartsWith("/supervisor", StringComparison.Ordinal));
    }

    // ----- the event format -----

    [Fact]
    public void TheEventReaderSurvivesWhatItHasNeverSeen()
    {
        Assert.Null(StreamEvent_Reader.Parse_OrNull("not json at all"));
        Assert.Null(StreamEvent_Reader.Parse_OrNull(string.Empty));

        var assistant = StreamEvent_Reader.Parse_OrNull("""
        {"type":"assistant","message":{"content":[{"type":"thinking","thinking":"…"},{"type":"text","text":"hello"},{"type":"tool_use","name":"Bash"}]}}
        """)!;

        Assert.Equal("hello", StreamEvent_Reader.Read_AssistantText(assistant));
        Assert.Equal(["Bash"], StreamEvent_Reader.Read_ToolNames(assistant));

        // A shape with no content at all is empty, not an exception.
        Assert.Equal(string.Empty, StreamEvent_Reader.Read_AssistantText(StreamEvent_Reader.Parse_OrNull("""{"type":"assistant"}""")!));

        var hook = StreamEvent_Reader.Parse_OrNull("""{"type":"system","subtype":"hook_response","hook_name":"PreToolUse:Bash"}""")!;
        Assert.Equal("PreToolUse:Bash", StreamEvent_Reader.Read_HookName(hook));
        Assert.Null(StreamEvent_Reader.Read_HookName(StreamEvent_Reader.Parse_OrNull("""{"type":"system","subtype":"init"}""")!));
    }

    [Fact]
    public void OneMessageLine_IsValidJson_EvenCarryingAChannelEntry()
    {
        // The text is a channel entry: newlines, quotes, em-dashes. Hand-built JSON is how those
        // become a transport error the owner reads as a session that stopped answering.
        var line = StreamUserMessage_Json.Build_Line("## [12] FROM supervisor — \"go\"\n\nbody\twith\ttabs");
        var parsed = StreamEvent_Reader.Parse_OrNull(line);

        Assert.NotNull(parsed);
        Assert.Equal("user", StreamEvent_Reader.Read_Type(parsed!));
        Assert.DoesNotContain("\n", line);
    }

    // ----- the limits translation -----

    [Fact]
    public void AFractionBecomesAPercentage_BecauseTheTolerantReaderDeliberatelyWillNot()
    {
        var payload = RateLimitEvent_Translator.Build_StatuslinePayload_OrNull(
            (JsonObject)JsonNode.Parse("""{"unifiedWindows":{"five_hour":{"utilization":0.73,"resetsAt":1788652200},"seven_day":{"utilization":1.0,"resetsAt":1788922800}}}""")!,
            "Haiku")!;

        var windows = RateLimits_Reader.Read_Windows(payload.ToJsonString());

        Assert.Equal(73, windows.Single(window => window.Window == "5h").Percent, 2);

        // A utilisation of exactly 1.0 is a FULL window — 100%. LimitData_Parser reads a bare 1 as
        // one percent (a phantom that once pinned the alert latch at its ceiling), which is right
        // for the status line's units and wrong for these; converting here, where the unit is known,
        // is what keeps both correct.
        Assert.Equal(100, windows.Single(window => window.Window == "weekly").Percent, 2);
    }

    [Fact]
    public void AnEventWithNoReadableWindow_ProducesNoReading()
    {
        Assert.Null(RateLimitEvent_Translator.Build_StatuslinePayload_OrNull((JsonObject)JsonNode.Parse("""{"status":"allowed"}""")!, null));
        Assert.Null(RateLimitEvent_Translator.Build_StatuslinePayload_OrNull((JsonObject)JsonNode.Parse("""{"unifiedWindows":{"five_hour":{"resetsAt":1788652200}}}""")!, null));
        Assert.Equal("no readable window", RateLimitEvent_Translator.Describe((JsonObject)JsonNode.Parse("""{}""")!));
    }

    // ----- the turn log -----

    [Fact]
    public void TheTurnLogRolls_AndTheReaderSpansBothFiles()
    {
        var folder = Path.Combine(Path.GetTempPath(), $"aiorch-turnlog-{Guid.NewGuid():N}");
        Directory.CreateDirectory(folder);

        try
        {
            var logFile = Path.Combine(folder, TurnLog_Store.FILE_NAME);

            File.WriteAllText(logFile + TurnLog_Store.ROLLED_SUFFIX, """{"type":"old"}""" + "\n");
            TurnLog_Store.Append_StreamEvent(logFile, "repo-1/imp-1/1", """{"type":"result"}""");
            TurnLog_Store.Append_StreamEvent(logFile, "repo-1/imp-1/2", "garbage, not json");

            var records = TurnLog_Store.Read_LastRecords(logFile, 10);

            Assert.Equal(3, records.Count);
            Assert.Equal("old", records[0]["type"]!.GetValue<string>());

            // A line that is not JSON is still evidence: kept as a record that says so.
            Assert.Equal("unparsed", records[2]["type"]!.GetValue<string>());

            // The LAST turn is keyed on the request id, not on a result boundary — a straggler event
            // (the Stop hook answers after the result, measured) would otherwise open a phantom turn.
            var lastTurn = TurnLog_Store.Read_LastTurn(logFile, 10);
            Assert.Single(lastTurn);
            Assert.Equal("repo-1/imp-1/2", TurnLog_Store.Read_RequestId_OrNull(lastTurn[0]));
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    [Fact]
    public void TwoRolesSharingAFolder_DoNotShareATurnLog()
    {
        var paths = SupervisionPaths_Factory.Create(Path.Combine(Path.GetTempPath(), $"aiorch-turnlog-paths-{Guid.NewGuid():N}"));

        var supervisor = TurnLog_Store.Get_File(paths, SessionRoles.Supervisor, "repo-1", "sup");
        var communicator = TurnLog_Store.Get_File(paths, SessionRoles.Communicator, "repo-1", "com");

        Assert.NotEqual(supervisor, communicator);
        Assert.Equal(Path.GetDirectoryName(supervisor), Path.GetDirectoryName(communicator));
    }
}
