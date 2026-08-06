namespace AIOrchestratorCoreLib.Bridge.OwnerDeliveryBuffer;

internal sealed class OwnerDeliveryBufferModel(int aggregationSeconds) : IOwnerDeliveryBuffer
{
    sealed class PendingDelivery
    {
        public readonly List<string> Segments = [];
        public DateTime LastArrivalUtc;
    }

    readonly int _aggregationSeconds = aggregationSeconds;
    readonly Dictionary<string, PendingDelivery> _pending = [];
    readonly Lock _lock = new();

    public void Add_Segment(string targetKey, string segment, DateTime nowUtc)
    {
        lock (_lock)
        {
            if (!_pending.TryGetValue(targetKey, out var delivery))
            {
                delivery = new PendingDelivery();
                _pending[targetKey] = delivery;
            }

            delivery.Segments.Add(segment);
            delivery.LastArrivalUtc = nowUtc;
        }
    }

    public IReadOnlyDictionary<string, string> Take_ReadyDeliveries(DateTime nowUtc)
    {
        Dictionary<string, string> ready = [];

        lock (_lock)
        {
            List<string> readyKeys = [];

            foreach (var pair in _pending)
            {
                if ((nowUtc - pair.Value.LastArrivalUtc).TotalSeconds >= _aggregationSeconds)
                    readyKeys.Add(pair.Key);
            }

            foreach (var key in readyKeys)
            {
                ready[key] = string.Join("\n\n", _pending[key].Segments);
                _pending.Remove(key);
            }
        }

        return ready;
    }

    public bool Has_PendingDeliveries()
    {
        lock (_lock)
        {
            return _pending.Count > 0;
        }
    }
}
