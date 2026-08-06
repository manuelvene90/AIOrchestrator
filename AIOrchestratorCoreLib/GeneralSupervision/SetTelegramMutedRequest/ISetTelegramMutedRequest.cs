namespace AIOrchestratorCoreLib.GeneralSupervision.SetTelegramMutedRequest;

/// <summary>
/// A supervisor's (or the general supervisor's) request to toggle Do-Not-Disturb: muted = true
/// stops app→owner Telegram texts until re-enabled; pending mirror traffic accumulates and is
/// delivered in one catch-up burst on unmute. The owner texting ANYTHING also auto-unmutes.
/// </summary>
public interface ISetTelegramMutedRequest
{
    bool Muted { get; }
    string SourceFilePath { get; }
}
