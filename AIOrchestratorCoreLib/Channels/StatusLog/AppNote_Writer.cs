using AIOrchestratorCoreLib.Running.PrintSessionState;
using AIOrchestratorCoreLib.Running.RoleRunnerConfig;

namespace AIOrchestratorCoreLib.Channels.StatusLog;

/// <summary>
/// CHANNEL OR LOG — ONE DECISION, ONE PLACE. Every site that writes a kind of
/// <see cref="AppNoteKinds"/> calls this instead of <see cref="ChannelAppender.Append_AppEntry"/>,
/// and nothing else about the site changes: same subject, same body, same <c>bool</c> meaning "an
/// entry is on disk".
///
/// <para>
/// AN OWNER-FACING NOTE NEVER MOVES, and this is the whole of the protection the owner's phone gets.
/// The mirror reads entries back out of the CHANNEL FILE — it never sees the call that wrote one
/// (<see cref="AppEntryAudience_Tag"/> exists because of that) — so an entry written to the log is an
/// entry the owner will never be shown. The audience screen here is therefore not a nicety: it is the
/// reason the 2026-09-15 plan can claim that nothing the owner sees today disappears, and it claims
/// it for all 78 write sites at once instead of asking the question 78 times.
/// </para>
/// <para>
/// THE SINK IS THE POLICY'S, NOT THE CONFIG'S. <see cref="BookkeepingSink_Policy"/> refuses the log
/// for a session that is handed no state pack and would never read the note — a terminal session on
/// the fingerprint watcher, and a member the dispatcher has let go while its role's config still says
/// <c>print</c>. Asking the config directly here would put that refusal at every call site.
/// </para>
/// <para>
/// IT IS NOT ITSELF A WAKER — it writes what its caller decided to write and decides nothing about
/// whether to speak — BUT IT IS ON THE WAKER REGISTER THROUGH ITS CALLERS, which is not the same
/// thing and was originally written here as if it were (corrected 2026-09-15, plan 02 task 12). The
/// engine reaches this through two adapters. <c>Route_SupervisorNote</c> sits inside
/// <c>Append_SupervisorAttention_UnlessMeeting</c>, below its <c>Is_Paused</c> screen, so it is
/// covered where every other piece of supervisor-facing traffic is. <c>Route_ChannelNote</c> had no
/// screen anywhere on its own path: the five coaching sites were unreachable in a paused
/// orchestration only because pause resolves to Deferred, Deferred freezes the channel's offsets and
/// the tailer then drops it from the tick — a delivery-mode screen two thousand lines away, standing
/// in for a pause screen that nobody had written. It asks for itself now, and
/// <c>PauseGatesEveryWakerScanTests</c> carries the row.
/// </para>
/// </summary>
public static class AppNote_Writer
{
    /// <summary>
    /// Returns whether the note landed — false means the channel stayed locked for the whole budget,
    /// or the log could not be written. Callers that record a memo on the strength of a note MUST
    /// honour it, for the reason each of them already states at its own append. A note that could not
    /// reach the log does NOT fall back to the channel: a silent reappearance in the boot read is the
    /// thing this series exists to end, and the false is the whole of the report.
    ///
    /// <para>
    /// <paramref name="state"/> is the session's own <c>print-session.json</c>, read with
    /// <c>PrintSessionState_Store.Read_OrNull(PrintSessionState_Store.Get_StateFile(...))</c>, or null
    /// when the app has no state file for it. Null REFUSES the log, in the policy — see there for why
    /// "I could not find out" is not "it is safe".
    /// </para>
    /// </summary>
    public static bool Write(
        string channelFilePath,
        string statusLogFilePath,
        IRoleRunnerConfig roleConfig,
        IPrintSessionState? state,
        AppNoteKinds kind,
        AppEntryAudiences audience,
        string subject,
        string body,
        DateTime nowLocal)
    {
        // The kind is not consulted for WHERE — every kind in the enum is movable, by construction —
        // but it is required so that routing a site is a named decision. It is carried into the log
        // by nothing and into the channel by nothing: the entry's text is identical either way, which
        // is what makes the switch reversible with no migration.
        _ = kind;

        if (audience == AppEntryAudiences.Owner || BookkeepingSink_Policy.Resolve(roleConfig, state) == BookkeepingSinks.Channel)
            return ChannelAppender.Append_AppEntry(channelFilePath, audience, subject, body, nowLocal);

        return StatusLog_Store.Append(statusLogFilePath, subject, body, nowLocal);
    }
}
