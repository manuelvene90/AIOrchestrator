using System.Diagnostics;
using AIOrchestratorCoreLib.Tests.TestSupport;
using Xunit;

namespace AIOrchestratorCoreLib.Tests.Kit;

/// <summary>
/// THE ADVISORY THAT LETS A LONG TURN CHOOSE ITS OWN ENDING.
///
/// Measured on the production VPS, 2026-09-09: the median member turn is 10 model calls, but the
/// tail runs 60 to 92 and hits the print runner's deadline — and **41 % of that night's member
/// tokens sat in turns that ran to it**, where the in-turn context had climbed to around 200 k. The
/// closing turn is the hard net; this hook is the chance to stop before the axe, at a point the
/// session picks.
///
/// It ADVISES and never blocks (decision 21), so what has to be proven is exactly four things: it
/// says nothing before the threshold, it speaks once per TURN past it, it never speaks to a role
/// whose turn is the owner's phone line, and no malformed input can make it emit anything at all.
/// None of that is readable from C# — the behaviour lives in bash — so this runs the shipped script
/// the way the CLI runs it: real payloads on stdin, the answer read off stdout, in the same style as
/// RunToTheEndHookTests next door.
///
/// EVERY PAYLOAD HERE DEFAULTS TO THE REAL SHAPE, AND THAT IS PART OF THE FIX. The first version of
/// this fixture put `agent_type` on every payload, and all 21 of its cases passed while the shipped
/// hook fired 0 times in 45 calls on a live member session — decision 20 in its purest form. The
/// field is ABSENT on the sessions this app spawns (no `--agent`, no agents in the kit): captured
/// both ways on 2026-09-10 against CLI 2.1.267, once with this machine's plugins on (every
/// main-agent payload carried `vibe-framework:vibe-agent`) and once with them off in the probe's own
/// settings (the key was not in the payload at all for the main agent, while the Explore sub-agent's
/// Bash still carried `agent_type` = `Explore`). The second is the app's shape, so it is the default
/// here and only the sub-agent cases add the field.
///
/// THERE IS NO TEST OF THE FILE'S UNIX MODE, deliberately. The three skills wire the hook as
/// `bash "${CLAUDE_PLUGIN_ROOT}/hooks/soft-boundary-check.sh"`, so the executable bit is never
/// consulted, and the kit's seven other hooks all ship 100644 (measured on the branch source
/// 2026-09-10) — a test pinning 100755 on this one alone pinned an accident, and on Windows it
/// returned early and passed while asserting nothing. The mode that IS load-bearing is `kit/bin/`,
/// where a helper is invoked by name from PATH, and RoleHooksAreShippedTests already holds that line.
///
/// Nothing here skips. If bash is missing the test FAILS: a harness that cannot run what it tests
/// must not certify it (decision 20).
/// </summary>
public class SoftBoundaryHookTests : IDisposable
{
    /// <summary>The default the shipped script carries. Restated here so a change to either side is a failing test rather than a silent drift.</summary>
    const int DEFAULT_THRESHOLD = 35;

    /// <summary>
    /// A sub-agent's `agent_type`, captured 2026-09-10: the parent's session_id, the parent's
    /// prompt_id, and this field naming the agent.
    /// </summary>
    const string SUB_AGENT = "Explore";

    readonly string _temp;

    /// <summary>
    /// ONE SESSION, MANY TURNS — the shape of every member outside fresh mode, and the shape the
    /// per-session counter got wrong. Every payload in this class carries this one session id, so a
    /// test that passes only because the session changed cannot pass here.
    /// </summary>
    readonly string _session = Guid.NewGuid().ToString();

