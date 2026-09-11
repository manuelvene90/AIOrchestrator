namespace AIOrchestratorCoreLib.Running.TurnSource;

public static class TurnSource_Factory
{
    /// <summary>The key of the conversation with the owner, on every role that has one.</summary>
    public const string OWNER_KEY = "owner";

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

    public static ITurnSource Create(string key, string channelFilePath, bool isOwnerChannel)
    {
        if (string.IsNullOrWhiteSpace(key))
            throw new ArgumentException($"A turn source needs a key (channel '{channelFilePath}')");
        if (string.IsNullOrWhiteSpace(channelFilePath))
            throw new ArgumentException($"A turn source needs a channel file (key '{key}')");

        return new TurnSourceModel(key, channelFilePath, isOwnerChannel);
    }
}
