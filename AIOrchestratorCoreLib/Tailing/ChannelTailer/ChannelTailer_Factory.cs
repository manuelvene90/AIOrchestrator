namespace AIOrchestratorCoreLib.Tailing.ChannelTailer;

public static class ChannelTailer_Factory
{
    public static IChannelTailer Create(IReadOnlyDictionary<string, long> persistedOffsets)
    {
        return new ChannelTailerModel(persistedOffsets);
    }

    public static IChannelTailer Create_Fresh()
    {
        return Create(new Dictionary<string, long>());
    }
}
