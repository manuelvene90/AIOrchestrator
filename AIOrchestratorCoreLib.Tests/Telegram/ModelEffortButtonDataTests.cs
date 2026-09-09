using AIOrchestratorCoreLib.Telegram;
using Xunit;

namespace AIOrchestratorCoreLib.Tests.Telegram;

/// <summary>
/// The callback payload behind a /model or /effort button. STATELESS on purpose: it names the
/// orchestration, the role and the value, so a tap survives an app restart and never goes through
/// the single-use option registry — where a tap becomes a synthetic OWNER MESSAGE routed to an
/// agent, which is the one thing a model change must not be.
/// </summary>
public class ModelEffortButtonDataTests
{
    [Fact]
    public void AModelChoice_RoundTrips()
    {
        var data = ModelEffortButton_Data.Build(ModelEffortKinds.Model, "option-lab-2", ModelEffortButton_Data.SUPERVISOR_ROLE, "fable");

        Assert.Equal("model:option-lab-2:sup:fable", data);
        Assert.Equal((ModelEffortKinds.Model, "option-lab-2", "sup", "fable"), ModelEffortButton_Data.Parse_OrNull(data));
    }

    [Fact]
    public void AnEffortChoice_RoundTrips()
    {
        var data = ModelEffortButton_Data.Build(ModelEffortKinds.Effort, "ai-orchestrator-21", ModelEffortButton_Data.IMPLEMENTER_ROLE, "xhigh");

        Assert.Equal("effort:ai-orchestrator-21:imp:xhigh", data);
        Assert.Equal((ModelEffortKinds.Effort, "ai-orchestrator-21", "imp", "xhigh"), ModelEffortButton_Data.Parse_OrNull(data));
    }

    /// <summary>A bracketed alias carries no colon, so it rides inside the payload untouched.</summary>
    [Fact]
    public void ABracketedAlias_RoundTrips()
    {
        var data = ModelEffortButton_Data.Build(ModelEffortKinds.Model, "crm-3", "imp", "opus[1m]");

        Assert.Equal((ModelEffortKinds.Model, "crm-3", "imp", "opus[1m]"), ModelEffortButton_Data.Parse_OrNull(data));
    }

    /// <summary>
    /// ANYTHING ELSE IS NOT OURS — the other families (cmd:, hold:, go:, close-yes-, opt-) must fall
    /// through to their own handlers untouched.
    /// </summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("opt-17")]
    [InlineData("cmd:show:4242")]
    [InlineData("hold:4242")]
    [InlineData("close-yes-abc")]
    [InlineData("model:")]
    [InlineData("model:crm-3")]
    [InlineData("model:crm-3:sup")]
    [InlineData("model:crm-3:boss:fable")]
    [InlineData("model:crm-3:sup:")]
    [InlineData("MODEL:crm-3:sup:fable")]
    [InlineData("effort:crm-3:imp:xhigh:extra")]
    public void AnythingElse_IsNotOurs(string? callbackData)
    {
        Assert.Null(ModelEffortButton_Data.Parse_OrNull(callbackData));
    }

    [Theory]
    [InlineData("boss")]
    [InlineData("")]
    [InlineData("SUP")]
    public void AnUnknownRole_IsRefusedAtBuildTime(string role)
    {
        var exception = Assert.Throws<ArgumentException>(() => ModelEffortButton_Data.Build(ModelEffortKinds.Model, "crm-3", role, "fable"));

        Assert.Contains(role, exception.Message);
    }

    /// <summary>A colon inside a field would shift every field after it on the way back.</summary>
    [Fact]
    public void AValueCarryingTheSeparator_IsRefusedAtBuildTime()
    {
        var exception = Assert.Throws<ArgumentException>(() => ModelEffortButton_Data.Build(ModelEffortKinds.Model, "crm-3", "sup", "fa:ble"));

        Assert.Contains("fa:ble", exception.Message);
    }

    /// <summary>Telegram's hard cap. A payload over it is rejected at send time, on the phone.</summary>
    [Fact]
    public void TheLongestPlausibleOrchestrationId_StillFitsSixtyFourBytes()
    {
        var data = ModelEffortButton_Data.Build(ModelEffortKinds.Effort, "option-database-preprocessor-123", "imp", "medium");

        Assert.True(System.Text.Encoding.UTF8.GetByteCount(data) <= 64, $"callback data too long: '{data}'");
    }

    [Fact]
    public void APayloadOverSixtyFourBytes_IsRefusedAtBuildTime_NotOnThePhone()
    {
        var absurdId = new string('x', 70);

        var exception = Assert.Throws<ArgumentException>(() => ModelEffortButton_Data.Build(ModelEffortKinds.Model, absurdId, "sup", "fable"));

        Assert.Contains("64", exception.Message);
    }
}
