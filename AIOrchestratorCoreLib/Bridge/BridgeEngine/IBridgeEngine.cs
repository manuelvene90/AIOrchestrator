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
    /// Mute = no OUTBOUND Telegram (mirror + limit alerts) while the owner works at the PC.
    /// Channel files stay the full history; muted entries are simply not mirrored (gaps in the
    /// topic are accepted by design). Inbound routing keeps working.
    /// </summary>
    void Set_TelegramMuted(bool muted);

    /// <summary>Raised (from background threads) whenever an orchestration's channels changed.</summary>
    event Action<string>? OrchestrationActivity;

    /// <summary>Raised when DND toggles — by the UI, by an agent request, or by the owner texting (auto-unmute).</summary>
    event Action<bool>? MutedChanged;
}
