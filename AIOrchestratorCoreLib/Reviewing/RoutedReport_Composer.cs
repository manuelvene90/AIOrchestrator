using AIOrchestratorCoreLib.Channels;
using AIOrchestratorCoreLib.Channels.ChannelEntry;

namespace AIOrchestratorCoreLib.Reviewing;

/// <summary>
/// THE RELAY ENTRY: THE APP COPIES, IT NEVER COMPOSES.
///
/// <para>
/// WHAT THIS WRITES. Two verbatim quotations — the supervisor's brief, out of the contract, and the
/// implementer's report, out of its own channel — plus a <c>git diff</c> command built from two
/// commit ids, and four fixed labels that say which is which. Nothing here is ABOUT the work: no
/// summary, no re-ordering, no re-wording, no count of what was fixed. Every word the app invented in
/// this entry is a word the reviewer would read as if its supervisor had written it, which is why the
/// invented half is held to navigation and provenance and nothing else.
/// </para>
/// <para>
/// WHY THE FRAME IS INVARIANT. The labels do not change with the content — the same frame carries a
/// one-line report and a forty-line one. That is the mechanical form of "never composes", and it is
/// what <c>RoutedReportComposerTests.TheFrameIsTheSameWhateverTheReportSays</c> pins: the day the app
/// writes one sentence of its own about the fix, it has become a participant in the review, and that
/// test goes red.
/// </para>
/// <para>
/// WHAT IT DELIBERATELY DOES NOT CARRY: the branch, the original brief, the earlier rounds. The
/// reviewer's own skill fixes the scope of a re-review at the DELTA and the earlier findings, and an
/// entry that widened it would re-open the whole-branch re-read the one-wake-model spec exists to
/// close.
/// </para>
/// </summary>
public static class RoutedReport_Composer
{
    /// <summary>
    /// HOW MUCH OF THE IMPLEMENTER'S REPORT IS CARRIED. Past this the tail is cut and the cut is
    /// STATED — a reviewer reasoning from half a claim it believes is whole is the worst shape a
    /// wrong answer can take, and this repo's history says a silent hole beats nothing and loses to a
    /// refusal. Whoever needs the rest asks its supervisor, which costs one wake-up and is the right
    /// price: that is a question only the supervisor can answer.
    ///
    /// <para>
    /// CHARACTERS, AND THE NAME SAYS SO. The plan called this <c>MAXIMUM_REPORT_BYTES</c> while its
    /// own truncation line counted characters; the cut is <see cref="string"/> arithmetic, and a byte
    /// cut would split a multi-byte rune and hand the reviewer mojibake at the seam. One measure, one
    /// name.
    /// </para>
    /// </summary>
    public const int MAXIMUM_REPORT_CHARACTERS = 8192;

    /// <summary>
    /// The subject and body of the relay. The subject carries <see cref="RoutedReport_Tag"/> and is
    /// passed to <c>ChannelAppender.Append_AppEntry</c> with <c>AppEntryAudiences.Agent</c>, which
    /// applies the audience tag around it — agent-facing, because the owner cannot act on one
    /// session's brief to another (CLAUDE.md decision 15).
    /// </summary>
    /// <param name="contract">a ROUTED contract — the head commit is the right side of the delta</param>
    /// <param name="report">the entry the matcher chose, carried on <c>FixReportMatch.Report</c></param>
    public static (string Subject, string Body) Compose(IRerouteContract contract, IChannelEntry report)
    {
        // A DECLARED CONTRACT HAS NO HEAD COMMIT, and `git diff abc1234..` is a command that reads as
        // a delta and is not one. This is a caller fault, not channel input: the matcher's refusals
        // are the designed fail-open, and composing from a half-built contract is the one thing that
        // would send the reviewer a diff nobody wrote. Same line the factory draws.
        if (contract.State != RerouteStates.Routed || string.IsNullOrWhiteSpace(contract.HeadCommit))
            throw new ArgumentException($"A relay is composed from a ROUTED contract; {contract.Id} is {contract.State} and names no head commit.", nameof(contract));

        var body =
            "RE-REVIEW, routed by the app on the contract your supervisor declared. You have not been briefed by a member: members never write in each other's channels, and this entry is the app's.\n\n"
            + $"DELTA — review this and the findings below, nothing else:\n    git diff {contract.BaseCommit}..{contract.HeadCommit}\n\n"
            + $"YOUR SUPERVISOR'S BRIEF, verbatim:\n{contract.Brief}\n\n"
            + $"WHAT {contract.ImplementerId} REPORTED, verbatim:\n{report.Subject}\n{Cap(report.Body)}\n\n"
            + "Report in THIS channel as usual. Your findings are input to your supervisor's verdict, not a verdict.";

        return (RoutedReport_Tag.Apply($"re-review — {contract.ImplementerId}'s fix for {contract.ReviewerId}"), body);
    }

    /// <summary>
    /// The report unchanged under the limit; otherwise its FIRST <see cref="MAXIMUM_REPORT_CHARACTERS"/>
    /// characters and a line naming how many were dropped. The front is what is kept because a report
    /// leads with what it did — and because a cut that kept the tail would still say TRUNCATED, which
    /// is a fault no assertion on that word alone could tell from this one.
    /// </summary>
    static string Cap(string reportBody)
    {
        if (reportBody.Length <= MAXIMUM_REPORT_CHARACTERS)
            return reportBody;

        var omitted = reportBody.Length - MAXIMUM_REPORT_CHARACTERS;

        return reportBody[..MAXIMUM_REPORT_CHARACTERS]
            + $"\n— TRUNCATED, {omitted} characters omitted. Ask your supervisor if you need the rest. —";
    }
}