    public SoftBoundaryHookTests()
    {
        _temp = Path.Combine(Path.GetTempPath(), $"aiorch-softboundary-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_temp);
    }

    public void Dispose() => Directory.Delete(_temp, recursive: true);

    /// <summary>
    /// THE ORDINARY TURN HEARS NOTHING. The median member turn is 10 calls and 80 % of them are
    /// under 9, so a threshold that fired early would be advising the whole population — which is
    /// how the named risk (short thinking: an agent that knows it may be stopped takes the smaller
    /// decision) would arrive in every turn instead of the top tenth.
    /// </summary>
    [Fact]
    public void NothingIsSaidBeforeTheThreshold()
    {
        var turn = New_Turn();

        for (var call = 1; call < DEFAULT_THRESHOLD; call++)
            Assert.Equal("", Advisory("implementer", turn, call));
    }

    /// <summary>
    /// The whole point: past the threshold the model is told, in its own context. The implementer
    /// case crosses the SHIPPED default, the other two a small threshold — the role gate is what
    /// differs between them, and 35 spawned processes per role prove nothing the third does not.
    /// </summary>
    [Theory]
    [InlineData("implementer", null)]
    [InlineData("reviewer", "4")]
    [InlineData("solo", "4")]
    public void PastTheThreshold_TheAdvisoryReachesTheModel(string role, string? threshold)
    {
        var bar = threshold == null ? DEFAULT_THRESHOLD : int.Parse(threshold);
        var turn = New_Turn();

        for (var call = 1; call < bar; call++)
            Assert.Equal("", Run_Raw(role, Payload(turn, call), threshold).Output);

        var advisory = Run_Raw(role, Payload(turn, bar), threshold).Output;

        Assert.Contains("\"hookEventName\":\"PreToolUse\"", advisory);
        Assert.Contains("additionalContext", advisory);
        Assert.Contains("SOFT BOUNDARY", advisory);
        // THE TURN'S COUNT, and the text says whose: a sub-agent's calls are spent on this turn, so
        // "you are N calls in" was a claim about the recipient the hook cannot make (re-review,
        // 2026-09-10: 37 counted, 7 of them the main agent's).
        Assert.Contains($"this turn has made {bar} tool calls", advisory);
        Assert.Contains("That total is the TURN's, not only yours", advisory);
    }

    /// <summary>
    /// ONCE PER TURN, NOT ONCE PER CALL PAST THE THRESHOLD. A 92-call turn would otherwise carry 57
    /// copies of the same paragraph — a waterfall, which is the thing this system exists to prevent
    /// (decision 14), and which would drown the advice it is trying to give.
    /// </summary>
    [Fact]
    public void ItSpeaksOnceAndThenNeverAgain()
    {
        var turn = New_Turn();

        var spoken = 0;

        // Threshold 4 and 30 calls: 26 of them are past the bar, which is the shape a 92-call turn
        // has past 35 — and it costs a fifth of the processes.
        for (var call = 1; call <= 30; call++)
        {
            if (Run_Raw("implementer", Payload(turn, call), threshold: "4").Output.Length > 0)
                spoken++;
        }

        Assert.Equal(1, spoken);
    }

    /// <summary>
    /// EVERY TURN OF ONE SESSION IS ADVISED ON ITS OWN — the count is keyed on `prompt_id`, not on
    /// `session_id`, and this is the case that says why.
    ///
    /// `RoleRunnerConfig_Factory.Create_Default` gives `Fresh` to the general supervisor alone;
    /// implementer, reviewer and solo default to Terminal + Transcript, where ONE session spans every
    /// turn the member ever takes. Keyed on the session, the latch latched for the life of the
    /// session: measured 2026-09-10 on the pre-fix script, one session id with four 40-call turns
    /// produced advisories 0, 1, 0, 0 — once in four turns, and not even in the turn that earned it,
    /// because the `1` landed on the first call of turn 2 (tool_use_id numbering restarts there).
    /// So the second turn's FIRST call is asserted silent too, not only its threshold call loud.
    /// </summary>
    [Fact]
    public void EveryTurnOfOneSessionIsAdvisedOnItsOwn()
    {
        var advisories = new List<int>();

        const int bar = 6;

        for (var t = 1; t <= 3; t++)
        {
            var turn = New_Turn();
            var spoken = 0;

            for (var call = 1; call <= bar; call++)
            {
                var advisory = Run_Raw("implementer", Payload(turn, call), threshold: bar.ToString()).Output;

                if (advisory.Length > 0)
                {
                    spoken++;

                    // A turn is advised at ITS OWN threshold, never on a call it inherited from the
                    // turn before: this is the assertion the 0, 1, 0, 0 measurement would fail.
                    Assert.Equal(bar, call);
                }
            }

            advisories.Add(spoken);
        }

        Assert.Equal(new[] { 1, 1, 1 }, advisories);
    }

    /// <summary>
    /// AN OLDER CLI THAT SENDS NO `prompt_id` STILL GETS AN ADVISORY. The fallback is `session_id`,
    /// which restores the old per-session behaviour rather than going silent — advising once per
    /// session is worse than once per turn and far better than never.
    /// </summary>
    [Fact]
    public void WithNoPromptId_TheCountFallsBackToTheSession()
    {
        for (var call = 1; call < 6; call++)
            Assert.Equal("", Run_Raw("implementer", Payload(turn: null, call: call), threshold: "6").Output);

        Assert.Contains("SOFT BOUNDARY", Run_Raw("implementer", Payload(turn: null, call: 6), threshold: "6").Output);
    }

    /// <summary>
    /// IT ADVISES, IT NEVER BLOCKS (decision 21: hooks advise, the app enforces at the point of
    /// effect). Asserted on the firing call, because that is the only output there is and a
    /// permissionDecision smuggled into it would deny a live member's tool call.
    /// </summary>
    [Fact]
    public void NothingItEverEmits_CanDenyACall()
    {
        var turn = New_Turn();

        var advisory = Run_Raw("implementer", Payload(turn, 1), threshold: "1").Output;

        Assert.NotEqual("", advisory);
        Assert.DoesNotContain("permissionDecision", advisory);
        Assert.DoesNotContain("\"decision\"", advisory);
        Assert.DoesNotContain("\"continue\"", advisory);
    }

    /// <summary>
    /// EVERY PATH ENDS AT 0, INCLUDING THE ONE THAT WRITES. The heredoc's own write fails when the
    /// caller has closed stdout (`cat` gets EBADF and returns 1), and that used to be the script's
    /// exit code: measured 2026-09-10, the pre-fix script exited 1 on the firing call with stdout
    /// closed while its header claimed "no exit code but 0 on any path". An untrue absolute in a
    /// comment is a defect here, so the claim and the code were both corrected and this pins it.
    /// </summary>
    [Fact]
    public void TheFiringPath_ExitsZero_EvenWithStdoutClosed()
    {
        var turn = New_Turn();

        Run_Raw("implementer", Payload(turn, 1), threshold: "2");

        var exitCode = Run_WithStdoutClosed("implementer", Payload(turn, 2), threshold: "2");

        Assert.Equal(0, exitCode);
    }

    /// <summary>
    /// THE SUPERVISOR'S TURN IS THE OWNER'S PHONE LINE, and the general supervisor's is their
    /// concierge: advising either to stop and report mid-sentence would interrupt the one
    /// conversation the whole system exists to protect. The empty role is a session outside an
    /// orchestration, which is nobody's business either. Threshold 1, so an exempt role that leaked
    /// through would fire on its first call rather than pass by being under the bar.
    /// </summary>
    [Theory]
    [InlineData("supervisor")]
    [InlineData("general-supervisor")]
    [InlineData("communicator")]
    [InlineData("")]
    public void TheRolesWhoseTurnIsAConversation_AreExempt(string role)
    {
        var turn = New_Turn();

        for (var call = 1; call <= 3; call++)
            Assert.Equal("", Run_Raw(role, Payload(turn, call), threshold: "1").Output);
    }

    /// <summary>
    /// A HOOK THAT CANNOT EVALUATE ITS PREDICATE SAYS SO, AND ALLOWS. Every one of these is a real
    /// shape: a payload the extractor cannot parse, one carrying neither turn key, and keys crafted
    /// to walk out of the state folder they become a path in — asserted for `prompt_id` (which is
    /// read first) and for `session_id` (the fallback).
    /// </summary>
    [Theory]
    [InlineData("not json at all")]
    [InlineData("")]
    [InlineData("{}")]
    [InlineData("{\"prompt_id\":\"\",\"session_id\":\"\"}")]
    [InlineData("{\"prompt_id\":\"../../etc\"}")]
    [InlineData("{\"prompt_id\":\"a/b\"}")]
    [InlineData("{\"session_id\":\"../../etc\"}")]
    [InlineData("{\"session_id\":\"a/b\"}")]
    public void AMalformedPayload_SaysNothingAndAllows(string payload)
    {
        // Threshold 1, so a payload that parsed at all WOULD fire on its first call — without this
        // the cases below would pass by being under the threshold rather than by being refused.
        var (output, exitCode) = Run_Raw("implementer", payload, threshold: "1");

        Assert.Equal("", output);
        Assert.Equal(0, exitCode);
    }

    /// <summary>
    /// THE NUMBER IS OVERRIDABLE so the bridge can tune it per role or per stage without a kit
    /// change — and a TYPO MUST NOT TURN THE GUARD OFF, which is why a non-numeric value falls back
    /// to the default instead of disabling anything. `0` is the one explicit OFF.
    /// </summary>
    [Fact]
    public void TheThreshold_IsOverridableByTheEnvironment()
    {
        var early = New_Turn();
        Assert.Equal("", Run_Raw("implementer", Payload(early, 1), threshold: "3").Output);
        Assert.Equal("", Run_Raw("implementer", Payload(early, 2), threshold: "3").Output);
        Assert.Contains("SOFT BOUNDARY", Run_Raw("implementer", Payload(early, 3), threshold: "3").Output);

        // `0` is OFF, and it stays off past the default bar as well as under it.
        var off = New_Turn();
        for (var call = 1; call <= DEFAULT_THRESHOLD + 5; call++)
            Assert.Equal("", Run_Raw("implementer", Payload(off, call), threshold: "0").Output);

        // A typo falls back to the DEFAULT, which is the one leg that has to spend 35 calls: the
        // fallback is only proved by the advisory arriving exactly there.
        var typo = New_Turn();
        for (var call = 1; call < DEFAULT_THRESHOLD; call++)
            Assert.Equal("", Run_Raw("implementer", Payload(typo, call), threshold: "lots").Output);

        Assert.Contains("SOFT BOUNDARY", Run_Raw("implementer", Payload(typo, DEFAULT_THRESHOLD), threshold: "lots").Output);
    }

    /// <summary>
    /// A NUMBER CAN BE ALL DIGITS AND STILL NOT COMPARABLE, so the guard has to be the RANGE and not
    /// only the alphabet. `99999999999999999999` made `[ … -le 0 ]` exit 2 with "integer expression
    /// expected"; the `if` read that failure as false, the script carried on, the threshold
    /// comparison failed the same way — and a threshold typed one digit too long FIRED ON CALL 1
    /// (measured 2026-09-10 on the pre-fix script, whose stderr named the line). It must fall back
    /// to the default like any other unusable value, which means silence until call 35 and an
    /// advisory there — the second half is what makes this a fallback rather than an off switch.
    /// </summary>
    [Fact]
    public void AThresholdTooLongToCompare_FallsBackToTheDefault()
    {
        const string tooLong = "99999999999999999999";

        var turn = New_Turn();

        for (var call = 1; call < DEFAULT_THRESHOLD; call++)
            Assert.Equal("", Run_Raw("implementer", Payload(turn, call), threshold: tooLong).Output);

        Assert.Contains("SOFT BOUNDARY", Run_Raw("implementer", Payload(turn, DEFAULT_THRESHOLD), threshold: tooLong).Output);
    }

    /// <summary>
    /// A SUB-AGENT'S CALLS COUNT, BUT IT IS NEVER THE ONE ADVISED — and both halves can fail here.
    ///
    /// Captured 2026-09-10: a sub-agent's tool call carries the PARENT's `session_id`, the parent's
    /// `transcript_path` AND the parent's `prompt_id`, with only `agent_type` (plus an `agent_id`)
    /// naming the sub-agent. Their calls cost real tokens so they belong in the count; but telling a
    /// read-only Explore agent to commit and write a report is nonsense, and it would burn the
    /// once-per-turn latch on the wrong recipient — silencing the advisory in exactly the
    /// fan-out-heavy turns it exists for, which decision 16 makes an implementer's default.
    ///
    /// THE COUNTING HALF IS WHAT THIS SHAPE PINS: only 7 of the 12 calls are the main agent's, so
    /// the advisory on call 12 is possible ONLY if the sub-agent's 5 calls were counted. The previous
    /// version of this case put 34 main-agent calls before the sub-agent's, and passed unchanged
    /// with sub-agent counting deleted.
    /// </summary>
    [Fact]
    public void ASubAgentsCallsCount_ButTheAdvisoryIsNeverGivenToIt()
    {
        const int bar = 11;

        var turn = New_Turn();

        for (var call = 1; call <= 6; call++)
            Assert.Equal("", Run_Raw("implementer", Payload(turn, call), threshold: bar.ToString()).Output);

        // The sub-agent's own calls carry the turn past the threshold and hear nothing.
        for (var call = 7; call <= bar; call++)
            Assert.Equal("", Run_Raw("implementer", Payload(turn, call, agentType: SUB_AGENT), threshold: bar.ToString()).Output);

        var advisory = Run_Raw("implementer", Payload(turn, bar + 1), threshold: bar.ToString()).Output;

        Assert.Contains("SOFT BOUNDARY", advisory);
        Assert.Contains($"this turn has made {bar + 1} tool calls", advisory);
    }

    /// <summary>
    /// THE 24 h SWEEP TAKES A TURN WHOLE OR NOT AT ALL. The counter, the marker and the latch used to
    /// be three siblings named after the session: the sweep removed the marker and the latch by mtime
    /// while every call refreshed the counter's, so a live session older than a day was UN-LATCHED
    /// and spoke a second time (measured 2026-09-10 on the pre-fix script). With the marker gone the
    /// gate can no longer tell a sub-agent from the main agent either. One directory per turn is the
    /// fix, and both directions are asserted here: a live turn keeps its state AND its latch, a dead
    /// one goes entirely.
    /// </summary>
    [Fact]
    public void TheDailySweep_TakesADeadTurnWhole_AndLeavesALiveTurnLatched()
    {
        var live = New_Turn();
        var dead = New_Turn();

        foreach (var turn in new[] { live, dead })
        {
            Run_Raw("implementer", Payload(turn, 1), threshold: "2");
            Assert.Contains("SOFT BOUNDARY", Run_Raw("implementer", Payload(turn, 2), threshold: "2").Output);
        }

        Age_ByThreeDays(Turn_Dir(live));
        Age_ByThreeDays(Turn_Dir(dead));

        // The live turn takes one more call, which is what refreshes its directory's mtime — the
        // property the old shape had for the counter alone.
        Run_Raw("implementer", Payload(live, 3), threshold: "2");

        // A brand-new turn is what triggers the sweep.
        Run_Raw("implementer", Payload(New_Turn(), 1), threshold: "2");

        Assert.False(Directory.Exists(Turn_Dir(dead)), "a turn older than a day should have been swept");
        Assert.True(Directory.Exists(Turn_Dir(live)), "a live turn must not be swept out from under itself");

        // And the latch inside it survived, so the member is not advised twice.
        Assert.Equal("", Run_Raw("implementer", Payload(live, 4), threshold: "2").Output);
    }

    /// <summary>
    /// THE ADVISORY IS HALF THE MECHANISM, and the half that can go wrong quietly. The named risk in
    /// the design's own risk table is SHORT THINKING — an agent that knows it may be stopped takes
    /// the smaller decision — so the words must forbid that at least as loudly as they ask for the
    /// stop. A rewrite that drops the second half would still pass every behavioural test above.
    ///
    /// The last three pairs are the review's judgement findings, each pinned so it cannot come back:
    /// the hook must not assert a clock it never read, must not invite the model to nominate the
    /// NEAREST thing that looks complete, and must not promise that stopping "loses nothing" — false
    /// in transcript mode, where there is no pack, and false whenever the work dropped is what would
    /// have closed the ledger line.
    /// </summary>
    [Fact]
    public void TheWords_TellTheModelNotToStopShort()
    {
        var turn = New_Turn();

        var advisory = Run_Raw("implementer", Payload(turn, 1), threshold: "1").Output;

        Assert.Contains("nothing is blocked", advisory);
        Assert.Contains("FINISH THAT CHANGE FIRST", advisory);
        Assert.Contains("never stop before something is verifiable", advisory);
        Assert.Contains("DO NOT TAKE A SMALLER DECISION", advisory);
        Assert.Contains("Do not narrow what you were asked to do", advisory);

        Assert.Contains("IT HAS READ NO CLOCK", advisory);
        Assert.DoesNotContain("deadline is close", advisory);

        Assert.Contains("AT, not near", advisory);
        Assert.DoesNotContain("AT OR NEAR", advisory);

        Assert.Contains("if the bridge starts your next turn FRESH", advisory);
        Assert.DoesNotContain("loses nothing", advisory);
    }

    /// <summary>
    /// THE THREE ROLES ARE TAUGHT WHERE TO STOP, NOT WHETHER TO STOP — and neither they nor the hook
    /// may cite a clock.
    ///
    /// Two review findings live in these sentences and nothing else pins them. (1) The advisory used
    /// to be bolted onto the RUN TO THE END paragraph as "the one exception", three lines under
    /// *"'I have reached a natural boundary' is not a reason at all"* — the one feeling that
    /// paragraph exists to override. It is not an exception to it: a turn ending is not the endeavour
    /// stopping, and the boundary only chooses where the turn ends. (2) All three sentences asserted
    /// that "the 30-minute deadline is close", which is a clock no part of this feature has read —
    /// the deadline is the print runner's alone, and a terminal spawn also sets AIORCH_ROLE and is
    /// never killed at all.
    /// </summary>
    [Fact]
    public void TheThreeSkills_TeachWhereToStop_AndCiteNoClock()
    {
        foreach (var role in new[] { "implementer", "reviewer", "solo" })
        {
            var path = KitRepoFiles.Find_RoleProtocol(role)
                ?? throw new Exception($"kit/skills/{role}/SKILL.md was not found — REFUSING to pass about a protocol this test never located.");

            var text = File.ReadAllText(path);

            Assert.Contains("SOFT BOUNDARY — this turn has made N tool calls", text);

            Assert.DoesNotContain("The one exception is announced", text);
            Assert.DoesNotContain("deadline is close", text);
            Assert.DoesNotContain("at or near", text);

            if (role == "reviewer")
            {
                // A partial review filed as a verdict is the failure this half exists to stop — said
                // in the fields this protocol ALREADY has. "PARTIAL" was a fourth word for a property
                // the schema carries (depth, coverage:, UNPROVEN), with no field to put it in and no
                // rule in the supervisor's protocol that reads it (re-review, 2026-09-10).
                Assert.Contains("never filed as a verdict", text);
                Assert.Contains("leaves a finding unproven rather than rounding it", text);
                Assert.DoesNotContain("PARTIAL", text);
                Assert.Contains("carries a count and no clock", text);
            }
            else
            {
                Assert.Contains("adds NO fourth reason", text);
                Assert.Contains("AT that point, not near it", text);
                Assert.Contains("a COUNT, and no clock", text);
            }
        }
    }

    /// <summary>A turn key: `prompt_id` is a lowercase UUID, captured 2026-09-10.</summary>
    static string New_Turn() => Guid.NewGuid().ToString();

    /// <summary>
    /// A PreToolUse payload IN THE REAL SHAPE, captured 2026-09-10 from CLI 2.1.267 with the
    /// machine's plugins switched off — which is the shape of the sessions this app spawns:
    /// `session_id`, `prompt_id`, `tool_use_id`, and NO `agent_type` key at all. Passing an
    /// agentType adds the field the way a sub-agent's call carries it; passing turn: null drops
    /// `prompt_id` the way an older CLI would.
    /// </summary>
    string Payload(string? turn, int call, string? agentType = null)
    {
        var json = "{\"session_id\":\"" + _session + "\""
            + ",\"transcript_path\":\"/tmp/t.jsonl\",\"cwd\":\"/tmp\",\"permission_mode\":\"acceptEdits\""
            + ",\"hook_event_name\":\"PreToolUse\",\"tool_name\":\"Bash\""
            + ",\"tool_input\":{\"command\":\"echo " + call + "\"}"
            + ",\"tool_use_id\":\"toolu_" + call.ToString("D4") + "\"";

        if (turn != null)
            json += ",\"prompt_id\":\"" + turn + "\"";

        if (agentType != null)
            json += ",\"agent_type\":\"" + agentType + "\",\"agent_id\":\"a31a1ffbad2f95ab9\"";

        return json + "}";
    }

    string Turn_Dir(string turn) => Path.Combine(_temp, "aiorch-soft-boundary", turn);

    /// <summary>
    /// Backdates a turn's whole directory past the sweep's `-mtime +1`. Three days, because BSD and
    /// GNU `find` both truncate that comparison to whole days, so "+1" is not "yesterday".
    /// </summary>
    static void Age_ByThreeDays(string directory)
    {
        var when = DateTime.UtcNow.AddDays(-3);

        foreach (var entry in Directory.GetFileSystemEntries(directory))
        {
            if (Directory.Exists(entry))
                Directory.SetLastWriteTimeUtc(entry, when);
            else
                File.SetLastWriteTimeUtc(entry, when);
        }

        Directory.SetLastWriteTimeUtc(directory, when);
    }

    /// <summary>One call at the shipped default, which is the only shape that needs no threshold.</summary>
    string Advisory(string role, string turn, int call) => Run_Raw(role, Payload(turn, call)).Output;

    /// <summary>
    /// Runs the SHIPPED script the way the CLI runs it — payload on stdin, verdict on stdout — with
    /// TMPDIR pointed at this test's own folder, so the per-turn state it keeps is hermetic, nothing
    /// outside the temp folder is touched, and two suites running in two worktrees cannot collide.
    /// </summary>
    (string Output, int ExitCode) Run_Raw(string role, string payload, string? threshold = null)
    {
        var startInfo = Start_Info(role, threshold);

        startInfo.ArgumentList.Add(Find_Hook_OrFail().Replace('\\', '/'));
        startInfo.RedirectStandardOutput = true;

        using var process = Start(startInfo, payload);

        var output = process.StandardOutput.ReadToEnd();

        Wait_OrFail(process);

        return (output.Trim(), process.ExitCode);
    }

    /// <summary>
    /// Runs the shipped script with STDOUT CLOSED, the way a caller that has gone away leaves it:
    /// `>&-` closes fd 1, so the heredoc's `cat` gets EBADF. Only the exit code is observable, which
    /// is the whole point of the case.
    /// </summary>
    int Run_WithStdoutClosed(string role, string payload, string? threshold = null)
    {
        var startInfo = Start_Info(role, threshold);

        startInfo.ArgumentList.Add("-c");
        startInfo.ArgumentList.Add($"bash \"{Find_Hook_OrFail().Replace('\\', '/')}\" >&-");

        using var process = Start(startInfo, payload);

        Wait_OrFail(process);

        return process.ExitCode;
    }

    ProcessStartInfo Start_Info(string role, string? threshold)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = Bash_Locator.Find_OrFail(),
            RedirectStandardInput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };

