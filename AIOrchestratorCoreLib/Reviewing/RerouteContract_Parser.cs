using System.Text.RegularExpressions;
using AIOrchestratorCoreLib.Channels;
using AIOrchestratorCoreLib.Channels.ChannelEntry;

namespace AIOrchestratorCoreLib.Reviewing;

/// <summary>What one entry says about a re-review contract.</summary>
public enum RerouteDeclarations
{
    /// <summary>Nothing — the overwhelmingly common case, and every refusal's answer too.</summary>
    None,

    /// <summary>A contract: a named reviewer and a base commit, with the supervisor's brief under it.</summary>
    Contract,

    /// <summary><c>REROUTE: cancel</c> — the supervisor retracts whatever is open on this channel.</summary>
    Cancel,
}

/// <summary>
/// The fields of a declaration. Empty strings rather than nulls on a <see cref="RerouteDeclarations.None"/>,
/// so a caller that ignores <c>Kind</c> builds a contract the factory refuses rather than one with a
/// plausible-looking half of a declaration in it.
/// </summary>
public readonly record struct RerouteDeclaration(RerouteDeclarations Kind, string ReviewerId, string BaseCommit, string Brief);

/// <summary>
/// READS A RE-REVIEW CONTRACT OUT OF THE SUPERVISOR'S OWN VERDICT, and reads exactly two fields.
///
/// <para>
/// The shape:
/// <code>
/// REROUTE: rev-1 from abc1234
/// Check F1 and F3 only. F2 was accepted as stated.
/// The delta is the fix — do not re-read the branch.
/// </code>
/// </para>
/// <para>
/// EVERYTHING BELOW THE LINE IS THE BRIEF, VERBATIM. It is not parsed, not summarised and not
/// reordered: the supervisor decides exactly what the reviewer reads and the app carries it. That
/// one rule removes every prose-parsing question from this feature and is what makes "the app never
/// composes a brief" literally true.
/// </para>
/// <para>
/// IT IS A TEXT READER AND SCREENS NO AUTHORS. Whether this entry's writer is allowed to declare a
/// contract is the caller's question, asked once, where the channel and the role are known.
/// </para>
/// <para>
/// DETECTION IS <see cref="Status.MemberState_Resolver.Contains_Marker"/>'S, via
/// <see cref="MarkerLine_Screen"/> — decoration, whole-token matching and the quotation exclusion all
/// come from there. Only the ARGUMENT is parsed here, and with a strict anchored regex: a half-read
/// declaration would route a round to a reviewer that does not exist or diff from a commit nobody
/// named, so anything that does not parse whole is no declaration at all.
/// </para>
/// </summary>
public static partial class RerouteContract_Parser
{
    /// <summary>
    /// <c>&lt;reviewer&gt; from &lt;sha&gt;</c>, anchored at both ends. Seven hex characters is git's
    /// own short-sha floor; forty is a whole one. <c>HEAD</c>, a branch name and a six-character
    /// prefix are all refused — a delta is read between two commits, and a moving name is not one.
    /// </summary>
    [GeneratedRegex(@"^(?<reviewer>[A-Za-z][A-Za-z0-9_-]{0,63})\s+from\s+(?<base>[0-9a-fA-F]{7,40})$", RegexOptions.ExplicitCapture)]
    private static partial Regex Argument();

    static readonly RerouteDeclaration NOTHING = new(RerouteDeclarations.None, string.Empty, string.Empty, string.Empty);

    public static RerouteDeclaration Read(IChannelEntry entry)
    {
        // NO ENTRY-LEVEL GATE ABOVE THIS LOOP, DELIBERATELY. An earlier draft asked
        // MemberState_Resolver.Contains_Marker about the whole entry first, as a fast path. It was
        // REDUNDANT — every line below is screened by that same method — and a redundant guard is a
        // line no test can falsify: removing it left the suite green, which is the signal that it was
        // asserting nothing (decision 20's rule, read from the other side). One screen, asked once per
        // line, is also the only shape in which the subject/body asymmetry cannot bite: the marker in
        // a SUBJECT would have opened the gate for an entry whose body declares nothing.
        var lines = entry.Body.Split('\n');

        for (var i = 0; i < lines.Length; i++)
        {
            var argument = MarkerLine_Screen.Read_Argument_OrNull(lines[i].TrimEnd('\r'), ChannelGrammar.REROUTE);

            if (argument == null)
                continue;

            // THE FIRST DECLARATION WINS — the one a human reads at the top, and the same rule the
            // question directives use for a repeated marker. A second one further down is left in the
            // brief, where the supervisor put it.
            if (string.Equals(argument, RerouteContract_Policy.CANCEL_WORD, StringComparison.OrdinalIgnoreCase))
                return new RerouteDeclaration(RerouteDeclarations.Cancel, string.Empty, string.Empty, string.Empty);

            var match = Argument().Match(argument);

            if (!match.Success)
                return NOTHING;

            return new RerouteDeclaration(
                RerouteDeclarations.Contract,
                match.Groups["reviewer"].Value,
                match.Groups["base"].Value.ToLowerInvariant(),
                Read_Brief(lines, i + 1));
        }

        // The marker matched the SUBJECT and no body line declares it. A subject has nothing under
        // it, so there is no brief — and a contract with no brief is a relay the supervisor did not
        // write. Fail-open: the verdict stays ordinary traffic.
        return NOTHING;
    }

    /// <summary>
    /// Everything from <paramref name="from"/> to the end of the body, trimmed of leading and
    /// trailing BLANK LINES and not otherwise touched. The blank lines go because they are an
    /// artefact of where the declaration sits; every other character is the supervisor's.
    /// </summary>
    static string Read_Brief(IReadOnlyList<string> lines, int from)
    {
        var first = from;
        var last = lines.Count - 1;

        while (first <= last && string.IsNullOrWhiteSpace(lines[first]))
            first++;

        while (last >= first && string.IsNullOrWhiteSpace(lines[last]))
            last--;

        if (first > last)
            return string.Empty;

        return string.Join('\n', lines.Skip(first).Take(last - first + 1).Select(line => line.TrimEnd('\r')));
    }
}
