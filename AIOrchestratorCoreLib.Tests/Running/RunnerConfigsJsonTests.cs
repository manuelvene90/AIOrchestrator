using System.Text.Json.Nodes;
using AIOrchestratorCoreLib.Configuration;
using AIOrchestratorCoreLib.Configuration.OrchestratorConfig;
using AIOrchestratorCoreLib.Configuration.RepoEntry;
using AIOrchestratorCoreLib.Running;
using AIOrchestratorCoreLib.Running.RoleRunnerConfig;
using AIOrchestratorCoreLib.Running.RunnerConfigs;
using AIOrchestratorCoreLib.SupervisionPaths;
using AIOrchestratorCoreLib.Status;
using Xunit;

namespace AIOrchestratorCoreLib.Tests.Running;

/// <summary>
/// The config.json shape of the runner configuration, both ways. The one property that matters
/// most is the first test: an existing config.json, with no runners block at all, means terminal
/// everywhere — nothing changes for anyone who did not ask.
/// </summary>
public class RunnerConfigsJsonTests : IDisposable
{
    readonly string _tempRoot;
    readonly ISupervisionPaths _paths;

    public RunnerConfigsJsonTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), $"aiorch-runner-config-tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempRoot);
        _paths = SupervisionPaths_Factory.Create(_tempRoot);
    }

    public void Dispose()
    {
        Directory.Delete(_tempRoot, recursive: true);
    }

    [Fact]
    public void AbsentBlock_MeansTerminalEverywhere_AndDefaultLimits()
    {
        var configs = RunnerConfigs_Json.Parse(JsonNode.Parse("""{"repos":[]}""") as JsonObject);

        foreach (var role in SessionRole_Names.ALL)
            Assert.Equal(SessionRunners.Terminal, configs.Get_ForRole(role).Runner);

        Assert.Equal(ResumeModes.Fresh, configs.Get_ForRole(SessionRoles.General).Resume);
        Assert.Equal(ResumeModes.Transcript, configs.Get_ForRole(SessionRoles.Implementer).Resume);
        Assert.Null(configs.Get_ForRole(SessionRoles.Implementer).PermissionMode);
        Assert.Equal(RunnerConfigs_Factory.DEFAULT_MAX_CONCURRENT_TURNS, configs.MaxConcurrentTurns);
        Assert.Equal(RunnerConfigs_Factory.DEFAULT_MAX_CONCURRENT_TURNS_PER_ORCHESTRATION, configs.MaxConcurrentTurnsPerOrchestration);
        Assert.Equal(RunnerConfigs_Factory.DEFAULT_TURN_TIMEOUT, configs.TurnTimeout);
        Assert.Equal(RunnerConfigs_Factory.DEFAULT_COALESCE_WINDOW, configs.CoalesceWindow);
        Assert.Equal(RunnerConfigs_Factory.DEFAULT_MEMBER_DIGEST_WINDOW, configs.MemberDigestWindow);
    }

    /// <summary>
    /// THE DIGEST WINDOW THE VPS WILL RUN ON, pinned at the surface the owner edits (spec C4). Five
    /// minutes is the spec's proposal; this is the place that says what production gets when nobody
    /// writes the key, and since 2026-09-10 the behavioural harness defaults to the same number, so
    /// the suite drives what ships rather than a window switched off.
    /// </summary>
    [Fact]
    public void TheMemberDigestWindow_IsFiveMinutesUnlessTheOwnerSaysOtherwise()
    {
        Assert.Equal(TimeSpan.FromMinutes(5), RunnerConfigs_Factory.DEFAULT_MEMBER_DIGEST_WINDOW);

        var configs = RunnerConfigs_Json.Parse(JsonNode.Parse("""
            {"printRunner": { "memberDigestMinutes": 2.5 }}
            """) as JsonObject);

        Assert.Equal(TimeSpan.FromMinutes(2.5), configs.MemberDigestWindow);
    }

    /// <summary>
    /// AND ZERO MEANS OFF, not "unreadable, have the default back". It is the owner's way back to one
    /// entry one turn — the behaviour before 2026-09-09 — without a deployment, which is why the key
    /// is read with the NON-NEGATIVE reader rather than the positive one that governs a timeout.
    /// </summary>
    [Fact]
    public void AZeroMemberDigest_TurnsTheDigestOffRatherThanRestoringTheDefault()
    {
        var configs = RunnerConfigs_Json.Parse(JsonNode.Parse("""
            {"printRunner": { "memberDigestMinutes": 0 }}
            """) as JsonObject);

        Assert.Equal(TimeSpan.Zero, configs.MemberDigestWindow);
    }

    /// <summary>
    /// AND BELOW ZERO MEANS OFF TOO — the review's LOW of 2026-09-09. A negative value used to restore
    /// the five-minute default, which contradicted the factory's own docstring three lines away and
    /// gave an owner who typed a minus sign the OPPOSITE of what they asked for: the longest wait
    /// instead of none. "Off" and "off" are the same answer; the only place a minus could honestly
    /// lead is nowhere else.
    /// </summary>
    [Fact]
    public void ANegativeMemberDigest_MeansOffAndNotTheDefaultBack()
    {
        var configs = RunnerConfigs_Json.Parse(JsonNode.Parse("""
            {"printRunner": { "memberDigestMinutes": -1 }}
            """) as JsonObject);

        Assert.Equal(TimeSpan.Zero, configs.MemberDigestWindow);
    }

    /// <summary>
    /// A DIGEST LONGER THAN THE CEILING IS REFUSED WITH A LINE, NOT APPLIED — the review's MEDIUM of
    /// 2026-09-09, and the one coupling in this change that bites another component.
    ///
    /// <para>
    /// <c>BridgeEngineModel</c> tells a supervisor it owes a member a verdict once that member's
    /// channel has been quiet for <see cref="Nudge_Windows.IMPLEMENTER_NUDGE_MINUTES"/>, and that clock
    /// runs from the member's REPORT — so a digest of D minutes leaves that window minus D for the turn to be released, run and
    /// answer. Probed by the review at D = 10: at minute 9 the app called the supervisor 9.6 min late
    /// on a verdict for a report IT WAS HOLDING, and spent that quiet spell's single nudge token on
    /// the false alarm, so a genuinely stalled supervisor got nothing.
    /// </para>
    /// <para>
    /// REFUSED THE WAY <c>sessionMemoryMax</c> IS: a rejection line the operator reads and the default
    /// applied, rather than a throw that stops the app starting over a hand-typed number. Turning the
    /// digest DOWN is always allowed — that is the direction of today's behaviour.
    /// </para>
    /// </summary>
    /// <summary>
    /// THE COUPLING ITSELF, PINNED — the half that was prose until 2026-09-10.
    ///
    /// <para>
    /// The ceiling exists only because of the nudge window, and while that window was a private
    /// <c>const int</c> in <c>BridgeEngineModel</c> the relationship could only be RESTATED in two
    /// docstrings. Now it is asserted: raise the nudge and this stays green, lower it to at or below the
    /// digest ceiling and this goes red — which is the whole point, because at that moment the app
    /// would start nudging a supervisor for a report it is itself holding at the DEFAULT setting, with
    /// nothing in <c>config.json</c> to refuse.
    /// </para>
    /// <para>
    /// It asserts the INEQUALITY and not a formula: the three minutes of headroom are a judgement about
    /// how long a released turn needs to run and answer, not an arithmetic identity, so a test that
    /// recomputed the ceiling would only restate the code (`.claude/rules/code-conventions.md`).
    /// </para>
    /// </summary>
    [Fact]
    public void TheDigestCeiling_LeavesTimeForTheTurnItDelays_BeforeTheAppNudgesForIt()
    {
        Assert.True(
            RunnerConfigs_Factory.MAX_MEMBER_DIGEST_WINDOW.TotalMinutes < Nudge_Windows.IMPLEMENTER_NUDGE_MINUTES,
            $"a digest ceiling of {RunnerConfigs_Factory.MAX_MEMBER_DIGEST_WINDOW.TotalMinutes:0.#} min against a nudge window of {Nudge_Windows.IMPLEMENTER_NUDGE_MINUTES} min leaves a held report to be nudged for while the app is holding it");

        Assert.True(
            RunnerConfigs_Factory.DEFAULT_MEMBER_DIGEST_WINDOW <= RunnerConfigs_Factory.MAX_MEMBER_DIGEST_WINDOW,
            "the shipped default must be inside the ceiling the config reader enforces");
    }

    [Fact]
    public void ADigestAboveTheCeiling_IsRefusedWithALine_AndTheDefaultIsApplied()
    {
        var configs = RunnerConfigs_Json.Parse(JsonNode.Parse("""
            {"printRunner": { "memberDigestMinutes": 10 }}
            """) as JsonObject);

        Assert.Equal(RunnerConfigs_Factory.DEFAULT_MEMBER_DIGEST_WINDOW, configs.MemberDigestWindow);

        var rejection = Assert.Single(configs.Rejections);

        Assert.Contains(RunnerConfigs_Json.MEMBER_DIGEST_MINUTES_KEY, rejection);
        Assert.Contains("nudge", rejection);
    }

    /// <summary>
    /// AN ABSURD NUMBER COSTS THAT SETTING ITS DEFAULT AND NOTHING ELSE — the second review's HIGH of
    /// 2026-09-10, and this file's own contract ("NOT A THROW, on either end").
    ///
    /// <para>
    /// The ceiling was applied by building a <c>TimeSpan</c> first, OUTSIDE the try that guards the
    /// read: <c>TimeSpan.FromMinutes(1e11)</c> raises <c>OverflowException</c>, which escaped
    /// <c>RunnerConfigs_Json.Parse</c>, then <c>OrchestratorConfig_Loader.Load_OrEmpty</c> and
    /// <c>IOrchestratorConfigProvider.Get_Current</c> — neither of which catches. <c>Get_Current</c>
    /// runs on the startup path and on every mirror tick, so one hand-typed number took down the
    /// whole config read and logged one error per tick for ever, with nothing after the read running.
    /// The comparison is now made on the NUMBER, so the only thing an absurd value can cost is
    /// itself.
    /// </para>
    /// <para>
    /// <c>1e309</c> is the other end of the same door: JSON has no infinity, so it is read as
    /// <c>double.PositiveInfinity</c> and reached the same constructor.
    /// </para>
    /// </summary>
    [Theory]
    [InlineData("100000000000")]
    [InlineData("1e11")]
    [InlineData("1e309")]
    public void ADigestNumberTooLargeForATimeSpan_IsRefused_AndTakesNothingElseDown(string written)
    {
        var configs = RunnerConfigs_Json.Parse(
            JsonNode.Parse($"{{\"printRunner\": {{ \"memberDigestMinutes\": {written}, \"maxConcurrentTurns\": 7 }}}}") as JsonObject);

        Assert.Equal(RunnerConfigs_Factory.DEFAULT_MEMBER_DIGEST_WINDOW, configs.MemberDigestWindow);
        Assert.Contains(RunnerConfigs_Json.MEMBER_DIGEST_MINUTES_KEY, Assert.Single(configs.Rejections));

        // The reading did not stop at the bad key: everything after it is still read.
        Assert.Equal(7, configs.MaxConcurrentTurns);
    }

    /// <summary>
    /// AND THE CEILING IS INCLUSIVE, so the value the app itself defaults to is not something the owner
    /// is refused for writing down.
    /// </summary>
    [Fact]
    public void ADigestAtTheCeiling_IsAccepted()
    {
        var configs = RunnerConfigs_Json.Parse(JsonNode.Parse("""
            {"printRunner": { "memberDigestMinutes": 5 }}
            """) as JsonObject);

        Assert.Equal(RunnerConfigs_Factory.MAX_MEMBER_DIGEST_WINDOW, configs.MemberDigestWindow);
        Assert.Empty(configs.Rejections);
    }

    [Fact]
    public void NoConfigAtAll_IsTheSameDefault()
    {
        Assert.Equal(SessionRunners.Terminal, RunnerConfigs_Json.Parse(null).Get_ForRole(SessionRoles.Solo).Runner);
        Assert.Equal(SessionRunners.Terminal, OrchestratorConfig_Loader.Load_OrEmpty(_paths).Runners.Get_ForRole(SessionRoles.Implementer).Runner);
    }

    [Fact]
    public void PerRoleBlocks_AndLimits_AreRead()
    {
        var root = JsonNode.Parse("""
            {
              "runners": {
                "implementer": { "runner": "print", "resume": "transcript", "permission_mode": "acceptEdits" },
                "general": { "runner": "print", "resume": "fresh" },
                "supervisor": { "runner": "terminal" }
              },
              "printRunner": { "maxConcurrentTurns": 4, "maxConcurrentTurnsPerOrchestration": 2, "turnTimeoutMinutes": 0.5, "coalesceSeconds": 1, "memberDigestMinutes": 4 }
            }
            """) as JsonObject;

        var configs = RunnerConfigs_Json.Parse(root);

        Assert.Equal(SessionRunners.Print, configs.Get_ForRole(SessionRoles.Implementer).Runner);
        Assert.Equal("acceptEdits", configs.Get_ForRole(SessionRoles.Implementer).PermissionMode);
        Assert.Equal(SessionRunners.Print, configs.Get_ForRole(SessionRoles.General).Runner);
        Assert.Equal(ResumeModes.Fresh, configs.Get_ForRole(SessionRoles.General).Resume);
        Assert.Equal(SessionRunners.Terminal, configs.Get_ForRole(SessionRoles.Supervisor).Runner);
        Assert.Equal(SessionRunners.Terminal, configs.Get_ForRole(SessionRoles.Reviewer).Runner);
        Assert.Equal(4, configs.MaxConcurrentTurns);
        Assert.Equal(2, configs.MaxConcurrentTurnsPerOrchestration);
        Assert.Equal(TimeSpan.FromSeconds(30), configs.TurnTimeout);
        Assert.Equal(TimeSpan.FromSeconds(1), configs.CoalesceWindow);
        // FOUR AND NOT SEVEN, which is what this said until 2026-09-10: seven is now above the ceiling
        // (RunnerConfigs_Factory.MAX_MEMBER_DIGEST_WINDOW) and would be refused, so the case would have
        // stopped testing "the key is read" and started testing the refusal a case of its own owns.
        Assert.Equal(TimeSpan.FromMinutes(4), configs.MemberDigestWindow);
    }

    [Fact]
    public void UnknownWords_AndBadLimits_FallBackToDefaults_RatherThanThrow()
    {
        var root = JsonNode.Parse("""
            {
              "runners": { "implementer": { "runner": "daemon", "resume": "sometimes" }, "solo": "print" },
              "printRunner": { "maxConcurrentTurns": 0, "turnTimeoutMinutes": -3, "coalesceSeconds": "soon", "memberDigestMinutes": "later" }
            }
            """) as JsonObject;

        var configs = RunnerConfigs_Json.Parse(root);

        Assert.Equal(SessionRunners.Terminal, configs.Get_ForRole(SessionRoles.Implementer).Runner);
        Assert.Equal(ResumeModes.Transcript, configs.Get_ForRole(SessionRoles.Implementer).Resume);
        Assert.Equal(SessionRunners.Terminal, configs.Get_ForRole(SessionRoles.Solo).Runner);
        Assert.Equal(RunnerConfigs_Factory.DEFAULT_MAX_CONCURRENT_TURNS, configs.MaxConcurrentTurns);
        Assert.Equal(RunnerConfigs_Factory.DEFAULT_TURN_TIMEOUT, configs.TurnTimeout);
        Assert.Equal(RunnerConfigs_Factory.DEFAULT_COALESCE_WINDOW, configs.CoalesceWindow);
        Assert.Equal(RunnerConfigs_Factory.DEFAULT_MEMBER_DIGEST_WINDOW, configs.MemberDigestWindow);
    }

    [Fact]
    public void Save_ThenLoad_RoundTripsEveryRoleAndLimit()
    {
        var runners = RunnerConfigs_Factory.Create_WithLimits(
            RunnerConfigs_Factory.Create_WithRole(
                RunnerConfigs_Factory.Create_Default(), SessionRoles.Reviewer, RoleRunnerConfig_Factory.Create(SessionRunners.Print, ResumeModes.Transcript, "plan")),
            7, 2, TimeSpan.FromMinutes(12), TimeSpan.FromSeconds(5), TimeSpan.FromMinutes(3));

        var config = OrchestratorConfig_Factory.Create(
            [RepoEntry_Factory.Create("Repo", "/tmp/repo")], "opus", "opus", null, null, "sonnet", "sonnet", null, null, null, null, null, null, runners);

        OrchestratorConfig_Loader.Save(config, _paths);
        var reloaded = OrchestratorConfig_Loader.Load_OrEmpty(_paths).Runners;

        Assert.Equal(SessionRunners.Print, reloaded.Get_ForRole(SessionRoles.Reviewer).Runner);
        Assert.Equal("plan", reloaded.Get_ForRole(SessionRoles.Reviewer).PermissionMode);
        Assert.Equal(SessionRunners.Terminal, reloaded.Get_ForRole(SessionRoles.Implementer).Runner);
        Assert.Equal(ResumeModes.Fresh, reloaded.Get_ForRole(SessionRoles.General).Resume);
        Assert.Equal(7, reloaded.MaxConcurrentTurns);
        Assert.Equal(2, reloaded.MaxConcurrentTurnsPerOrchestration);
        Assert.Equal(TimeSpan.FromMinutes(12), reloaded.TurnTimeout);
        Assert.Equal(TimeSpan.FromSeconds(5), reloaded.CoalesceWindow);
        Assert.Equal(TimeSpan.FromMinutes(3), reloaded.MemberDigestWindow);

        // Written explicitly: every role appears, so the owner sees the whole surface.
        var written = JsonNode.Parse(File.ReadAllText(_paths.ConfigFile)) as JsonObject;
        Assert.NotNull(written!["runners"]!["solo"]);
        Assert.Equal("terminal", written["runners"]!["solo"]!["runner"]!.GetValue<string>());
        Assert.Equal(12, written["printRunner"]![RunnerConfigs_Json.TURN_TIMEOUT_MINUTES_KEY]!.GetValue<double>());
    }

    /// <summary>
    /// The /screenshots toggle rebuilds the config from an existing one; the Settings window passes
    /// the runners through explicitly. Neither may reset a hand-edited block. (There were two such
    /// toggles until 2026-09-09, when /italian and the layer it drove were abolished.)
    /// </summary>
    [Fact]
    public void ToggleCopies_KeepTheRunners()
    {
        var runners = RunnerConfigs_Factory.Create_WithRole(RunnerConfigs_Factory.Create_Default(), SessionRoles.Implementer, RoleRunnerConfig_Factory.Create(SessionRunners.Print, ResumeModes.Transcript, null));
        var source = OrchestratorConfig_Factory.Create([], null, null, null, null, null, null, null, null, null, null, null, null, runners);

        Assert.Same(runners, OrchestratorConfig_Factory.Create_WithStatusScreenshots(source, true).Runners);
    }

    [Fact]
    public void TheOverloadWithoutRunners_DefaultsToTerminal()
    {
        var config = OrchestratorConfig_Factory.Create([], null, null, null, null, null, null, null, null, null, null, null, null);

        Assert.Equal(SessionRunners.Terminal, config.Runners.Get_ForRole(SessionRoles.Implementer).Runner);
    }

    /// <summary>
    /// THE MEMBER SILENCE BRAKE'S KEY, read the way the digest window is: absent is the default,
    /// zero and below are OFF (a minus sign never gives the brake back), an absurd number is refused
    /// with a line rather than thrown — <c>TimeSpan.FromMinutes(1e11)</c> overflows, and a throw here
    /// takes the whole config read down on every tick — and a save writes it back.
    /// </summary>
    [Theory]
    [InlineData("""{"printRunner":{}}""", 15.0, false)]
    [InlineData("""{"printRunner":{"memberSilenceMinutes":20}}""", 20.0, false)]
    [InlineData("""{"printRunner":{"memberSilenceMinutes":0}}""", 0.0, false)]
    [InlineData("""{"printRunner":{"memberSilenceMinutes":-3}}""", 0.0, false)]
    [InlineData("""{"printRunner":{"memberSilenceMinutes":1e11}}""", 15.0, true)]
    [InlineData("""{"printRunner":{"memberSilenceMinutes":"soon"}}""", 15.0, false)]
    public void TheMemberSilenceLimit_IsReadWithBothEndsDecided(string json, double expectedMinutes, bool refused)
    {
        var configs = RunnerConfigs_Json.Parse(JsonNode.Parse(json) as JsonObject);

        Assert.Equal(TimeSpan.FromMinutes(expectedMinutes), configs.MemberSilenceLimit);
        Assert.Equal(refused, configs.Rejections.Any(line => line.Contains(RunnerConfigs_Json.MEMBER_SILENCE_MINUTES_KEY)));
    }

    [Fact]
    public void TheMemberSilenceLimit_SurvivesASave()
    {
        var configs = RunnerConfigs_Json.Parse(JsonNode.Parse("""{"printRunner":{"memberSilenceMinutes":22}}""") as JsonObject);
        var root = new JsonObject();

        RunnerConfigs_Json.Write(root, configs);

        Assert.Equal(TimeSpan.FromMinutes(22), RunnerConfigs_Json.Parse(root).MemberSilenceLimit);
    }

    [Fact]
    public void TheMemberSilenceLimit_SurvivesTheCopyFactories()
    {
        var configs = RunnerConfigs_Json.Parse(JsonNode.Parse("""{"printRunner":{"memberSilenceMinutes":22}}""") as JsonObject);

        Assert.Equal(TimeSpan.FromMinutes(22), RunnerConfigs_Factory.Create_WithRole(configs, SessionRoles.Implementer, configs.Get_ForRole(SessionRoles.Implementer)).MemberSilenceLimit);
        Assert.Equal(TimeSpan.FromMinutes(22), RunnerConfigs_Factory.Create_WithLimits(configs, 4, 2, TimeSpan.FromMinutes(30), TimeSpan.Zero).MemberSilenceLimit);
    }
}
