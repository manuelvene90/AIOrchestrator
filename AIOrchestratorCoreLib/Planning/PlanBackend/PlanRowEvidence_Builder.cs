using AIOrchestratorCoreLib.Channels.ChannelEntry;

namespace AIOrchestratorCoreLib.Planning.PlanBackend;

/// <summary>
/// Turns the orchestration's conversation into the one line of evidence that travels upstream with a
/// closed row.
///
/// <para>
/// IT IS A FORMATTER WITH AN EXTERNAL CONTRACT, which is why it is not left inside the engine: the
/// string it produces is read by software outside this repository, and it was written in
/// <c>internal sealed</c> code that no test can reach while <see cref="PlanRowEvidence"/> quoted the
/// same format in its own docstring — one format, two copies, neither pinned. Same argument as
/// <see cref="PlanLedgerRows_Builder"/>: the untestability was a placement choice, not a constraint.
/// </para>
/// </summary>
public static class PlanRowEvidence_Builder
{
    /// <summary>The conversation entry that was live when the app saw the marker change.</summary>
    public static PlanRowEvidence Build(IReadOnlyList<IChannelEntry> ownerChannelEntries, DateTime observedUtc)
    {
        if (ownerChannelEntries.Count == 0)
            return new PlanRowEvidence(observedUtc, null, null);

        var last = ownerChannelEntries[^1];

        return new PlanRowEvidence(observedUtc, Describe_Entry(last), last.Subject);
    }

    /// <summary>
    /// "owner-channel #84 FROM supervisor" — the reference a person can go and read, spelled the way
    /// the channel file itself spells it (<see cref="Channels.ChannelAppender"/> writes the author word
    /// in lower case), so the string can be searched for in the file it names.
    /// </summary>
    public static string Describe_Entry(IChannelEntry entry)
    {
        return $"owner-channel #{entry.Index} FROM {entry.Author.ToString().ToLowerInvariant()}";
    }
}
