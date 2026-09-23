using AIOrchestratorCoreLib.Configuration.PulseSettings;
using AIOrchestratorCoreLib.Telegram;

namespace AIOrchestratorCoreLib.Bridge.CommandBars;

/// <summary>
/// THE ENGINE'S HALF OF THE BUTTON BARS (plan 03 Task 5) — what <c>pulse.buttons</c>,
/// <c>general.buttons</c> and <c>pulse.holdToggle</c> become on the phone, and the one log line each owes
/// when it asks for something the app cannot draw.
///
/// <para>
/// MOVED OUT OF <c>BridgeEngineModel</c>, which is not being split and does not grow by inertia
/// (code-conventions). The row shape, the General thread id and the hold toggle's placement used to be
/// three private members there; the settings added a fourth concern — saying a refusal ONCE — and state
/// to say it with, which is what makes this a component rather than another static helper.
/// </para>
/// <para>
/// EVERY VALUE IS HANDED IN, NEVER HELD. The engine reads <c>_configProvider.Get_Current()</c> at the point
/// of effect and passes the blocks; nothing here caches a setting, so an edit to config.json is on the
/// next bar the engine draws. What IS held is only the set of lines already said.
/// </para>
/// </summary>
public interface ICommandBars
{
    /// <summary>
    /// PULSE's bar for one orchestration topic, as rows of two: the configured verbs a tap can run, in
    /// the configured order, then the hold toggle when its placement is the bar. A configured verb no tap
    /// can run is left off and said once.
    /// </summary>
    IReadOnlyList<IReadOnlyList<(string Data, string Label)>> Build_TopicRows(
        IPulseSettings pulse, ReceiptStyles receipts, long messageThreadId, bool isHolding, int heldCount);

    /// <summary>
    /// General's bar, as rows of two. NO ROWS when <c>general.buttons</c> is empty (D4) — the dashboard
    /// then goes out with no <c>reply_markup</c> at all.
    ///
    /// <para>
    /// General's bar was built and unit-tested from 2026-09-09 and NEVER RENDERED — its builder had no
    /// production caller — which is why DndKeepsTheSilentSurfacesCurrentTests and
    /// ConfigurableCommandButtonsTests ask what reached the client, not what this returns.
    /// </para>
    /// </summary>
    IReadOnlyList<IReadOnlyList<(string Data, string Label)>> Build_GeneralRows(IPulseSettings pulse);

    /// <summary>
    /// Whether the ⏸/▶ hold toggle is drawn on the PULSE bar rather than on the receipt — THE one value
    /// both homes read (<see cref="HoldTogglePlacement_Resolver"/>). D10's fallback is said once.
    /// </summary>
    bool Is_HoldToggleOnTheBar(IPulseSettings pulse, ReceiptStyles receipts);
}
