using AIOrchestratorCoreLib.Configuration.PhoneSettings;

namespace AIOrchestratorCoreLib.Bridge.OwnerDeliveryBuffer;

/// <summary>
/// WHICH AGGREGATION WINDOW IS IN FORCE — the owner's setting, unless a test's timing names one.
///
/// <para>
/// The window became a per-user setting on 2026-09-23 (plan 03 task 13, owner: <i>"it should be a
/// buffer of 6 seconds, giving me the time to press wait if I need"</i>): <c>phone.aggregationSeconds</c>
/// and <c>phone.finishedMessageSeconds</c>, 6 / 6 under classic, the fork's 3 / 2 under quiet and as
/// shipped. The engine's flush asks this on EVERY take, over <c>_configProvider.Get_Current().Phone</c>,
/// so an edit to config.json is obeyed on the next flush with no restart.
/// </para>
/// <para>
/// A TEST-SUPPLIED WINDOW KEEPS WINNING. Dozens of engine tests hand the bridge a one-second window
/// (<c>BridgeTestTiming.Fast()</c>) or a sixty-second one through
/// <c>BridgeEngineTiming_Factory.Create_Custom</c>, and they assert what a window DOES rather than
/// its number; making them read the owner's 6 s would cost them wall clock and prove nothing more. So
/// <see cref="Bridge.BridgeEngineTiming.IBridgeEngineTiming.OwnerAggregationSeconds_OrNull"/> is null in
/// production and set by the custom seam, and a set value outranks the setting here — the ONE place
/// the two meet.
/// </para>
/// <para>
/// THE DISCOUNT ALWAYS COMES FROM THE SETTING, even under a custom window: the buffer clamps it to
/// whichever window is in force (<c>OwnerDeliveryBufferModel.Read_Window</c>), so under a one-second
/// test window a finished message serves one second, as it always has.
/// </para>
/// </summary>
public static class OwnerAggregationWindow_Resolver
{
    public static (int AggregationSeconds, int FinishedMessageSeconds) Resolve(int? customAggregationSecondsOrNull, IPhoneSettings phone)
    {
        return (customAggregationSecondsOrNull ?? phone.AggregationSeconds, phone.FinishedMessageSeconds);
    }
}