        startInfo.Environment["TMPDIR"] = _temp;
        startInfo.Environment["AIORCH_ROLE"] = role;

        // No AIORCH_ID: the advisory needs no orchestration folder, and the log-undecidable helper
        // deliberately says nothing when there is no orchestration to tell.
        startInfo.Environment.Remove("AIORCH_ID");

        if (threshold != null)
            startInfo.Environment["AIORCH_SOFT_BOUNDARY_CALLS"] = threshold;
        else
            startInfo.Environment.Remove("AIORCH_SOFT_BOUNDARY_CALLS");

        return startInfo;
    }

    static Process Start(ProcessStartInfo startInfo, string payload)
    {
        var process = Process.Start(startInfo) ?? throw new Exception("could not start bash");

        // A BROKEN PIPE HERE IS THE HOOK BEING FAST, NOT THE HOOK BEING WRONG. The exempt-role and
        // threshold-off checks come BEFORE the script reads stdin — every hook in this kit is
        // arranged that way, so the cheapest path costs one `case` — which means bash can exit while
        // this write is still in flight. It is a race: the same four cases passed on a filtered run
        // and failed on the full suite, purely on timing. The payload not arriving is exactly what
        // those cases assert, so the write is allowed to fail and the verdict is still read.
        try
        {
            process.StandardInput.Write(payload);
            process.StandardInput.Close();
        }
        catch (IOException)
        {
        }

        return process;
    }

    static void Wait_OrFail(Process process)
    {
        if (!process.WaitForExit(60_000))
            throw new Exception("the hook did not exit within 60 s");
    }

    static string Find_Hook_OrFail()
    {
        return KitRepoFiles.Find(Path.Combine("kit", "hooks", "soft-boundary-check.sh"))
            ?? throw new Exception("kit/hooks/soft-boundary-check.sh was not found — REFUSING to pass about a hook this test never located.");
    }
}
