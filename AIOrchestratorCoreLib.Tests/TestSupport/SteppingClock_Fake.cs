using AIOrchestratorCoreLib.Time.Clock;

namespace AIOrchestratorCoreLib.Tests.TestSupport;

/// <summary>
/// A CLOCK THAT MOVES ON BEING READ, by a fixed step — for the tests that drive
/// <c>IChannelTailer.Poll</c> synchronously, in a tight loop, with no wall clock to wait out.
///
/// <para>
/// WHY IT EXISTS AT ALL. Until 2026-09-09 the tailer released a trailing entry after two POLLS with no
/// growth, so a synchronous test satisfied the rule for free and the whole suite could be written with
/// no sleeps in it. The rule is a DURATION now — the count silently meant "two times the poll
/// interval", and the mirror loop's new 200 ms cadence cut the protection 12× without touching the
/// constant (<c>ChannelTailer_Factory.TRAILING_ENTRY_QUIET_MILLISECONDS</c>). Those tests must
/// therefore express time, and the honest way to keep them free is to let each poll BE a step.
/// </para>
/// <para>
/// THE STEP IS THE MIRROR TICK, deliberately: one read per <c>Poll</c> at 2000 ms reproduces exactly
/// the cadence the poll-count rule was written against, so the assertions those tests already make —
/// read poll, quiet poll, quiet poll, release — mean the same thing they always did.
/// </para>
/// </summary>
internal sealed class SteppingClock_Fake(TimeSpan step) : IClock
{
    /// <summary>An arbitrary fixed origin: nothing here depends on the wall clock, which is the point.</summary>
    DateTime _utcNow = new(2026, 9, 9, 12, 0, 0, DateTimeKind.Utc);

    public DateTime UtcNow
    {
        get
        {
            var reading = _utcNow;
            _utcNow += step;
            return reading;
        }
    }
}
