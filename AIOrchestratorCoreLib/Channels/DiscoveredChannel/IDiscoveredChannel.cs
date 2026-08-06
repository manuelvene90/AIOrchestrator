namespace AIOrchestratorCoreLib.Channels.DiscoveredChannel;

/// <summary>A channel file found under the supervision root, with its orchestration coordinates.</summary>
public interface IDiscoveredChannel
{
    string OrchId { get; }

    /// <summary>'imp-n' for an implementer spoke, 'owner' for the owner ⇄ supervisor channel.</summary>
    string SpokeName { get; }

    string FilePath { get; }
    bool IsOwnerChannel { get; }
}
