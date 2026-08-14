using Xunit;

namespace AIOrchestratorCoreLib.Tests.Channels;

/// <summary>
/// Serialises every test class that exercises the channel lock.
/// <para>
/// Not a workaround — it follows from the design. <c>ChannelLock_Diagnostics</c> is a PROCESS-WIDE
/// sink, deliberately, because the lock has to be able to report a failure no matter which of the
/// ~24 call sites triggered it. A process-wide sink is also process-wide in a test run: with these
/// classes in parallel, one class's contention lands in another's captured lines, and a test that
/// asserts "exactly one diagnostic" fails for a reason that has nothing to do with its subject.
/// That happened, and the failure named the wrong culprit.
/// </para>
/// <para>
/// These classes also contend for real filesystem locks and spawn bash processes, so serialising
/// them makes the timing assertions mean what they say.
/// </para>
/// </summary>
[CollectionDefinition(NAME)]
public class CHANNEL_LOCK_COLLECTION
{
    public const string NAME = "channel-lock";
}
