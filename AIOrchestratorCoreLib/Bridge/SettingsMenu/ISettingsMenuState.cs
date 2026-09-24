namespace AIOrchestratorCoreLib.Bridge.SettingsMenu;

/// <summary>
/// WHAT THE /settings MENU REMEMBERS (plan 04 Task 5): the one live menu message, and the pending reply steps.
/// Out of <c>BridgeEngineModel</c> per the code-conventions rule — the engine holds this object, persists what
/// it reads off it, and keeps no field of its own for either.
///
/// <para>
/// ONE LIVE MENU, because this bridge serves ONE supergroup and the machine menu lives only in its General
/// topic (D3) — "per chat" is exactly one. It is the message the edit-gap exemption follows (D7).
/// </para>
/// <para>
/// A STEP PER TOPIC (D9), and replacing the live menu clears every one of them: a prompt whose message has been
/// replaced is a prompt the owner can no longer see, and a step nobody can see is the invisible trap D9 forbids.
/// </para>
/// <para>
/// THREAD-SAFE: read by the engine's persistence from any loop, written by the inbound loop.
/// </para>
/// </summary>
public interface ISettingsMenuState
{
    /// <summary>The message the machine menu currently lives in, or null when there is none.</summary>
    long? LiveMenuMessageId { get; }

    /// <summary>
    /// Makes <paramref name="messageId"/> the live menu and CLEARS every pending step (D9: "cleared whenever the
    /// menu message is closed or replaced"). Returns the id it replaced, so the caller can release that
    /// message's exemption and take it down.
    /// </summary>
    long? Replace_LiveMenu(long? messageId);

    ISettingsReplyStep? Find_Step_OrNull(long? threadId);

    /// <summary>Starts — or, for a held value, replaces — the step for its topic.</summary>
    void Put_Step(ISettingsReplyStep step);

    /// <summary>True when a step was there to clear.</summary>
    bool Clear_Step(long? threadId);

    /// <summary>Every pending step, for persistence.</summary>
    IReadOnlyList<ISettingsReplyStep> Read_Steps();
}
