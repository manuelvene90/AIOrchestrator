namespace AIOrchestratorCoreLib.Bridge.OwnerDeliveryBuffer;

public static class OwnerDeliveryBuffer_Factory
{
    public static IOwnerDeliveryBuffer Create(int aggregationSeconds, int holdCapSeconds)
    {
        if (aggregationSeconds < 1)
            throw new ArgumentException($"aggregationSeconds must be >= 1, got {aggregationSeconds}");

        if (holdCapSeconds < aggregationSeconds)
            throw new ArgumentException($"holdCapSeconds ({holdCapSeconds}) must be >= aggregationSeconds ({aggregationSeconds}) — a hold that expires sooner than the normal window would be pointless");

        return new OwnerDeliveryBufferModel(aggregationSeconds, holdCapSeconds);
    }
}
