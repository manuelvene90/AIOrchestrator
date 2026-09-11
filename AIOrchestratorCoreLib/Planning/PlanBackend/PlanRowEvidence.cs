namespace AIOrchestratorCoreLib.Planning.PlanBackend;

/// <summary>
/// What the app can honestly say about a ledger line reaching <c>[x]</c>.
///
/// <para>
/// IT IS AN OBSERVATION, NOT A PROOF, and the wording of every field says so. The app did not watch
/// the work happen: it read PLAN.md, saw a marker change, and looked at the conversation that was
/// live at that moment. That is worth carrying upstream — a row closed with the channel entry that
/// accompanied it can be checked by a person — and it is not a verification. The distinction is the
/// same one the ledger itself makes: <c>[x]</c> means finished and evidenced, never reviewed.
/// </para>
/// </summary>
/// <param name="ObservedUtc">When the app BUILT this evidence, which is when it noticed — up to a pass later than the marker actually changed, and never when the work finished.</param>
/// <param name="ChannelEntryRef">The conversation entry live at that moment, e.g. "owner-channel #84 FROM supervisor", or null when the channel could not be read.</param>
/// <param name="Excerpt">That entry's subject line, shortened. Null when there is no entry.</param>
public sealed record PlanRowEvidence(DateTime ObservedUtc, string? ChannelEntryRef, string? Excerpt);
