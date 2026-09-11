namespace AIOrchestratorCoreLib.Bridge.ChannelChangeWaker;

/// <summary>
/// THE WAIT AT THE BOTTOM OF THE MIRROR LOOP: it ends when a channel file is written, or when the
/// tick elapses, whichever comes first.
///
/// <para>
/// WHY IT EXISTS. Measured on the VPS on 2026-09-09: 11–12 s median from the owner's Telegram message
/// to their supervisor's turn starting. <c>MIRROR_TICK_MILLISECONDS</c> is 2000, so anything appended
/// to a channel waited on average a second — and up to two — before the loop so much as looked at it,
/// and the owner's path pays that TWICE, once for the tick that writes their message into the channel
/// and once for the tick that carries the answer back. The owner's decision that day was to react to
/// the write and keep the tick.
/// </para>
/// <para>
/// THE TICK IS A CEILING, NEVER A QUANTUM, and that is the whole safety contract. Filesystem
/// notification is best-effort everywhere and absent in places this app runs: inotify runs out of
/// watches on a Linux box with many folders, a network filesystem reports nothing at all, a container
/// can have no notification backend. A waker that never fires is therefore not a fault condition — it
/// is the loop the bridge had before this existed, unchanged, and every caller must be written so that
/// the difference is latency and nothing else.
/// </para>
/// </summary>
public interface IChannelChangeWaker : IDisposable
{
    /// <summary>
    /// Returns when a channel file changed or <paramref name="tickMilliseconds"/> elapsed.
    /// Throws <see cref="OperationCanceledException"/> on cancellation, exactly as the bare
    /// <c>Task.Delay</c> it replaced did — the loop above already catches that and ends.
    /// </summary>
    Task Wait_ForChangeOrTick_Async(int tickMilliseconds, CancellationToken cancellationToken);
}
