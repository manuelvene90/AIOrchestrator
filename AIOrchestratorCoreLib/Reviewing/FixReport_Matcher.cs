using System.Text.RegularExpressions;
using AIOrchestratorCoreLib.Channels;
using AIOrchestratorCoreLib.Channels.ChannelEntry;
using AIOrchestratorCoreLib.Status;

namespace AIOrchestratorCoreLib.Reviewing;

/// <summary>
/// The answer to "has the report this contract waits for arrived?".
///
/// <para>
/// <paramref name="Report"/> is the entry itself, carried rather than left to the caller to find
/// again: the relay is made of this entry's words and its digest, and a caller that searched for it
/// a second time would be a second copy of the rule that chose it (CLAUDE.md decision 12).
/// </para>
/// </summary>
public readonly record struct FixReportMatch(bool Matched, string HeadCommit, string Refusal, IChannelEntry? Report);

/// <summary>
/// DOES THIS CHANNEL CARRY THE FIX REPORT THAT CONTRACT ASKED FOR — AND IF NOT, WHICH PREDICATE
/// FAILED.
///
/// <para>
/// THE APP NEVER DECIDES THAT A FIX IS GOOD. It decides that the report the supervisor said to wait
/// for has arrived and names a delta. Nothing here reads prose for meaning: the implementer DECLARES
/// the fix with one grammar marker and the app checks mechanical facts about that declaration. The
/// judgement stays the supervisor's, at the re-verdict.
/// </para>
/// <para>
/// EVERY REFUSAL IS A DESIGNED FAIL-OPEN, not an error: nothing is routed, the fix report stays
/// ordinary traffic, and the supervisor wakes on it exactly as it does today. The refusal words are
/// what the sweep writes to <c>orchestrator.log.jsonl</c> — decision 21: a predicate that cannot be
/// satisfied says WHICH one, because "reroute failed" is the silence again. Nothing here throws.
/// </para>
/// </summary>
public static partial class FixReport_Matcher
{
    // THE WORDS THE LOG CARRIES, BUILT FROM THE GRAMMAR rather than spelled here — `static readonly`
    // and not `const` for exactly the reason QuestionDirectives_Parser is: the marker is a value in a
    // FILE that bash and .NET both read, so it arrives at runtime. A log line that named a marker the
    // grammar no longer uses would send whoever reads it looking for the wrong word.
    public static readonly string NO_DECLARATION = $"no member entry after the contract declares {ChannelGrammar.FIXED}";
    public static readonly string PREDATES_THE_CONTRACT = $"the only {ChannelGrammar.FIXED} entry predates the contract";
    public static readonly string AMBIGUOUS_HEAD = $"two {ChannelGrammar.FIXED} lines in one report — the head commit is ambiguous";
    public static readonly string NO_COMMIT = $"the {ChannelGrammar.FIXED} line names no commit";
    public static readonly string NO_DELTA = $"the {ChannelGrammar.FIXED} commit is the base commit";
    public const string DECLARATION_NOT_IN_CHANNEL = "the declaring entry is not among the entries read";

    /// <summary>
    /// A COMMIT AND NOTHING ELSE. Seven hex characters is git's own short-sha floor and forty is a
    /// whole one; a branch name, `HEAD`, or a sentence about the branch is refused rather than
    /// guessed at, because the relay diffs between two commits and a moving name is not one.
    /// </summary>
    [GeneratedRegex(@"^[0-9a-fA-F]{7,40}$", RegexOptions.ExplicitCapture)]
    private static partial Regex Commit();

