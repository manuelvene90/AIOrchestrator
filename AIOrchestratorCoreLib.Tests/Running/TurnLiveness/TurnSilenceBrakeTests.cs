using AIOrchestratorCoreLib.Running;
using AIOrchestratorCoreLib.Running.TurnLiveness;
using Xunit;

namespace AIOrchestratorCoreLib.Tests.Running.TurnLiveness;

/// <summary>
/// THE DECISION ALONE, on a stopped clock and staged signals — every sign of life on its own must be
/// enough to spare the turn, and only their joint absence may kill it. The spec's own sentence is the
/// test: "no turn is killed while a call of its own or of a sub-agent landed within the threshold".
/// </summary>
public class TurnSilenceBrakeTests
{
    static readonly DateTime START = new(2026, 9, 11, 12, 0, 0, DateTimeKind.Utc);
    static readonly TimeSpan LIMIT = TimeSpan.FromMinutes(15);

    static ITurnSilenceBrake Brake(DateTime? transcriptLastWrite, int? descendants)
    {
        return TurnSilenceBrake_Factory.Create_WithReaders(LIMIT, TimeSpan.FromSeconds(15), () => transcriptLastWrite, _ => descendants);
    }

    [Fact]
    public void Decide_NothingAtAllPastTheLimit_KillsAndNamesTheLimitAndTheKey()
    {
        var line = Brake(null, 0).Decide_Kill_OrNull(START.AddMinutes(16), START, null, 4242);

        Assert.NotNull(line);
        Assert.Contains("no sign of life for 16.0 min", line);
        Assert.Contains("the limit is 15.0 min", line);
        Assert.Contains("'printRunner.memberSilenceMinutes'", line);
    }

    [Fact]
    public void Decide_NothingAtAllInsideTheLimit_SparesTheTurn()
    {
        Assert.Null(Brake(null, 0).Decide_Kill_OrNull(START.AddMinutes(14), START, null, 4242));
    }

    [Fact]
    public void Decide_RecentOutput_SparesTheTurn()
    {
        Assert.Null(Brake(null, 0).Decide_Kill_OrNull(START.AddMinutes(40), START, START.AddMinutes(30), 4242));
    }

    /// <summary>The parent blocked on a sub-agent writes nothing; the sub-agent's transcript does.</summary>
    [Fact]
    public void Decide_RecentTranscriptWrite_SparesTheTurn()
    {
        Assert.Null(Brake(START.AddMinutes(30), 0).Decide_Kill_OrNull(START.AddMinutes(40), START, null, 4242));
    }

    /// <summary>A build writes nothing anywhere until it returns — measured: a 29-minute Bash, 2026-09-10.</summary>
    [Fact]
    public void Decide_ACommandRunningBelowTheTurn_SparesTheTurn()
    {
        Assert.Null(Brake(null, 2).Decide_Kill_OrNull(START.AddMinutes(40), START, null, 4242));
    }

    /// <summary>"Cannot tell" is read as alive — the safe direction is never killing live work.</summary>
    [Fact]
    public void Decide_TheOsCannotSayWhatRunsBelow_SparesTheTurn()
    {
        Assert.Null(Brake(null, null).Decide_Kill_OrNull(START.AddMinutes(40), START, null, 4242));
    }

    /// <summary>
    /// A RESUMED TRANSCRIPT CARRIES EVERY EARLIER TURN'S WRITES. A stamp from before this turn started
    /// must not count as life — nor, the other way round, as silence that began before the turn did:
    /// the stream brake's first version measured from the previous turn and killed 17 idle
    /// supervisors five seconds into their next prompt (2026-09-09).
    /// </summary>
    [Fact]
    public void Decide_AStampFromAnEarlierTurn_CountsFromThisTurnsStartInstead()
    {
        var brake = Brake(START.AddHours(-8), 0);

        Assert.Null(brake.Decide_Kill_OrNull(START.AddMinutes(1), START, START.AddHours(-8), 4242));
        Assert.Contains("for 15.5 min", brake.Decide_Kill_OrNull(START.AddMinutes(15.5), START, START.AddHours(-8), 4242));
    }

    [Fact]
    public void Decide_SilenceMeasuredFromTheLatestSign_NotTheFirst()
    {
        var line = Brake(START.AddMinutes(10), 0).Decide_Kill_OrNull(START.AddMinutes(27), START, START.AddMinutes(5), 4242);

        Assert.Contains("for 17.0 min", line);
    }

