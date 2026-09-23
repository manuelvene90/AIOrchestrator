using AIOrchestratorCoreLib.Configuration.PhoneSettings;
using AIOrchestratorCoreLib.Configuration.PulseSettings;

namespace AIOrchestratorCoreLib.Telegram;

/// <summary>
/// WHERE THE ⏸/▶ HOLD TOGGLE IS DRAWN — on the PULSE bar or on the receipt, and NEVER BOTH (plan 03
/// Task 5). One answer, handed to both homes: <see cref="TopicCommandButtons.Build_ForTopic"/> draws it
/// only when this says the bar, <see cref="ReceiptButtons_Builder"/> only when it says the receipt.
///
/// <para>
/// WHY A RESOLVER AND NOT A STRAIGHT READ OF <c>pulse.holdToggle</c>: the key has two coherent presets
/// and two incoherent cross combinations (plan D10). Classic is <c>false</c> + <c>ticks</c> — the toggle
/// rides the ✓ tick, master's phone. Quiet is <c>true</c> + <c>reactions</c> — no tick is posted at all,
/// so the toggle lives on PULSE, brief D's phone. <c>false</c> + <c>reactions</c> asks for the toggle on a
/// receipt message the owner has also asked never to be sent, and read literally it leaves them with
/// no toggle anywhere.
/// </para>
/// <para>
/// THAT COMBINATION FALLS BACK TO THE BAR, and says so ONCE in the log (D10, answered 2026-09-14). This is
/// decision 21's rule — the app enforces at the point of effect, and a refusal at load would only take
/// the toggle away. "One toggle in one place" still holds, because the fallback picks one. The line goes
/// to the log and never to Telegram (decision 15): the owner cannot fix a config key from the phone.
/// </para>
/// </summary>
public static class HoldTogglePlacement_Resolver
{
    /// <summary>
    /// The one warning for D10's fallback, naming BOTH keys — either one is the fix, and a line naming only
    /// the key that "lost" would send the owner to change the one they may have meant.
    /// </summary>
    public const string FALLBACK_WARNING =
        $"{PulseSettings_Json.HOLD_TOGGLE_PATH} = false asks for the ⏸/▶ hold toggle on the receipt, but " +
        $"{PhoneSettings_Json.RECEIPTS_PATH} = reactions posts no receipt message to carry it — the toggle is " +
        $"drawn on the PULSE bar instead (set {PulseSettings_Json.HOLD_TOGGLE_PATH} to true, or " +
        $"{PhoneSettings_Json.RECEIPTS_PATH} to ticks, to choose)";

    /// <summary>
    /// <c>OnTheBar</c> is the placement; <c>IsFallback</c> is true only for D10's combination, so the
    /// caller that holds a log can say it once. Pure: the caller resolves both keys at the point of
    /// effect and hands them in.
    /// </summary>
    public static (bool OnTheBar, bool IsFallback) Resolve(bool holdToggle, ReceiptStyles receipts)
    {
        if (holdToggle)
            return (true, false);

        return receipts == ReceiptStyles.Reactions
            ? (true, true)
            : (false, false);
    }
}
