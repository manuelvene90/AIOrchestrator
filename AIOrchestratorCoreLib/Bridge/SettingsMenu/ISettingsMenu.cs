using AIOrchestratorCoreLib.Telegram.TelegramApiClient;
using AIOrchestratorCoreLib.Telegram.TelegramCallbackTap;
using AIOrchestratorCoreLib.Telegram.TelegramOwnerMessage;

namespace AIOrchestratorCoreLib.Bridge.SettingsMenu;

/// <summary>
/// THE TELEGRAM /settings MENU, WIRED (plan 04 Task 5, spec §8.2). The owner, 2026-09-23: a /settings dialog in
/// the General topic, so both they and the fork's author — headless on Linux, with no desktop window — can change
/// every behavioural setting from a phone. Task 4 drew the menu as a pure function
/// (<c>Telegram.SettingsMenu.SettingsMenu_Builder</c>); this sends it, edits it in place, writes what a tap or a
/// typed value asks for through <c>Settings_Writer</c>, and runs D9's reply step.
///
/// <para>
/// OUT OF <c>BridgeEngineModel</c>, per the code-conventions rule. The engine keeps four call sites — the
/// command in its typed chain, the tap as the fifth <c>Try_Handle…</c> before the generic option path, and the
/// reply step at its two points in the inbound loop — and nothing else.
/// </para>
/// </summary>
public interface ISettingsMenu
{
    /// <summary>What is persisted with the engine state.</summary>
    ISettingsMenuState State { get; }

    /// <summary>
    /// <c>/settings</c>. In General: the categories, as a NEW live menu message (the previous one is released and
    /// taken down). In an orchestration topic: that orchestration's rows, read-only, pointing at /model and
    /// /effort (D3) — no buttons, so nothing there can write a machine setting.
    /// </summary>
    Task Send_Menu_Async(ITelegramApiClient client, ISettingsMenuHost host, long? messageThreadId, CancellationToken cancellationToken);

    /// <summary>
    /// A "set:" tap: answered always, applied only when it resolves against this build's catalogue (D8), only in
    /// General (D3), and written only when the phone may change the row (D2). False for any other payload.
    /// </summary>
    Task<bool> Try_HandleTap_Async(ITelegramApiClient client, ISettingsMenuHost host, ITelegramCallbackTap tap, CancellationToken cancellationToken);

    /// <summary>
    /// A typed <c>/command</c> reaching a topic with a pending step: every command ends the step (D9) and runs as
    /// usual — except <c>/cancel</c> while the step is live, which is consumed and answered (ruling P15). True
    /// only for that consumed <c>/cancel</c>.
    /// </summary>
    Task<bool> Try_EndReplyStep_OnCommand_Async(ITelegramApiClient client, ISettingsMenuHost host, ITelegramOwnerMessage message, string command, CancellationToken cancellationToken);

    /// <summary>
    /// A routable message: true when the live step in its topic took it as the value — and then it has been
    /// answered in the topic, never swallowed silently. False when there is no live step, when it lapsed (the
    /// message routes to the supervisor as usual), or when the message is not a value.
    /// </summary>
    Task<bool> Try_TakeReply_Async(ITelegramApiClient client, ISettingsMenuHost host, ITelegramOwnerMessage message, CancellationToken cancellationToken);
}
