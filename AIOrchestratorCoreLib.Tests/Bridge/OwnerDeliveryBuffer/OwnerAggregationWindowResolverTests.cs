using System.Text.Json.Nodes;
using AIOrchestratorCoreLib.Bridge.BridgeEngineTiming;
using AIOrchestratorCoreLib.Bridge.OwnerDeliveryBuffer;
using AIOrchestratorCoreLib.Configuration.PhoneSettings;
using AIOrchestratorCoreLib.Configuration.SettingsCatalog;
using Xunit;

namespace AIOrchestratorCoreLib.Tests.Bridge.OwnerDeliveryBuffer;

/// <summary>
/// WHICH WINDOW IS IN FORCE (plan 03 task 13) — the owner's <c>phone.aggregationSeconds</c>, unless the
/// engine's timing names one. The second half is what keeps every engine test on
/// <c>BridgeTestTiming.Fast()</c> (1 s) and <c>SendNowSkipsTheWindowTests.LongWindow()</c> (60 s) exactly
/// as it was, unedited.
/// </summary>
public class OwnerAggregationWindowResolverTests
{
    static IPhoneSettings Classic_Phone()
    {
        return PhoneSettings_Json.Parse(configRoot: null, Presets_Loader.Resolve_ForConfig(configRoot: null).Tree);
    }

    [Fact]
    public void WithNoWindowInTheTiming_TheSettingIsTheWindow()
    {
        Assert.Equal((6, 6), OwnerAggregationWindow_Resolver.Resolve(customAggregationSecondsOrNull: null, Classic_Phone()));
    }

    /// <summary>
    /// A TEST'S WINDOW OUTRANKS THE SETTING, and the discount still comes from the setting — the buffer
    /// clamps it to the one-second window, which is what a finished message served there before.
    /// </summary>
    [Fact]
    public void AWindowInTheTiming_OutranksTheSetting_AndTheDiscountStillComesFromTheSetting()
    {
        Assert.Equal((1, 6), OwnerAggregationWindow_Resolver.Resolve(customAggregationSecondsOrNull: 1, Classic_Phone()));
    }

    /// <summary>
    /// THE SEAM'S TWO SHAPES: production timing carries no window (so the setting decides), the custom
    /// seam carries the one it is handed, and the settings-window custom seam carries none on purpose.
    /// </summary>
    [Fact]
    public void ProductionTimingCarriesNoWindow_AndTheCustomSeamCarriesItsOwn()
    {
        Assert.Null(BridgeEngineTiming_Factory.Create_Production().OwnerAggregationSeconds_OrNull);
        Assert.Equal(60, BridgeEngineTiming_Factory.Create_Custom(20, 60, 30, 150, 40).OwnerAggregationSeconds_OrNull);
        Assert.Null(BridgeEngineTiming_Factory.Create_Custom_WindowFromSettings(20, 30, 150, 40).OwnerAggregationSeconds_OrNull);
    }

    /// <summary>The config.json rung reaches the resolver — the value it hands the buffer is the file's.</summary>
    [Fact]
    public void AWindowInConfigJson_IsTheWindowTheResolverHandsOn()
    {
        var configRoot = (JsonObject)JsonNode.Parse("""{"phone":{"aggregationSeconds":12,"finishedMessageSeconds":4}}""")!;
        var phone = PhoneSettings_Json.Parse(configRoot, Presets_Loader.Resolve_ForConfig(configRoot).Tree);

        Assert.Equal((12, 4), OwnerAggregationWindow_Resolver.Resolve(customAggregationSecondsOrNull: null, phone));
    }
}