    /// <param name="entriesInFileOrder">the implementer channel's entries, as read — file order, never [n]</param>
    /// <param name="declarationIdentity">the <c>ChannelEntry_Digest</c> of the entry that declared the contract</param>
    public static FixReportMatch Find(
        IRerouteContract contract,
        IReadOnlyList<IChannelEntry> entriesInFileOrder,
        string declarationIdentity)
    {
        var declaredAt = Find_Declaration_OrMinusOne(entriesInFileOrder, declarationIdentity);

        // NOT AN INFERENCE. The live file is not stable over time — Channel_Compactor archives the
        // oldest entries — so "the declaration is gone, therefore everything here is newer" is a
        // guess, and it is also exactly what a caller handed the WRONG channel looks like.
        if (declaredAt < 0)
            return Refused(DECLARATION_NOT_IN_CHANNEL);

        IChannelEntry? report = null;

        // FILE ORDER, NEVER THE [n] IN THE HEADER, which is agent-written: two [80]s in one channel
        // is a live incident (decision 12), and an index comparison would then drop a filed report
        // with nothing anywhere saying so.
        for (var i = declaredAt + 1; i < entriesInFileOrder.Count; i++)
        {
            if (Is_FixReport(entriesInFileOrder[i]))
                report = entriesInFileOrder[i];
        }

        if (report == null)
        {
            // Two different silences, and they mean different things to whoever reads the log: a
            // round that has not reported yet, against a report filed before the contract existed —
            // which is a supervisor that declared the contract too late.
            for (var i = 0; i < declaredAt; i++)
            {
                if (Is_FixReport(entriesInFileOrder[i]))
                    return Refused(PREDATES_THE_CONTRACT);
            }

            return Refused(NO_DECLARATION);
        }

        var declarations = Read_FixedArguments(report);

        // AN AMBIGUITY IS NEVER GUESSED. Two heads named in one report is a delta the app cannot
        // choose between, and choosing wrong sends the reviewer a diff nobody wrote.
        if (declarations.Count > 1)
            return Refused(AMBIGUOUS_HEAD);

        var head = declarations.Count == 1 ? declarations[0] : string.Empty;

        if (!Commit().IsMatch(head))
            return Refused(NO_COMMIT);

        head = head.ToLowerInvariant();

        if (string.Equals(head, contract.BaseCommit, StringComparison.OrdinalIgnoreCase))
            return Refused(NO_DELTA);

        return new FixReportMatch(Matched: true, HeadCommit: head, Refusal: string.Empty, Report: report);
    }

    static FixReportMatch Refused(string refusal)
    {
        return new FixReportMatch(Matched: false, HeadCommit: string.Empty, Refusal: refusal, Report: null);
    }

    static int Find_Declaration_OrMinusOne(IReadOnlyList<IChannelEntry> entries, string declarationIdentity)
    {
        for (var i = 0; i < entries.Count; i++)
        {
            if (ChannelEntry_Digest.Compute(entries[i]) == declarationIdentity)
                return i;
        }

        return -1;
    }

    /// <summary>
    /// A MEMBER DECLARING THE MARKER. Both halves matter and neither is redundant: the supervisor
    /// writes `FIXED:` when it teaches the protocol, and the APP writes it when it quotes the report
    /// into the reviewer's channel — so the author screen is what keeps the app from satisfying its
    /// own contract. The marker screen is <see cref="MemberState_Resolver.Contains_Marker"/>'s, which
    /// has excluded quotation and mid-sentence mention since 2026-09-09.
    /// </summary>
    static bool Is_FixReport(IChannelEntry entry)
    {
        return ChannelAuthor_Kinds.Is_Member(entry.Author)
            && MemberState_Resolver.Contains_Marker(entry, ChannelGrammar.FIXED);
    }

    /// <summary>
    /// The argument of every line that DECLARES the marker — a quoted one is not one, which is what
    /// keeps an implementer quoting the brief back from making its own report ambiguous.
    /// </summary>
    static IReadOnlyList<string> Read_FixedArguments(IChannelEntry entry)
    {
        List<string> arguments = [];

        foreach (var rawLine in entry.Body.Split('\n'))
        {
            var argument = MarkerLine_Screen.Read_Argument_OrNull(rawLine.TrimEnd('\r'), ChannelGrammar.FIXED);

            if (argument != null)
                arguments.Add(argument);
        }

        return arguments;
    }
}
