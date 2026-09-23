namespace AIOrchestratorCoreLib.Running.TurnSource;

public static class TurnSource_Factory
{
    /// <summary>The key of the conversation with the owner, on every role that has one.</summary>
    public const string OWNER_KEY = "owner";

    /// <summary>
    /// The prefix of a sibling's key, <c>sibling:&lt;orchId&gt;</c>. A PREFIX, not the bare orch id, because a
    /// key is also a cursor's name in the state file and a label in the prompt: it must never collide with
    /// <c>owner</c> or a member id, and it has to tell the session reading it what the channel is.
    /// </summary>
    public const string SIBLING_KEY_PREFIX = "sibling:";

    public static ITurnSource Create_Owner(string channelFilePath)
    {
        return Create(OWNER_KEY, channelFilePath, isOwnerChannel: true);
    }

    /// <summary>A member's spoke, addressed by the member id — <c>imp-1</c>, <c>rev-2</c>.</summary>
    public static ITurnSource Create_Spoke(string memberId, string channelFilePath)
    {
        if (string.Equals(memberId, OWNER_KEY, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException($"'{OWNER_KEY}' is reserved for the owner conversation and cannot be a member id");

        return Create(memberId, channelFilePath, isOwnerChannel: false);
    }

    /// <summary>Another solo's outbox, read by a linked solo (spec 2026-09-23 §5.4). Keyed <c>sibling:&lt;orchId&gt;</c>.</summary>
    public static ITurnSource Create_Sibling(string siblingOrchId, string outboxPath)
    {
        if (string.IsNullOrWhiteSpace(siblingOrchId))
            throw new ArgumentException($"A sibling source needs the sibling's orch id (outbox '{outboxPath}')");

        return Create_OfKind($"{SIBLING_KEY_PREFIX}{siblingOrchId}", outboxPath, TurnSourceKinds.Sibling);
    }

    /// <summary>An owner or a spoke source: the two kinds that existed before siblings, and all this shape can say.</summary>
    public static ITurnSource Create(string key, string channelFilePath, bool isOwnerChannel)
    {
        return Create_OfKind(key, channelFilePath, isOwnerChannel ? TurnSourceKinds.Owner : TurnSourceKinds.Spoke);
    }

    static ITurnSource Create_OfKind(string key, string channelFilePath, TurnSourceKinds kind)
    {
        if (string.IsNullOrWhiteSpace(key))
            throw new ArgumentException($"A turn source needs a key (channel '{channelFilePath}')");
        if (string.IsNullOrWhiteSpace(channelFilePath))
            throw new ArgumentException($"A turn source needs a channel file (key '{key}')");

        return new TurnSourceModel(key, channelFilePath, kind);
    }
}
