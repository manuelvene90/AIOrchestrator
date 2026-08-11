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

    /// <summary>
    /// The 🇮🇹 translation layer: whether outbound Telegram traffic is translated to Italian on the
    /// way out. Unlike the two mode switches this one is PERSISTED (config.json), because it is a
    /// preference rather than a passing state — it must survive an app restart.
    /// </summary>
    void Set_ItalianLayer(bool enabled);

    /// <summary>
    /// Closes an orchestration on the OWNER's own instruction, from the app, where they have already
    /// answered a modal. It is a direct call and not a request file on purpose: the request protocol
    /// is the AGENT path, where every close parks until the owner taps, and the app does not need a
    /// file protocol to talk to itself.
    ///
    /// The alternative — a "the owner already agreed" flag in the request JSON — was written and
    /// removed. Nobody would have had to forge it: a role command written from that JSON, or an
    /// agent copying a request shape out of an archived file, and "no tap, no close" quietly stops
    /// being true while everyone behaves reasonably.
    /// </summary>
    void Close_Orchestration_ByOwner(string orchId, string reason);

    /// <summary>Raised (from background threads) whenever an orchestration's channels changed.</summary>
    event Action<string>? OrchestrationActivity;

    /// <summary>Raised when DND toggles — by the UI, by a Telegram command, by an agent request, or by the owner texting.</summary>
    event Action<bool>? MutedChanged;

    /// <summary>Raised when app-wide silence toggles, so the UI stays in sync with /mute_all.</summary>
    event Action<bool>? SilenceAllChanged;

    /// <summary>Raised when the Italian layer toggles, so the UI stays in sync with /italian.</summary>
    event Action<bool>? ItalianLayerChanged;
}
