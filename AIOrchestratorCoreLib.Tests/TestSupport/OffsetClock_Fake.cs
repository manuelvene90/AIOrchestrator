using AIOrchestratorCoreLib.Time.Clock;

namespace AIOrchestratorCoreLib.Tests.TestSupport;

/// <summary>
/// THE WALL CLOCK, WHICH A TEST CAN JUMP FORWARD. It runs with real time — so everything the engine
/// paces off its injected clock (the tailer's trailing-entry quiet period first of all) behaves exactly
/// as under <c>Clock_Factory.Create_System()</c> — and <see cref="Advance"/> adds a fixed offset on top.
///
/// <para>
/// WHY NOT <c>FixedClock_Fake</c>: a clock that stands still also stops the tailer from ever releasing a
/// trailing entry, so an engine test that needs a session's entry mirrored could not also freeze time.
/// Written for the status line's minute (owner, 2026-09-30: PULSE moves once the session's last message
/// is a minute old), which a test must be able to cross without waiting it out.
/// </para>
/// </summary>
internal sealed class OffsetClock_Fake : IClock
{
    readonly object _lock = new();
    TimeSpan _offset = TimeSpan.Zero;

    public DateTime UtcNow
    {
        get
        {
            lock (_lock)
                return DateTime.UtcNow + _offset;
        }
    }

    public void Advance(TimeSpan span)
    {
        lock (_lock)
            _offset += span;
    }
}
