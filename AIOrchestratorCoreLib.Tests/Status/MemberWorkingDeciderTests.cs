using AIOrchestratorCoreLib.Status;
using Xunit;

namespace AIOrchestratorCoreLib.Tests.Status;

/// <summary>
/// "IDLE" WAS AN ASSERTION THE APP COULD NOT BACK.
///
/// <para>
/// Every liveness surface — the member chips, the typing bubble, the stall alert, the periodic status
/// feed and the orphan escalation — resolved through a probe whose only input is a file the Claude
/// Code status line writes, and a headless <c>claude -p</c> session renders no status line. So the
/// probe returned Unknown for every bridge-driven member and the caller collapsed Unknown into
/// <c>false</c>, i.e. "idle". In the fincanva-1 run of 2026-09-07 the phrase "working now" never
/// appeared once in two hours, across five members that were running turns the whole time.
/// </para>
/// <para>
/// These pin the three-way answer, and above all that Unknown never becomes Idle again.
/// </para>
/// </summary>
public class MemberWorkingDeciderTests
{
    static readonly DateTime NOW = new(2026, 9, 7, 12, 0, 0, DateTimeKind.Utc);

    static WorkingVerdicts Decide(
        bool registered = true,
        bool inFlight = false,
        bool queued = false,
        DateTime? lastTurnEndedUtc = null)
    {
        return MemberWorking_Decider.Decide(registered, inFlight, queued, lastTurnEndedUtc, NOW);
    }

    [Fact]
    public void ATurnInFlight_IsWorking()
    {
        Assert.Equal(WorkingVerdicts.Working, Decide(inFlight: true));
    }

    /// <summary>
    /// QUEUED IS NOT WORKING. On 2026-09-10 the owner read "still at it" for 29 minutes about a
    /// supervisor whose turn was admitted and waiting for a slot behind five implementer turns — the
    /// in-flight table alone cannot tell the two apart, so the decider is told, and says so.
    /// </summary>
    [Fact]
    public void ATurnWaitingForASlot_IsQueued_NotWorking()
    {
        var verdict = Decide(inFlight: true, queued: true, lastTurnEndedUtc: NOW.AddSeconds(-10));

        Assert.Equal(WorkingVerdicts.Queued, verdict);
        Assert.NotEqual(WorkingVerdicts.Working, verdict);
    }

    /// <summary>A "queued" flag with no admitted turn is a contradiction the decider must not trust.</summary>
    [Fact]
    public void QueuedWithoutATurnInFlight_IsNotQueued()
    {
        Assert.NotEqual(WorkingVerdicts.Queued, Decide(inFlight: false, queued: true, lastTurnEndedUtc: NOW.AddMinutes(-20)));
    }

    /// <summary>The guards ask "is its turn occupied": a queued member cannot read a nudge any more than a running one.</summary>
    [Theory]
    [InlineData(WorkingVerdicts.Working, true)]
    [InlineData(WorkingVerdicts.Queued, true)]
    [InlineData(WorkingVerdicts.Idle, false)]
    [InlineData(WorkingVerdicts.Unknown, false)]
    public void WorkingAndQueued_AreBusy_TheOthersAreNot(WorkingVerdicts verdict, bool expected)
    {
        Assert.Equal(expected, MemberWorking_Decider.Is_Busy(verdict));
    }

    /// <summary>The dispatcher's map is empty in the gap between two turns; the member is not idle there.</summary>
    [Fact]
    public void ATurnThatEndedMomentsAgo_IsStillWorking()
    {
        Assert.Equal(WorkingVerdicts.Working, Decide(lastTurnEndedUtc: NOW.AddSeconds(-30)));
    }

    [Fact]
    public void TheGraceWindow_IsInclusiveAtItsEdge()
    {
        Assert.Equal(
            WorkingVerdicts.Working,
            Decide(lastTurnEndedUtc: NOW.AddSeconds(-MemberWorking_Decider.RECENTLY_WORKED_SECONDS)));
    }

