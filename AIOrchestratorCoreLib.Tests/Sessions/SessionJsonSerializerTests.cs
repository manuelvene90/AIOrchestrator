using AIOrchestratorCoreLib.Sessions;
using AIOrchestratorCoreLib.Sessions.OrchestrationSession;
using Xunit;

namespace AIOrchestratorCoreLib.Tests.Sessions;

/// <summary>
/// The effort override travels through session.json exactly as the model override does — it is a
/// per-orchestration setting the owner chose, and one that vanished on restart would put the
/// next respawn back on the CLI's default without anyone deciding that.
/// </summary>
public class SessionJsonSerializerTests
{
    const string SOURCE = "test-session.json";

    static IOrchestrationSession Build_FreshSession()
    {
        return OrchestrationSession_Factory.Create("arb-fix", "Arb Studio", @"C:\repos\arb", new DateTime(2026, 9, 9, 10, 0, 0, DateTimeKind.Utc), null, null, []);
    }

    [Fact]
    public void Serialize_ThenDeserialize_RoundTripsBothEffortOverrides()
    {
        var withSupervisorEffort = OrchestrationSession_Factory.CreateFrom_Existing_WithSupervisorEffortOverride(Build_FreshSession(), "xhigh");
        var withBoth = OrchestrationSession_Factory.CreateFrom_Existing_WithImplementerEffortOverride(withSupervisorEffort, "low");

        var json = SessionJson_Serializer.Serialize(withBoth);
        var reloaded = SessionJson_Serializer.Deserialize(json, SOURCE);

        Assert.Equal("xhigh", reloaded.SupervisorEffortOverride);
        Assert.Equal("low", reloaded.ImplementerEffortOverride);
    }

    [Fact]
    public void Serialize_ThenDeserialize_NullEffortOverrides_StayNull()
    {
        var json = SessionJson_Serializer.Serialize(Build_FreshSession());

        // The keys are WRITTEN even when null — asserted so the old-file test below can never
        // silently become a test of a key that was never there in the first place.
        Assert.Contains("\"supervisorEffortOverride\"", json, StringComparison.Ordinal);
        Assert.Contains("\"implementerEffortOverride\"", json, StringComparison.Ordinal);

        var reloaded = SessionJson_Serializer.Deserialize(json, SOURCE);

        Assert.Null(reloaded.SupervisorEffortOverride);
        Assert.Null(reloaded.ImplementerEffortOverride);
    }

    /// <summary>
    /// Every session.json written before this field lacks the keys, and absence must read as
    /// "no override" — the CLI's own default — never as an error and never as some level.
    /// </summary>
    [Fact]
    public void Deserialize_SessionWrittenBeforeEffortExisted_ReadsBothAsNull()
    {
        var json = SessionJson_Serializer.Serialize(Build_FreshSession());

        var withoutEffortKeys = string.Join(
            Environment.NewLine,
            json.Split(Environment.NewLine).Where(line =>
                !line.Contains("\"supervisorEffortOverride\"", StringComparison.Ordinal) &&
                !line.Contains("\"implementerEffortOverride\"", StringComparison.Ordinal)));

        // The removal itself is asserted, so this can never pass on a key that is still there.
        Assert.DoesNotContain("EffortOverride", withoutEffortKeys, StringComparison.Ordinal);

        var reloaded = SessionJson_Serializer.Deserialize(withoutEffortKeys, SOURCE);

        Assert.Null(reloaded.SupervisorEffortOverride);
        Assert.Null(reloaded.ImplementerEffortOverride);
        Assert.Equal("arb-fix", reloaded.OrchId);
    }
}
