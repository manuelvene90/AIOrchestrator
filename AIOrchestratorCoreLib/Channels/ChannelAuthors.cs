namespace AIOrchestratorCoreLib.Channels;

/// <summary>
/// Author of a channel entry, parsed from the '## [n] FROM &lt;author&gt; — date — subject' header.
/// Unknown covers malformed or future author words: the entry is still carried and mirrored,
/// it just gets a generic direction tag.
/// </summary>
public enum ChannelAuthors
{
    Supervisor,
    Implementer,
    Owner,

    /// <summary>The orchestrator app itself (request confirmations/failures on the general channel).</summary>
    App,

    /// <summary>The orchestration's press-secretary session: narrates the supervisor's activity to the owner, never works.</summary>
    Communicator,

    /// <summary>A read-only review session (rev-n). A MEMBER like an implementer — see Is_Member.</summary>
    Reviewer,

    /// <summary>The single session of a BASIC orchestration, talking straight to the owner.</summary>
    Solo,

    Unknown,
}

public static class ChannelAuthor_Kinds
{
    /// <summary>
    /// True for the authors that are MEMBERS of an orchestration — the sessions that own a spoke
    /// channel. Everything that reasons about "the member spoke last" must use this rather than
    /// comparing to Implementer: reviewers were invisible to the mirror, to state resolution and
    /// to the idle detector for exactly that reason, and the idle detector then nudged a reviewer
    /// as if its own entry were unread traffic.
    /// </summary>
    public static bool Is_Member(ChannelAuthors author)
    {
        return author == ChannelAuthors.Implementer
            || author == ChannelAuthors.Reviewer
            || author == ChannelAuthors.Solo;
    }

    /// <summary>
    /// Every author that is a SESSION of this orchestration — everyone except the owner, the app,
    /// and an author word we do not recognise.
    ///
    /// Distinct from <see cref="Is_Member"/> on purpose: a supervisor is not a member (it owns no
    /// spoke) but it is very much a session, and a surface asking "what did this orchestration last
    /// SAY" wants both. Used by the topic status line's `last ·` field, which showed the owner their
    /// OWN messages back — every one of which carries the subject "via Telegram", because that is
    /// what the bridge stamps on inbound traffic (owner's call, 2026-08-19: "only the session's own
    /// entries, never yours").
    ///
    /// A POSITIVE LIST, so <see cref="ChannelAuthors.Unknown"/> is excluded: a malformed or
    /// future author word is not something we can claim is a session. The cost is soft and stated —
    /// a genuinely new session kind shows an older entry in that field until it is added here —
    /// which is the safe direction, since the alternative is putting an unidentified author's words
    /// in front of the owner labelled as their orchestration speaking.
    /// </summary>
    public static bool Is_Session(ChannelAuthors author)
    {
        return author == ChannelAuthors.Supervisor
            || author == ChannelAuthors.Communicator
            || Is_Member(author);
    }

    /// <summary>
    /// Authors whose messages the OWNER is expected to answer. Used by the quiet/away detector: in a
    /// basic orchestration the solo session is the only voice, so silence after it must count the
    /// same way a supervisor's does.
    /// </summary>
    public static bool Speaks_ToOwner(ChannelAuthors author)
    {
        return author == ChannelAuthors.Supervisor || author == ChannelAuthors.Solo;
    }
}
