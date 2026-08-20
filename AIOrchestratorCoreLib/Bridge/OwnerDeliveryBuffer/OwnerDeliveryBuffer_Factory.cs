namespace AIOrchestratorCoreLib.Bridge.OwnerDeliveryBuffer;

public static class OwnerDeliveryBuffer_Factory
{
    /// <summary>
    /// THERE IS NO HOLD CAP ANY MORE. It took a second argument until 2026-08-20 — sixty seconds,
    /// after which a hold ended by itself — and the owner removed it once they saw what it actually
    /// did: it ended in silence, so the receipt reverted to delivered and every following message
    /// went through as though they had never pressed anything.
    ///
    /// The parameter is GONE rather than defaulted, so no caller can quietly reintroduce a lapse.
    /// </summary>
    public static IOwnerDeliveryBuffer Create(int aggregationSeconds)
    {
        if (aggregationSeconds < 1)
            throw new ArgumentException($"aggregationSeconds must be >= 1, got {aggregationSeconds}");

        return new OwnerDeliveryBufferModel(aggregationSeconds);
    }
}
