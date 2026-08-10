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
    /// Authors whose messages the OWNER is expected to answer. Used by the quiet/away detector: in a
    /// basic orchestration the solo session is the only voice, so silence after it must count the
    /// same way a supervisor's does.
    /// </summary>
    public static bool Speaks_ToOwner(ChannelAuthors author)
    {
        return author == ChannelAuthors.Supervisor || author == ChannelAuthors.Solo;
    }
}
