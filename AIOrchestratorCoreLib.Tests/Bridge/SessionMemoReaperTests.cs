using AIOrchestratorCoreLib.Bridge;
using Xunit;

namespace AIOrchestratorCoreLib.Tests.Bridge;

/// <summary>
/// THE RULE THAT DECIDES WHICH PER-SESSION MEMO HAS OUTLIVED ITS SESSION. It is a pure function
/// because the engine that holds those memos is <c>internal sealed</c> with no
/// <c>InternalsVisibleTo</c>, and because dropping a memo has NO observable behaviour to assert on
/// from outside — only a footprint. Decided in here, it is checkable; decided in the engine it would
/// be neither tested nor testable.
/// </summary>
public class SessionMemoReaperTests
{
    const string LIVE = @"C:\state\repo-1\supervisor.state.json";
    const string CLOSED = @"C:\state\repo-1\imp-2.state.json";

    /// <summary>
    /// THE DEFECT, BOTH WAYS ROUND IN ONE ASSERTION. A reaper that returns everything would also
    /// satisfy "the closed one is dropped", so the live key is asserted to SURVIVE in the same call —
    /// dropping a live session's watch would re-arm it and give a genuinely stalled session a fresh
    /// window of silence.
    /// </summary>
    [Fact]
    public void AMemoWhoseSessionIsGoneIsReaped_AndALiveOneIsNot()
    {
        var orphans = SessionMemo_Reaper.Find_Orphans([LIVE, CLOSED], [LIVE]);

        Assert.Equal([CLOSED], orphans);
    }

    /// <summary>
    /// NOTHING IS REAPED WHEN NOTHING IS GONE — the control. Without it the case above is satisfied
    /// by a reaper that drops whatever it is handed second.
    /// </summary>
    [Fact]
    public void EverySessionStillRegistered_MeansNothingToReap()
    {
        Assert.Empty(SessionMemo_Reaper.Find_Orphans([LIVE, CLOSED], [CLOSED, LIVE]));
    }

    /// <summary>
    /// THE ENGINE'S HOST IS WINDOWS AND THESE KEYS ARE FILE PATHS, so two spellings of one path are
    /// one session. A case-sensitive comparison would call a live session's memo an orphan and drop
    /// it — the failure pointing the wrong way, silently.
    /// </summary>
    [Fact]
    public void TwoSpellingsOfOnePath_AreOneSession()
    {
        Assert.Empty(SessionMemo_Reaper.Find_Orphans([LIVE], [LIVE.ToUpperInvariant()]));
    }

    /// <summary>
    /// EVERY SESSION GONE — an app whose last orchestration has just closed. The whole map empties,
    /// which is the state the daemon has to be able to reach after weeks of closed orchestrations.
    /// </summary>
    [Fact]
    public void NoLiveSessionsAtAll_ReapsEverything()
    {
        Assert.Equal([LIVE, CLOSED], SessionMemo_Reaper.Find_Orphans([LIVE, CLOSED], []));
    }
}
