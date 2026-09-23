namespace AIOrchestratorCoreLib.Telegram.SettingsMenu;

/// <summary>
/// WHAT ONE /settings MESSAGE SHOWS (plan 04 Task 4, spec §8.2). The menu is one message that edits itself,
/// so a view is not a message — it is what that message currently reads, and every button's payload names
/// the view the tap lands on (<see cref="SettingsButton_Data"/>).
///
/// <para>
/// <see cref="Categories"/> the sections with rows; <see cref="Category"/> one section's settings, paged;
/// <see cref="Setting"/> one setting — its value, origin, restart and description, and what can be done with
/// it; <see cref="Values"/> a Choice's words or a picker list's words, paged.
/// </para>
/// <para>
/// TWO VIEWS ADDED BY RULING P6 (2026-09-23), one per owner answer. <see cref="Confirm"/> is D2 (a): every
/// editable Kernel row outside the three fenced keys takes a SECOND tap, so its first tap lands here and
/// writes nothing. <see cref="Orchestration"/> is D3: /settings typed in an orchestration topic answers
/// read-only with that orchestration's own rows and points at /model and /effort — it has no buttons, so no
/// payload ever names it.
/// </para>
/// </summary>
public enum SettingsMenuViews
{
    Categories,
    Category,
    Setting,
    Values,
    Confirm,
    Orchestration,
}
