namespace AIOrchestratorCoreLib.Bridge.SettingsMenu;

/// <summary>
/// The two things the /settings menu needs from the engine that holds it — adapters only, the shape
/// <c>IPeriodicStatusHost</c> set: the menu is built with no reference to the engine (a primary constructor's
/// field initialiser cannot hand it <c>this</c>), and the engine passes itself per call.
/// </summary>
public interface ISettingsMenuHost
{
    /// <summary>Tracks a message so <c>/clear</c> in its topic removes it with the rest.</summary>
    void Remember_TopicMessage(long? messageThreadId, long? messageId);

    /// <summary>Writes the engine state — which carries the live menu and the pending steps (ruling P23).</summary>
    void Persist_EngineState();
}