    /// <summary>
    /// THE ONLY CASE THE APP MAY CALL IDLE: registered, nothing running, nothing recent. Everything
    /// else is either working or unknown.
    /// </summary>
    [Fact]
    public void RegisteredWithNothingRunningAndNothingRecent_IsIdle()
    {
        var verdict = Decide(lastTurnEndedUtc: NOW.AddMinutes(-20));

        Assert.Equal(WorkingVerdicts.Idle, verdict);
        Assert.True(MemberWorking_Decider.Safe_ToDisturb(verdict));
    }

    /// <summary>
    /// THE REGRESSION THAT MATTERS. An unregistered member is a terminal-run one, about which this
    /// decider knows nothing; answering "idle" here would rebuild the same fiction one layer up.
    /// </summary>
    [Fact]
    public void AnUnregisteredMember_IsUnknown_NeverIdle()
    {
        var verdict = Decide(registered: false);

        Assert.Equal(WorkingVerdicts.Unknown, verdict);
        Assert.NotEqual(WorkingVerdicts.Idle, verdict);
    }

    /// <summary>A registered session that has not completed a turn yet has said nothing either way.</summary>
    [Fact]
    public void RegisteredButNoTurnEverCompleted_IsUnknown()
    {
        Assert.Equal(WorkingVerdicts.Unknown, Decide(lastTurnEndedUtc: null));
    }

    /// <summary>In flight outranks everything: it is the app's own direct knowledge.</summary>
    [Theory]
    [InlineData(true, null)]
    [InlineData(true, -20)]
    [InlineData(true, -6000)]
    public void InFlight_BeatsEveryTimestamp(bool inFlight, int? endedMinutesAgo)
    {
        var ended = endedMinutesAgo == null ? (DateTime?)null : NOW.AddMinutes(endedMinutesAgo.Value);

        Assert.Equal(WorkingVerdicts.Working, Decide(inFlight: inFlight, lastTurnEndedUtc: ended));
    }

    /// <summary>
    /// ACTING NEEDS KNOWLEDGE; SPARING DOES NOT. Working and Unknown both forbid disturbing the
    /// member — that asymmetry is the whole lesson of the 19 false ORPHANED events.
    /// </summary>
    [Theory]
    [InlineData(WorkingVerdicts.Working, false)]
    [InlineData(WorkingVerdicts.Queued, false)]
    [InlineData(WorkingVerdicts.Unknown, false)]
    [InlineData(WorkingVerdicts.Idle, true)]
    public void OnlyAPositiveIdle_IsSafeToDisturb(WorkingVerdicts verdict, bool expected)
    {
        Assert.Equal(expected, MemberWorking_Decider.Safe_ToDisturb(verdict));
    }

    /// <summary>
    /// A SURFACE MUST BE ABLE TO SAY NOTHING. Rendering Unknown as "idle" is precisely what told the
    /// owner a working member was resting, so the describer returns null and the caller omits the
    /// field rather than inventing one.
    /// </summary>
    [Fact]
    public void Unknown_DescribesAsNothing()
    {
        Assert.Null(MemberWorking_Decider.Describe_OrNull(WorkingVerdicts.Unknown));
    }

    [Theory]
    [InlineData(WorkingVerdicts.Working)]
    [InlineData(WorkingVerdicts.Queued)]
    [InlineData(WorkingVerdicts.Idle)]
    public void EveryKnownVerdict_HasWords(WorkingVerdicts verdict)
    {
        Assert.False(string.IsNullOrWhiteSpace(MemberWorking_Decider.Describe_OrNull(verdict)));
    }

    /// <summary>The words for a queued turn must not be the words for a working one — that equivalence was the defect.</summary>
    [Fact]
    public void Queued_IsNotDescribedAsWorking()
    {
        var words = MemberWorking_Decider.Describe_OrNull(WorkingVerdicts.Queued)!;

        Assert.NotEqual(MemberWorking_Decider.Describe_OrNull(WorkingVerdicts.Working), words);
        Assert.Contains("queued", words);
        Assert.DoesNotContain("working", words);
    }

    /// <summary>
    /// The grace window must stay far under the nudge window (8 minutes), or a member that genuinely
    /// stopped would be described as working for long enough to matter.
    /// </summary>
    [Fact]
    public void TheGraceWindow_StaysWellUnderTheNudgeWindow()
    {
        Assert.True(MemberWorking_Decider.RECENTLY_WORKED_SECONDS < 8 * 60);
    }
}
