namespace AIOrchestratorCoreLib.Reviewing;

/// <summary>
/// How far along one re-review contract is. Two states and no more: the supervisor has ASKED for a
/// relay, or the relay has been WRITTEN. There is deliberately no "satisfied"/"answered" state — the
/// app never judges the re-review, and what happens after the relay is the supervisor's, at the
/// re-verdict.
/// </summary>
public enum RerouteStates
{
    /// <summary>The supervisor declared it; no fix report has satisfied it yet.</summary>
    Declared,

    /// <summary>The fix report arrived and the relay was written into the reviewer's channel.</summary>
    Routed,
}

/// <summary>
/// ONE RE-REVIEW CONTRACT: the supervisor's standing instruction that when a named implementer
/// declares a fix on a commit, the app — not the supervisor — hands the delta and the supervisor's
/// own brief to a named reviewer.
///
/// <para>
/// IT CARRIES NO JUDGEMENT AND CANNOT. Every field is either an identifier the app matches
/// mechanically or the supervisor's words carried verbatim. Nothing here says whether a fix is good;
/// that sentence is the supervisor's and is written at the re-verdict, after the reviewer answers.
/// </para>
/// <para>
/// WHY IT IS A RECORD ON DISK AND NOT A QUESTION ASKED OF THE CHANNELS. "Have I already routed this
/// one?" read off a live channel file is CLAUDE.md decision 13's exact trap:
/// <c>Channel_Compactor</c> archives from the front, so the live file is not stable over time and an
/// answer derived from it silently changes. The contract is small, app-written and app-read, and
/// lives beside <c>session.json</c>.
/// </para>
/// </summary>
public interface IRerouteContract
{
    /// <summary>
    /// The identity of the SUPERVISOR ENTRY that declared it — <c>ChannelEntry_Digest</c>, never the
    /// <c>[n]</c> in the header, which is agent-written and has duplicated in production (CLAUDE.md
    /// decision 12). It is also how the matcher knows which entries came AFTER the declaration.
    /// </summary>
    string Id { get; }

    string OrchId { get; }

    /// <summary>Whose fix report satisfies it. One open contract per implementer, never a search.</summary>
    string ImplementerId { get; }

    /// <summary>Who receives the relay. The app writes into this member's own channel, signed <c>app</c>.</summary>
    string ReviewerId { get; }

    /// <summary>The last reviewed commit — the LEFT side of the delta the reviewer is asked to read.</summary>
    string BaseCommit { get; }

    /// <summary>
    /// The supervisor's own words, verbatim, from under the <c>REROUTE:</c> line to the end of the
    /// body. Not parsed, not summarised, not reordered — which is what makes "the app never composes
    /// a brief" literally true rather than nearly true.
    /// </summary>
    string Brief { get; }

    DateTime DeclaredUtc { get; }

    RerouteStates State { get; }

    /// <summary>The digest of the fix report that satisfied it — null while <see cref="RerouteStates.Declared"/>.</summary>
    string? ReportIdentity { get; }

    /// <summary>The RIGHT side of the delta — null while <see cref="RerouteStates.Declared"/>.</summary>
    string? HeadCommit { get; }

    /// <summary>When the relay was written — null while <see cref="RerouteStates.Declared"/>. The
    /// clock the <see cref="RerouteContract_Policy.REVIEW_CAP"/> expiry is measured from.</summary>
    DateTime? RoutedUtc { get; }

    /// <summary>
    /// The identity of the RELAY ENTRY the app wrote into the reviewer's channel — the anchor for the
    /// one question the ordinary exit asks: "has the reviewer filed anything of its OWN since?".
    ///
    /// <para>
    /// A DIGEST AND NOT A COUNT, for CLAUDE.md decision 13's reason: <c>Channel_Compactor</c> archives
    /// from the front, so a stored count of the reviewer's entries compared against a later live count
    /// answers "it has gone backwards" on a channel that only grew. The anchor is the entry itself,
    /// and "after it in file order" is the same rule <see cref="FixReport_Matcher"/> already uses for
    /// the declaration.
    /// </para>
    /// <para>
    /// NULLABLE EVEN WHILE <see cref="RerouteStates.Routed"/>, deliberately. It is read back off the
    /// channel after the append lands, and a read that finds nothing must not cost the app the
    /// routing it has just done; a contract that cannot name its relay simply leaves by the cap
    /// instead of by the reviewer's answer, which is the same fail-open the matcher's refusals are.
    /// </para>
    /// </summary>
    string? RelayIdentity { get; }
}
