namespace AIOrchestratorCoreLib.Planning.PlanBackend;

/// <summary>
/// One thing the owner asked for, approved upstream and not yet in this orchestration's plan.
///
/// <para>
/// <paramref name="Words"/> IS SEPARATE FROM <paramref name="Title"/> ON PURPOSE. The OWNER REQUESTS
/// table's whole rule is "their words, not your restatement" — a paraphrase is where a request slips,
/// because the reader recognises their own summary and moves on. The title is the short handle that
/// becomes the ledger line; the words are what the owner actually wrote, and they go in the table.
/// When an upstream system has only one of the two, pass it as both.
/// </para>
/// </summary>
/// <param name="RequestId">Stable upstream identifier. The app's idempotence key — never re-used, never null.</param>
/// <param name="Title">Short handle. Becomes the ledger line's text, and therefore the row reference.</param>
/// <param name="Words">What the owner wrote, verbatim, for the OWNER REQUESTS table.</param>
public sealed record ApprovedPlanRequest(string RequestId, string Title, string Words);
