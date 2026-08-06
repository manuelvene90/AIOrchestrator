namespace AIOrchestratorCoreLib.Bridge.OwnerDeliveryBuffer;

public static class OwnerDeliveryBuffer_Factory
{
    public static IOwnerDeliveryBuffer Create(int aggregationSeconds)
    {
        if (aggregationSeconds < 1)
            throw new ArgumentException($"aggregationSeconds must be >= 1, got {aggregationSeconds}");

        return new OwnerDeliveryBufferModel(aggregationSeconds);
    }
}
