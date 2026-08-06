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

    Unknown,
}
