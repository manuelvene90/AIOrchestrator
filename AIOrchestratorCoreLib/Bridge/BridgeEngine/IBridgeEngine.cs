namespace AIOrchestratorCoreLib.Bridge.BridgeEngine;

/// <summary>
/// The background service: mirrors channel traffic to Telegram, routes owner messages back into
/// channel files, and executes agent requests (.requests/). Runs until the token is cancelled.
/// The file protocol works without it — this engine only adds the Telegram window and automation.
/// </summary>
public interface IBridgeEngine
{
    Task Run_Async(CancellationToken cancellationToken);

    /// <summary>
    /// App-wide DO-NOT-DISTURB (🌙): outbound Telegram traffic is HELD and replayed in full when
    /// it goes off — nothing is lost. For being away from the phone. A topic's own mode overrides
    /// this. Inbound routing keeps working, and the owner texting anything lifts it.
    /// </summary>
    void Set_TelegramMuted(bool muted);

    /// <summary>
    /// App-wide SILENCE (🔕): outbound Telegram traffic is DROPPED while it lasts — for working
    /// at the PC, where the same content is already in the terminals. Deliberately lossy, which is
    /// exactly what distinguishes it from Do-Not-Disturb.
    /// </summary>
    void Set_SilenceAllTopics(bool silenced);

    /// <summary>Raised (from background threads) whenever an orchestration's channels changed.</summary>
    event Action<string>? OrchestrationActivity;

    /// <summary>Raised when DND toggles — by the UI, by a Telegram command, by an agent request, or by the owner texting.</summary>
    event Action<bool>? MutedChanged;

    /// <summary>Raised when app-wide silence toggles, so the UI stays in sync with /mute_all.</summary>
    event Action<bool>? SilenceAllChanged;
}
