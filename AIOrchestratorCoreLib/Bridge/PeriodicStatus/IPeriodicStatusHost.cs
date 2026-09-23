using AIOrchestratorCoreLib.Planning;
using AIOrchestratorCoreLib.Planning.PlanProgress;
using AIOrchestratorCoreLib.Sessions.OrchestrationSession;
using AIOrchestratorCoreLib.Telegram;

namespace AIOrchestratorCoreLib.Bridge.PeriodicStatus;

/// <summary>
/// WHAT THE SWEEP ASKS THE ENGINE — reads of usage files, channels and the ledger, and the one write.
/// Every member is an adapter over something the engine already had; none of them decides anything.
///
/// <para>
/// WHY A HOST AND NOT THE SWEEP LIVING IN THE ENGINE. <c>BridgeEngineModel</c> is
/// <c>internal sealed</c> with no <c>InternalsVisibleTo</c>, and before plan 03 Task 8 (2026-09-23) this
/// sweep was a method inside it — so every claim about it (the pause gate, the change gate, the order
/// of the stamp) could only be pinned by SCANNING THE SOURCE for a word, which three test files did and
/// which their own comments call the weaker claim: *"it proves the gate is present and placed, not that
/// it fires"*. Driving the engine into a slot boundary needs a half hour of wall clock. With the
/// decisions out here and the reads behind this interface, a test hands the sweep a clock and a stub
/// and watches what it posts.
/// </para>
/// </summary>
public interface IPeriodicStatusHost
{
    /// <summary>The three-line away digest — the engine's <c>Build_AwayUpdateText</c>.</summary>
    string Build_AwayDigest(IOrchestrationSession session);

    /// <summary>
    /// The member roster block the owner also gets from <c>/status</c> — ONE builder for both
    /// (<c>Build_MemberStatusText_ForSession</c>), so the answer they pull and the one the app pushes
    /// cannot disagree.
    /// </summary>
    /// <param name="previous">The figures of the last status POSTED, for the "17(+1)/30" deltas; null for none.</param>
    /// <param name="withVolatileReadings">
    /// False leaves out the readings that change by the passing of time or by the status's OWN wake —
    /// each member's "last wrote N min ago", and the context figure and working state of the supervisor
    /// and of a solo (the two sessions whose channel is the owner channel, so the two a status can
    /// wake). The no-change guard compares that version; see <see cref="PeriodicStatusSweepModel"/>.
    /// </param>
    string Build_MemberStatus(IOrchestrationSession session, PlanProgressSnapshot? previous, bool withVolatileReadings);

    /// <summary>The orchestration's PLAN.md as the ledger parser reads it, or null when there is none.</summary>
    IPlanProgress? Read_PlanProgress_OrNull(string orchId);

    /// <summary>Whether any of the orchestration's sessions did anything in the last <paramref name="minutes"/>.</summary>
    bool Has_AnySessionWorkedWithin(IOrchestrationSession session, int minutes);

    /// <summary>The <c>IMAGE:</c> line of a terminal screenshot to ride the entry, or empty.</summary>
    Task<string> Build_ScreenshotMarker_OrEmpty_Async(IOrchestrationSession session, CancellationToken cancellationToken);

    /// <summary>
    /// Appends the entry to the orchestration's OWNER channel through the engine's attention choke
    /// point — which refuses a meeting and a pause a second time — and says whether it is on disk.
    /// </summary>
    bool Post_StatusEntry(string orchId, string text, OwnerPresenceModes presence);
}
