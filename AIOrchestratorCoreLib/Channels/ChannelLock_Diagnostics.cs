namespace AIOrchestratorCoreLib.Channels;

/// <summary>
/// Where the channel lock reports its failures, so they stop being invisible.
/// <para>
/// Why the report comes from the lock itself rather than from a logger threaded through the callers,
/// stated because the shape is a deviation and deserves one: the lock is reached from ~24 call sites
/// across <c>BridgeEngineModel</c>, and 23 of them discard the bool that says the write did not
/// happen. A signal nobody reads is indistinguishable from no signal, so the report has to come from
/// the lock ITSELF — it is the only place that knows a failure occurred regardless of what the caller
/// does with the result. Threading a logger through those call sites would work and is the tidier
/// shape, but it is a mechanical edit across a file other branches are editing, and it would still
/// leave every future caller free to drop the signal again.
/// </para>
/// <para>
/// Failures only. An uncontended write says nothing: a diagnostic channel that reports success is a
/// firehose, and the real failures drown in it.
/// </para>
///
/// <para>
/// TWO SINKS, AND THE SECOND ONE IS THE FIX FOR A YEAR-OLD FALSE FAILURE. The process-wide sink
/// (<see cref="Set_Sink"/>) is the production one and is unchanged: a real bridge wires it once at
/// startup and every lock failure anywhere in the process reaches its log, whichever thread, task or
/// call site produced it. On top of it sits <see cref="Capture_OnThisFlow"/>, an ambient sink that
/// belongs to ONE async flow and to every flow started under it, and that a report prefers when the
/// flow it is reported on has one.
/// </para>
/// <para>
/// MEASURED, 2026-09-09. A single process-wide sink is also process-wide in a TEST RUN.
/// <c>BridgeEngineModel.Run_Async</c> calls <see cref="Set_Sink"/> unconditionally at every engine
/// start; dozens of Bridge tests start a real engine; xUnit runs collections in parallel. So whichever
/// test was capturing lock diagnostics had its sink STOLEN mid-test by an unrelated one, and
/// <c>ChannelLockDiagnosticsTests</c> reported "Assert.Single() Failure: The collection was empty"
/// about a lock that had behaved perfectly. The static dates from 6ac38dc (2026-08-13) and
/// <c>.claude/rules/code-conventions.md</c> had already given up and named it the suite's known
/// intermittent. It got worse, not better, as the suite got faster: 2 reds in 4 full runs on the
/// pre-speedup commit f5368ce, then two different tests failing in a single run once the suite went
/// from ~107 s to ~37 s — the same number of sink-stealing engine starts packed into a third of the
/// wall clock collides more often.
/// </para>
/// <para>
/// WHY AN AMBIENT PER-FLOW SINK AND NOT THE OTHER THREE SHAPES. (a) Making the whole thing per-flow —
/// no process-wide sink at all — is what <c>TickIo_Counters</c> does for the same reason, but it would
/// silently change PRODUCTION: lock failures are reported synchronously on whatever flow reached the
/// lock, and most of them do not descend from <c>Run_Async</c> (a request handler, a UI-thread append,
/// a compaction on its own task). Those would stop being logged, which is the exact silence this class
/// exists to end. (b) An engine-owned sink passed down to the lock means a parameter on every
/// <c>ChannelFile_Lock</c> entry point and on <c>Channel_Compactor</c> — the mechanical edit rejected
/// above, and it still cannot serve the lock's own internal call sites. (c) An instance-based
/// diagnostics object with a default is the same edit wearing a different hat. The layering here keeps
/// production byte-for-byte and gives a test something no other test can take away from it.
/// </para>
/// <para>
/// A TEST CANNOT BE BROKEN BY A TEST THAT FORGOT. That is the property, and it is why
/// <c>Clear_Sink</c> is gone rather than kept for symmetry: the only remaining way to touch the
/// process-wide sink is <see cref="Set_Sink"/>, which production calls and no test should. A test
/// captures with <see cref="Capture_OnThisFlow"/>; a test that captures nothing cannot steal, cannot
/// silence, and cannot leak its own contention into somebody else's captured lines — an engine started
/// on another flow reports to the process-wide sink and is invisible here.
/// </para>
/// </summary>
public static class ChannelLock_Diagnostics
{
    static Action<string>? _processWideSink;

    static readonly AsyncLocal<Action<string>?> _flowSink = new();

    /// <summary>
    /// Wired once, at bridge startup, to the orchestration log. Until it is wired the lock is silent
    /// — which is the pre-existing behaviour, not a regression, and keeps the core lib usable from
    /// tests and tools that have no log.
    /// <para>
    /// PRODUCTION ONLY. A test that calls this steals the sink from every other test in the process
    /// and hands its own contention to whoever is capturing; that is the defect documented on the
    /// class. Tests use <see cref="Capture_OnThisFlow"/>.
    /// </para>
    /// </summary>
    public static void Set_Sink(Action<string> sink)
    {
        _processWideSink = sink;
    }

    /// <summary>
    /// Captures lock diagnostics reported on THIS async flow, and on every task and thread started
    /// under it, until the returned scope is disposed. Reports from any other flow are unaffected and
    /// keep going to the process-wide sink, so two captures can run at once and neither sees the other.
    /// <para>
    /// Open it in the test METHOD, not in the class constructor: an ambient value has to be set on the
    /// flow the assertions run on, and xUnit is free to hop flows between constructing a class and
    /// invoking its test. <c>TickIo_Counters.Begin_Scope</c> is used the same way for the same reason.
    /// </para>
    /// </summary>
    public static Capture Capture_OnThisFlow(Action<string> sink)
    {
        var previous = _flowSink.Value;

        _flowSink.Value = sink;

        return new Capture(previous);
    }

    /// <summary>
    /// Reports one failure. Never throws: a diagnostic that can break the write it is describing is
    /// worse than no diagnostic, and this runs inside the lock's own error paths.
    /// </summary>
    public static void Report(string message)
    {
        var sink = _flowSink.Value ?? _processWideSink;

        if (sink == null)
            return;

        try
        {
            sink(message);
        }
        catch
        {
            // Swallowed deliberately. The caller is mid-failure already; replacing its problem with
            // a logging problem would hide the thing this exists to surface.
        }
    }

    /// <summary>
    /// One open <see cref="Capture_OnThisFlow"/>. Disposing restores whatever this flow had before,
    /// so a nested capture behaves like a nested scope rather than clearing the outer one.
    /// </summary>
    public sealed class Capture(Action<string>? previous) : IDisposable
    {
        public void Dispose()
        {
            _flowSink.Value = previous;
        }
    }
}
