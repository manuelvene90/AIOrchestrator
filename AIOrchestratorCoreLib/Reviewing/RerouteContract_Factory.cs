namespace AIOrchestratorCoreLib.Reviewing;

/// <summary>
/// The ONLY two ways a contract comes into being, and the reason the second one exists.
///
/// <para>
/// <see cref="CreateFrom_Routed"/> copies every field of the declaration rather than taking them
/// again from a caller. The relay entry is made of the base commit and the supervisor's brief, so a
/// transition that let a caller restate them is a transition that can lose them — and losing them is
/// silent: the reviewer gets a relay with an empty brief and re-derives the scope from the diff,
/// which is exactly the independence-eroding guesswork this feature exists to avoid.
/// </para>
/// </summary>
public static class RerouteContract_Factory
{
    public static IRerouteContract Create_Declared(
        string id,
        string orchId,
        string implementerId,
        string reviewerId,
        string baseCommit,
        string brief,
        DateTime declaredUtc)
    {
        // EVERY IDENTIFIER IS REQUIRED. An empty member id builds a channel path out of nothing and a
        // missing base commit makes a delta the reviewer cannot read — both would arm a hold that can
        // never be satisfied, which is this feature's one silent failure.
        Require(id, nameof(id));
        Require(orchId, nameof(orchId));
        Require(implementerId, nameof(implementerId));
        Require(reviewerId, nameof(reviewerId));
        Require(baseCommit, nameof(baseCommit));

        // The BRIEF is not required: a supervisor may declare a contract and say nothing more than
        // the line itself, and an empty brief still routes a correct delta.
        return new RerouteContractModel(
            id, orchId, implementerId, reviewerId, baseCommit, brief ?? string.Empty,
            declaredUtc.ToUniversalTime(), RerouteStates.Declared,
            reportIdentity: null, headCommit: null, routedUtc: null, relayIdentity: null);
    }

    /// <param name="relayIdentity">
    /// the digest of the relay entry, read back off the reviewer's channel AFTER the append landed —
    /// null when the append has not happened yet, or when the entry could not be found again. It is
    /// the last argument and optional for exactly that ordering: the relay is composed from a ROUTED
    /// contract, so the contract has to exist before the entry it names does.
    /// </param>
    public static IRerouteContract CreateFrom_Routed(
        IRerouteContract declared,
        string reportIdentity,
        string headCommit,
        DateTime routedUtc,
        string? relayIdentity = null)
    {
        Require(reportIdentity, nameof(reportIdentity));
        Require(headCommit, nameof(headCommit));

        return new RerouteContractModel(
            declared.Id,
            declared.OrchId,
            declared.ImplementerId,
            declared.ReviewerId,
            declared.BaseCommit,
            declared.Brief,
            declared.DeclaredUtc,
            RerouteStates.Routed,
            reportIdentity,
            headCommit,
            routedUtc.ToUniversalTime(),
            relayIdentity);
    }

    static void Require(string value, string name)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException($"A re-review contract needs a {name} — an empty one routes nothing and says nothing.", name);
    }
}
