namespace AIOrchestratorCoreLib.Telegram.SettingsMenu;

/// <summary>
/// WHAT A /settings TAP ASKS TO CHANGE, beside the view it lands on. A payload with no edit only navigates.
///
/// <para>
/// A SEPARATE AXIS FROM THE VIEW, because the same view is reached with and without a change: the Setting view
/// after a toggle flip, after a Reset, after a Confirm's Yes, and after a plain Back. Folding the change into
/// the view would give the payload one enum member per (view, change) pair; keeping it apart lets the one rule
/// that matters — a <see cref="SettingsMenuViews.Confirm"/> view never writes (ruling P6) — be one line in
/// <see cref="SettingsButton_Data.Writes_OnTap"/>.
/// </para>
/// <list type="bullet">
/// <item><see cref="Set"/> — write the carried word (a Toggle's on/off, a Choice's word, "not set" for a
/// nullable Choice's null meaning), through <c>SettingValue_Parser.Parse</c> exactly as the WPF editor does.</item>
/// <item><see cref="Add"/> / <see cref="Remove"/> — a picker list gains or loses ONE word of its current value
/// (ruling P39: offers are candidates, never a list to write whole).</item>
/// <item><see cref="Reset"/> — delete the key from config.json, so the preset or the shipped default answers.</item>
/// <item><see cref="Reply"/> — start the "reply with the value" step (Task 5's state, D9). Writes nothing.</item>
/// <item><see cref="ApplyHeldReply"/> — write the value the reply step is HOLDING. A Kernel reply's Confirm
/// carries only the id (ruling P6): the typed text never rides in a payload, where 64 bytes would cut it.</item>
/// </list>
/// </summary>
public enum SettingsMenuEdits
{
    Set,
    Add,
    Remove,
    Reset,
    Reply,
    ApplyHeldReply,
}
