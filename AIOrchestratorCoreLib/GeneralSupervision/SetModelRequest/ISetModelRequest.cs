namespace AIOrchestratorCoreLib.GeneralSupervision.SetModelRequest;

/// <summary>
/// A supervisor's request to change the model for ONE orchestration's role (owner: "use fable for
/// this") — a per-orchestration override, never a change to the global defaults. The app stores
/// it on session.json and respawns the affected sessions, which resume from their channels.
/// </summary>
public interface ISetModelRequest
{
    string OrchId { get; }

    /// <summary>"supervisor" or "implementer".</summary>
    string Role { get; }

    string Model { get; }
    string SourceFilePath { get; }
}