    [Theory]
    [InlineData(SessionRoles.Implementer, true)]
    [InlineData(SessionRoles.Reviewer, true)]
    [InlineData(SessionRoles.Supervisor, false)]
    [InlineData(SessionRoles.General, false)]
    [InlineData(SessionRoles.Solo, false)]
    [InlineData(SessionRoles.Communicator, false)]
    public void Create_ForTurn_OnlyTheWorkingMembersAreBraked(SessionRoles role, bool braked)
    {
        var brake = TurnSilenceBrake_Factory.Create_ForTurn_OrNull(role, LIMIT, Guid.NewGuid().ToString(), new Dictionary<string, string>());

        Assert.Equal(braked, brake != null);
    }

    [Fact]
    public void Create_ForTurn_ALimitOfZero_IsOff()
    {
        Assert.Null(TurnSilenceBrake_Factory.Create_ForTurn_OrNull(SessionRoles.Implementer, TimeSpan.Zero, Guid.NewGuid().ToString(), new Dictionary<string, string>()));
    }

    const string LOOP = "the same Bash call returned the same result 4 times in a row";

    static ITurnSilenceBrake LoopingBrake(int? descendants, DateTime? subAgentWrite)
    {
        return TurnSilenceBrake_Factory.Create_WithReaders(LIMIT, TimeSpan.FromSeconds(15), () => START.AddMinutes(9), _ => descendants, _ => LOOP, () => subAgentWrite);
    }

    /// <summary>
    /// A LOOPING TURN SHOWS LIFE FOR EVER — output a second ago, its transcript growing with every
    /// repeat — and is still killed, because repetition is the other half of best practice (research
    /// 2026-09-11, OpenHands' stuck detector). And the loop is looked for only in THIS turn's steps.
    /// </summary>
    [Fact]
    public void Decide_ALoopingTurnThatWritesAndPrints_IsKilledAsALoop()
    {
        DateTime? askedSince = null;
        var brake = TurnSilenceBrake_Factory.Create_WithReaders(LIMIT, TimeSpan.FromSeconds(15), () => START.AddMinutes(9), _ => 0,
            since => { askedSince = since; return LOOP; });

        var line = brake.Decide_Kill_OrNull(START.AddMinutes(10), START, START.AddMinutes(10), 4242);

        Assert.NotNull(line);
        Assert.StartsWith("the turn was looping: the same Bash call", line);
        Assert.True(TurnSilenceBrake_Factory.Is_LoopKill(line!));
        Assert.Equal(START, askedSince);
    }

    [Fact]
    public void Is_LoopKill_TellsTheTwoBrakeKillsApart()
    {
        var silence = Brake(null, 0).Decide_Kill_OrNull(START.AddMinutes(16), START, null, 4242);

        Assert.False(TurnSilenceBrake_Factory.Is_LoopKill(silence!));
    }

    /// <summary>
    /// WAITING IS NOT LOOPING (review finding, 2026-09-11). A member polling its own background work
    /// gets the same answer again and again while that work moves — a build below it, a sub-agent
    /// writing. The repetition is spared while anything else moves, and "cannot tell" moves.
    /// </summary>
    [Fact]
    public void Decide_ARepeatedPollWhileABuildRunsBelow_IsSpared()
    {
        Assert.Null(LoopingBrake(1, null).Decide_Kill_OrNull(START.AddMinutes(10), START, START.AddMinutes(10), 4242));
    }

    [Fact]
    public void Decide_ARepeatedPollWhileASubAgentWrites_IsSpared()
    {
        Assert.Null(LoopingBrake(0, START.AddMinutes(8)).Decide_Kill_OrNull(START.AddMinutes(10), START, START.AddMinutes(10), 4242));
    }

    [Fact]
    public void Decide_ARepeatedPollWhenTheOsCannotSayWhatRuns_IsSpared()
    {
        Assert.Null(LoopingBrake(null, null).Decide_Kill_OrNull(START.AddMinutes(10), START, START.AddMinutes(10), 4242));
    }

    [Fact]
    public void Decide_ARepeatAfterTheSubAgentWentQuiet_IsKilled()
    {
        Assert.NotNull(LoopingBrake(0, START.AddMinutes(-20)).Decide_Kill_OrNull(START.AddMinutes(10), START, START.AddMinutes(10), 4242));
    }
}
