using System.Text.Json.Nodes;
using AIOrchestratorCoreLib.Running;
using AIOrchestratorCoreLib.Running.RunnerConfigs;
using Xunit;

namespace AIOrchestratorCoreLib.Tests.Running;

/// <summary>
/// TWO CEILINGS, ONE READER. A braked member gets the long one; everyone else — and a member whose
/// brake is off — keeps the short one, because the long ceiling is only safe while something else
/// catches a hang sooner. And whatever waits for turns at shutdown waits for the LONGEST.
/// </summary>
public class TurnTimeoutRuleTests
{
    static IRunnerConfigs Configs(string printRunner)
    {
        return RunnerConfigs_Json.Parse(JsonNode.Parse($$"""{"printRunner":{{printRunner}}}""") as JsonObject);
    }

    [Theory]
    [InlineData(SessionRoles.Implementer, 120)]
    [InlineData(SessionRoles.Reviewer, 120)]
    [InlineData(SessionRoles.Solo, 30)]
    [InlineData(SessionRoles.General, 30)]
    [InlineData(SessionRoles.Supervisor, 30)]
    [InlineData(SessionRoles.Communicator, 30)]
    public void Resolve_ForRole_OnlyBrakedMembersGetTheLongCeiling(SessionRoles role, double expectedMinutes)
    {
        Assert.Equal(TimeSpan.FromMinutes(expectedMinutes), TurnTimeout_Rule.Resolve_ForRole(role, Configs("{}")));
    }

    [Fact]
    public void Resolve_ForRole_AMemberWithTheBrakeOff_KeepsTheShortCeiling()
    {
        var configs = Configs("""{"memberSilenceMinutes":0}""");

        Assert.Equal(TimeSpan.FromMinutes(30), TurnTimeout_Rule.Resolve_ForRole(SessionRoles.Implementer, configs));
        Assert.Equal(TimeSpan.FromMinutes(30), TurnTimeout_Rule.Resolve_Longest(configs));
    }

    [Fact]
    public void Resolve_Longest_IsTheMemberCeilingWhileTheBrakeIsOn_AndNeverShorterThanTheTurnTimeout()
    {
        Assert.Equal(TimeSpan.FromMinutes(120), TurnTimeout_Rule.Resolve_Longest(Configs("{}")));
        Assert.Equal(TimeSpan.FromMinutes(45), TurnTimeout_Rule.Resolve_Longest(Configs("""{"turnTimeoutMinutes":45,"memberTurnTimeoutMinutes":20}""")));
    }

    [Theory]
    [InlineData("""{}""", 120.0, false)]
    [InlineData("""{"memberTurnTimeoutMinutes":90}""", 90.0, false)]
    [InlineData("""{"memberTurnTimeoutMinutes":0}""", 120.0, true)]
    [InlineData("""{"memberTurnTimeoutMinutes":-5}""", 120.0, true)]
    [InlineData("""{"memberTurnTimeoutMinutes":1e11}""", 120.0, true)]
    public void TheMemberCeiling_HasNoOff_AndRefusesWhatCannotBeACeiling(string printRunner, double expectedMinutes, bool refused)
    {
        var configs = Configs(printRunner);

        Assert.Equal(TimeSpan.FromMinutes(expectedMinutes), configs.MemberTurnTimeout);
        Assert.Equal(refused, configs.Rejections.Any(line => line.Contains(RunnerConfigs_Json.MEMBER_TURN_TIMEOUT_MINUTES_KEY)));
    }

    [Fact]
    public void TheMemberCeiling_SurvivesASaveAndTheCopyFactories()
    {
        var configs = Configs("""{"memberTurnTimeoutMinutes":90}""");
        var root = new JsonObject();
        RunnerConfigs_Json.Write(root, configs);

        Assert.Equal(TimeSpan.FromMinutes(90), RunnerConfigs_Json.Parse(root).MemberTurnTimeout);
        Assert.Equal(TimeSpan.FromMinutes(90), RunnerConfigs_Factory.Create_WithRole(configs, SessionRoles.Implementer, configs.Get_ForRole(SessionRoles.Implementer)).MemberTurnTimeout);
        Assert.Equal(TimeSpan.FromMinutes(90), RunnerConfigs_Factory.Create_WithLimits(configs, 4, 2, TimeSpan.FromMinutes(30), TimeSpan.Zero).MemberTurnTimeout);
    }

    /// <summary>
    /// WINDOWS KEEPS THE SHORT CEILING: the CLI runs under cmd.exe there, so a process always runs below
    /// the turn and the silence brake can never fire — a two-hour ceiling would be two hours of a hung slot.
    /// </summary>
    [Fact]
    public void Resolve_ForRole_OnWindows_AMemberKeepsTheShortCeiling()
    {
        Assert.Equal(TimeSpan.FromMinutes(30), TurnTimeout_Rule.Resolve_ForRole(SessionRoles.Implementer, Configs("{}"), isWindows: true));
        Assert.Equal(TimeSpan.FromMinutes(120), TurnTimeout_Rule.Resolve_ForRole(SessionRoles.Implementer, Configs("{}"), isWindows: false));
    }
}
