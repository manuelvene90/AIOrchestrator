namespace AIOrchestratorCoreLib.Bridge.SettingsMenu;

internal sealed class SettingsMenuStateModel : ISettingsMenuState
{
    readonly Lock _lock = new();

    long? _liveMenuMessageId;

    /// <summary>Keyed by topic; <see cref="GENERAL_KEY"/> stands for General, because a dictionary key cannot be null.</summary>
    readonly Dictionary<long, ISettingsReplyStep> _stepsByThread = [];

    /// <summary>No forum topic id is ever negative, so this cannot collide with a real one.</summary>
    const long GENERAL_KEY = -1;

    internal SettingsMenuStateModel(long? liveMenuMessageId, IReadOnlyList<ISettingsReplyStep> steps)
    {
        _liveMenuMessageId = liveMenuMessageId;

        foreach (var step in steps)
            _stepsByThread[Key_Of(step.ThreadId)] = step;
    }

    public long? LiveMenuMessageId
    {
        get
        {
            lock (_lock)
                return _liveMenuMessageId;
        }
    }

    public long? Replace_LiveMenu(long? messageId)
    {
        lock (_lock)
        {
            var previous = _liveMenuMessageId;

            _liveMenuMessageId = messageId;
            _stepsByThread.Clear();

            return previous;
        }
    }

    public ISettingsReplyStep? Find_Step_OrNull(long? threadId)
    {
        lock (_lock)
            return _stepsByThread.TryGetValue(Key_Of(threadId), out var step) ? step : null;
    }

    public void Put_Step(ISettingsReplyStep step)
    {
        lock (_lock)
            _stepsByThread[Key_Of(step.ThreadId)] = step;
    }

    public bool Clear_Step(long? threadId)
    {
        lock (_lock)
            return _stepsByThread.Remove(Key_Of(threadId));
    }

    public IReadOnlyList<ISettingsReplyStep> Read_Steps()
    {
        lock (_lock)
            return [.. _stepsByThread.Values];
    }

    static long Key_Of(long? threadId)
    {
        return threadId ?? GENERAL_KEY;
    }
}
