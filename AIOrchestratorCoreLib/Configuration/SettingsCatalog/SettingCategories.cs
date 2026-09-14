namespace AIOrchestratorCoreLib.Configuration.SettingsCatalog;

/// <summary>
/// The GROUPING a renderer shows a setting under — the tab in the WPF window, the section of a
/// Telegram menu, the heading of the web page. <c>Phone</c> notification taste, <c>Pulse</c> the
/// topic status line's fields, <c>Receipts</c> how the bot marks a read message, <c>Models</c> which
/// model/effort a role spawns with, <c>Kernel</c> host-level plumbing (never hand-toggled lightly),
/// <c>Kit</c> the installed role commands and hooks, <c>Owner</c> facts about the owner themself
/// (their Telegram id, their timezone) rather than a behaviour choice.
/// </summary>
public enum SettingCategories
{
    Phone,
    Pulse,
    Receipts,
    Models,
    Kernel,
    Kit,
    Owner,
}
