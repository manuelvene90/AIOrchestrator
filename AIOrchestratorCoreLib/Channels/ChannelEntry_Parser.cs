using System.Text.RegularExpressions;
using AIOrchestratorCoreLib.Channels.ChannelEntry;

namespace AIOrchestratorCoreLib.Channels;

/// <summary>
/// Parses append-only channel text into entries. Header format:
/// '## [n] FROM &lt;author&gt; — &lt;date&gt; — &lt;subject&gt;'.
/// The em-dash also appears inside subjects, so the header is split on the FIRST two em-dashes only.
/// Text before the first header (file seed/preamble) is ignored.
/// </summary>
public static partial class ChannelEntry_Parser
{
    /// <summary>
    /// THE HEADER SHAPE, named so it can be COMPARED with the grammar the tool writes from.
    ///
    /// <para>
    /// It cannot be read from <see cref="ChannelGrammar"/> at runtime: <c>[GeneratedRegex]</c> needs a
    /// compile-time constant, and the source-generated matcher is why this parse is cheap enough to
    /// run on every tick. So the pattern stays a literal here — and
    /// <c>ChannelGrammarTests.TheGrammarsHeaderPatternIsTheOneTheParserCompiles</c> asserts it equals
    /// the grammar's, which is the cheapest thing that cannot silently rot. Two spellings of a header
    /// is the drift that produced four header variants on 2026-08-08.
    /// </para>
    /// </summary>
    public const string HEADER_PATTERN = @"^##\s*\[(\d+)\]\s*FROM\s+(\S+)\s*(.*)$";

    [GeneratedRegex(HEADER_PATTERN, RegexOptions.Compiled)]
    private static partial Regex Header_Regex();

    const string EM_DASH = "—";

    /// <summary>
    /// The stamp the tool writes (<c>kit/grammar/channel-grammar.json</c>: <c>yyyy-MM-dd HH:mm</c>),
    /// recognised by SHAPE so a one-field header can be told from a dated one. Deliberately lenient
    /// about the time half — older entries carry the date alone.
    /// </summary>
    [GeneratedRegex(@"^\d{4}-\d{2}-\d{2}([ T]\d{2}:\d{2}(:\d{2})?)?$", RegexOptions.Compiled)]
    private static partial Regex Stamp_Regex();

    public static IReadOnlyList<IChannelEntry> Parse_All(string channelText)
    {
        List<IChannelEntry> entries = [];

        if (string.IsNullOrEmpty(channelText))
            return entries;

        var lines = channelText.Split('\n');
        var quoted = ChannelFence_Screen.Map_QuotedLines(lines);
        List<string> currentLines = [];
        Match? currentHeader = null;

        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i].TrimEnd('\r');
            var headerMatch = quoted[i] ? Match.Empty : Header_Regex().Match(line);

            if (headerMatch.Success)
            {
                Add_Entry_IfUsable(entries, currentHeader, currentLines);

                currentHeader = headerMatch;
                currentLines = [line];
            }
            else if (currentHeader != null)
            {
                currentLines.Add(line);
            }
        }

        Add_Entry_IfUsable(entries, currentHeader, currentLines);

        return entries;
    }

    /// <summary>
    /// Adds the entry unless its header carries an index this system cannot use — see
    /// <see cref="Read_Index_OrNull"/>. The entry's lines are DROPPED rather than folded into the
    /// previous entry: an unusable header is not a continuation of the message above it, and merging
    /// would quietly put one agent's text inside another's.
    /// </summary>
    static void Add_Entry_IfUsable(List<IChannelEntry> entries, Match? header, IReadOnlyList<string> entryLines)
    {
        if (header == null || Read_Index_OrNull(header) == null)
            return;

        entries.Add(Build_Entry(header, entryLines));
    }

    /// <summary>
    /// THE INDEX, OR NULL WHEN IT CANNOT BE USED — never a throw.
    ///
    /// <para>
    /// WHY. The pattern captures an unbounded <c>(\d+)</c>, so <c>## [99999999999999999999]</c> threw
    /// <c>OverflowException</c> out of <c>int.Parse</c>, and <c>## [0]</c> threw
    /// <c>ArgumentException</c> out of <see cref="ChannelEntry_Factory"/>. Either throw landed inside
    /// the tailer AFTER the read offset had advanced and BEFORE <c>Pending</c> was cleared, so the
    /// same throw recurred every two seconds for ever: that channel never mirrored again, its pending
    /// buffer grew without bound, and <c>ChannelAppender.Get_NextIndex</c> hit the same throw inside
    /// the write lock — so nothing could append to it either, not even the app's own error report
    /// about it. One bad character killed a channel permanently.
    /// </para>
    /// <para>
    /// A channel header is agent-written, untrusted input (CLAUDE.md decision 12), so this is the
    /// swallow case the conventions name: skip the entry, keep the channel. The loss is VISIBLE —
    /// such a line opens no entry, so <see cref="Opens_AnEntry"/> reports it as malformed and it
    /// travels the alert path <see cref="ChannelShape_Validator.Find_MalformedHeaders"/> already
    /// owns, right down to the owner-channel alert. A dropped entry is a reported fault, never
    /// silence.
    /// </para>
    /// </summary>
    static int? Read_Index_OrNull(Match header)
    {
        return int.TryParse(header.Groups[1].Value, out var index) && index >= 1 ? index : null;
    }

    /// <summary>
    /// HOW MANY ENTRIES the text holds, WITHOUT BUILDING ANY OF THEM. Same splitting and same header
    /// pattern as <see cref="Parse_All"/> — it is that method with the entry construction removed —
    /// so the two can never answer differently about the same text.
    ///
    /// <para>
    /// IT EXISTS FOR ONE CALLER AND ONE QUESTION: <see cref="Channel_Compactor"/> asks, of every
    /// channel, on every 2-second tick, whether the file is over its threshold — and the answer is
    /// "no" for every short-lived orchestration in the system. Paying <see cref="Parse_All"/> for that
    /// no meant allocating an entry object, a line list and four substrings per entry, for every
    /// channel, thirty times a minute, to throw all of it away.
    /// </para>
    /// <para>
    /// IT IS A PRE-FILTER, NOT THE DECISION. The compactor still parses before it archives anything:
    /// what leaves the live file has to be the entries themselves, and a count is not evidence about
    /// them.
    /// </para>
    /// </summary>
    /// <remarks>
    /// <para>
    /// IT STAYS ALLOCATION-FREE FOR THE FILE THAT HAS NO FENCES, which is nearly every file and
    /// certainly every tick's common case. Fence awareness needs to know whether a delimiter further
    /// down ever CLOSES, which one forward pass cannot answer — but a file containing no delimiter at
    /// all cannot hold a quoted header, so the cheap span walk is already the exact answer there and
    /// only a file that has one pays for <see cref="ChannelFence_Screen.Map_QuotedLines"/>. Writing a
    /// second fence rule that a span could evaluate in one pass was the alternative, and it is the
    /// drift this file's own header warns about.
    /// </para>
    /// <para>
    /// IT COUNTS HEADER LINES, and since 2026-09-15 that is no longer the same as the number
    /// <see cref="Parse_All"/> returns: a header carrying an index this system cannot use opens no
    /// entry (see <see cref="Read_Index_OrNull"/>) but is still counted here. Harmless for the one
    /// caller — a pre-filter that over-counts asks the compactor to look, and the compactor parses
    /// before it archives anything. <c>CountEntriesAgreesWithTheHeaderScan</c> pins the equality that
    /// does hold.
    /// </para>
    /// </remarks>
    public static int Count_Entries(string channelText)
    {
        if (string.IsNullOrEmpty(channelText))
            return 0;

        var count = 0;
        var sawFence = false;
        var remaining = channelText.AsSpan();

        while (!remaining.IsEmpty)
        {
            var lineBreak = remaining.IndexOf('\n');
            var line = (lineBreak < 0 ? remaining : remaining[..lineBreak]).TrimEnd('\r');

            if (ChannelFence_Screen.Looks_LikeDelimiter(line))
                sawFence = true;
            else if (Header_Regex().IsMatch(line))
                count++;

            if (lineBreak < 0)
                break;

            remaining = remaining[(lineBreak + 1)..];
        }

        return sawFence ? Read_HeaderLineIndexes(channelText.Split('\n')).Count : count;
    }

    /// <summary>
    /// WHERE THE HEADER LINES ARE, fence-aware, for the callers that cut text at them rather than
    /// parse it.
    ///
    /// <para>
    /// It exists so <see cref="Tailing.ChannelTailer.ChannelTailerModel"/> does not keep its own
    /// scan: the tailer decides where an APPEND is cut, so a tailer that disagreed with the parser
    /// about what opens an entry would split one message where the parser sees none — the same class
    /// of two-copies drift that <see cref="Read_HeaderLines"/> was extracted to end.
    /// </para>
    /// </summary>
    public static IReadOnlyList<int> Read_HeaderLineIndexes(IReadOnlyList<string> lines)
    {
        List<int> indexes = [];

        var array = lines as string[] ?? [.. lines];
        var quoted = ChannelFence_Screen.Map_QuotedLines(array);

        for (var i = 0; i < array.Length; i++)
        {
            if (!quoted[i] && Header_Regex().IsMatch(array[i].TrimEnd('\r')))
                indexes.Add(i);
        }

        return indexes;
    }

    public static int Get_NextIndex(string channelText)
    {
        var entries = Parse_All(channelText);

        if (entries.Count == 0)
            return 1;

        // The HIGHEST index used, not the last one written. Entries are appended by several
        // writers and a file that has already suffered a collision is not sorted — numbering from
        // the tail then hands out an index that exists further up, turning one duplicate into a
        // run of them.
        return entries.Max(entry => entry.Index) + 1;
    }

    public static bool Is_HeaderLine(string line)
    {
        return Header_Regex().IsMatch(line.TrimEnd('\r'));
    }

    /// <summary>
    /// Whether this line actually OPENS AN ENTRY — it is header-shaped AND carries an index this
    /// system can use.
    ///
    /// <para>
    /// Distinct from <see cref="Is_HeaderLine"/>, which answers about the SHAPE alone and is what the
    /// print-runner's neutraliser wants: that one must spot a header-shaped line wherever it appears,
    /// precisely so it can defuse it. This one answers the question every shape check is really
    /// asking — "will the parser make an entry out of this?" — and the two differ exactly on
    /// <c>## [0]</c> and on an index too large for <c>int</c>, which parse as headers and become
    /// nothing. Before <see cref="Read_Index_OrNull"/> they became an exception instead, every two
    /// seconds, for ever.
    /// </para>
    /// </summary>
    public static bool Opens_AnEntry(string line)
    {
        var match = Header_Regex().Match(line.TrimEnd('\r'));

        return match.Success && Read_Index_OrNull(match) != null;
    }

    /// <summary>
    /// Every header line with WHERE it is and what index it claims — for callers that need to reason
    /// about the header lines themselves rather than about the entries they open.
    ///
    /// It exists so that <see cref="ChannelIndexSequence_Screen"/> does not carry a second copy of
    /// <c>Header_Regex</c>. A duplicated header pattern is the drift that has cost this codebase a
    /// legend, two ledger parsers and a marker list in one evening — and it would be a particularly
    /// poor place to start, since the screen's whole job is to notice header lines that should not be
    /// there.
    ///
    /// Line numbers are 1-based, matching <see cref="ChannelShape_Validator.Find_MalformedHeaders"/>,
    /// so the two report the same coordinates for the same file.
    /// </summary>
    public static IReadOnlyList<(int LineNumber, int Index, string Line)> Read_HeaderLines(string channelText)
    {
        List<(int LineNumber, int Index, string Line)> headers = [];

        if (string.IsNullOrEmpty(channelText))
            return headers;

        var lines = channelText.Split('\n');

        foreach (var i in Read_HeaderLineIndexes(lines))
        {
            var line = lines[i].TrimEnd('\r');
            var index = Read_Index_OrNull(Header_Regex().Match(line));

            // An index this system cannot use opens no entry (see Read_Index_OrNull), so reporting
            // it here would describe a header that nothing downstream honours.
            if (index != null)
                headers.Add((i + 1, index.Value, line.Trim()));
        }

        return headers;
    }

    static IChannelEntry Build_Entry(Match header, IReadOnlyList<string> entryLines)
    {
        var index = Read_Index_OrNull(header)
            ?? throw new Exception($"Build_Entry reached an unusable index '{header.Groups[1].Value}' — Add_Entry_IfUsable must filter it");
        var author = Parse_Author(header.Groups[2].Value);
        var afterAuthor = header.Groups[3].Value;

        var (dateText, subject) = Split_DateAndSubject(afterAuthor);

        var bodyLines = entryLines.Skip(1).ToList();

        // THE DECLARED TYPE COMES OUT OF THE BODY (E3 requirement 3). The tool writes it directly
        // under the header, and it is METADATA — so it must not travel on as body text: the mirror
        // would put "type: question" on the owner's phone, which is the app's bookkeeping read aloud.
        // Same reasoning as the STATE: line, which brief C strips for the same reason.
        //
        // ONLY THE FIRST NON-BLANK LINE IS CONSIDERED. A prose line beginning "type: " further down
        // is prose — the owner writes about types — and the tool always emits this one first.
        string? declaredType = null;
        var firstContentIndex = bodyLines.FindIndex(line => !string.IsNullOrWhiteSpace(line));

        if (firstContentIndex >= 0
            && bodyLines[firstContentIndex].StartsWith(ChannelGrammar.TypeLine_Prefix, StringComparison.Ordinal))
        {
            declaredType = bodyLines[firstContentIndex][ChannelGrammar.TypeLine_Prefix.Length..].Trim();
            bodyLines.RemoveAt(firstContentIndex);

            if (declaredType.Length == 0)
                declaredType = null;
        }

        var body = string.Join('\n', bodyLines).Trim('\n');

        // RawText KEEPS THE TYPE LINE, deliberately: it is the entry as written, and the audit trail
        // is the one place the app's own bookkeeping belongs. Every recogniser that reads RawText
        // matches a marker at a line start, and `type: ` is not one.
        var rawText = string.Join('\n', entryLines).Trim('\n');

        return ChannelEntry_Factory.Create(index, author, dateText, subject, body, rawText, declaredType);
    }

    /// <summary>
    /// The author word, with surrounding punctuation and markdown stripped before it is matched.
    ///
    /// The header regex captures a bare token, so `FROM **implementer**`, `FROM implementer:` and
    /// `FROM _supervisor_` all fall through to <see cref="ChannelAuthors.Unknown"/> without this.
    ///
    /// SPECULATIVE, AND SAYING SO IS THE POINT. An earlier version of this comment claimed the shape
    /// was observed live. It was not: 3,406 headers on this machine carry five distinct author words
    /// and ZERO decorated ones, and the incidents that were cited turned out to be header SHAPE
    /// defects — a missing index, two non-numeric ones — not author drift. The guard stays because it
    /// is near-harmless and the failure it prevents is severe, but it is a defence against a shape
    /// nobody has seen. A defence labelled speculative is fine; one labelled measured is how the next
    /// reader stops checking.
    ///
    /// The severity is what earns it. Window markers are read only from member-authored entries, so
    /// an entry that OPENS a window under a clean header and CLOSES it under a drifted one leaves the
    /// close invisible — and a missing close reads as still-open, forever, with the app then telling
    /// the member to append the close it already appended.
    ///
    /// Normalised HERE, where the author word is interpreted, rather than in the resolver that
    /// happened to notice: a second normaliser would be one more rule with two copies.
    /// </summary>
    static ChannelAuthors Parse_Author(string authorWord)
    {
        var normalized = authorWord.Trim().ToLowerInvariant().Trim('*', '_', '`', ':', ',', '.', ';', '(', ')', '[', ']', '"', '\'');

        return normalized switch
        {
            "supervisor" => ChannelAuthors.Supervisor,
            "implementer" => ChannelAuthors.Implementer,
            "reviewer" => ChannelAuthors.Reviewer,
            "solo" => ChannelAuthors.Solo,
            "owner" => ChannelAuthors.Owner,
            "app" => ChannelAuthors.App,
            "communicator" => ChannelAuthors.Communicator,

            // THE SUPERVISOR'S MEMBER ID, AS A CLOSED HISTORICAL RESIDUE — not an alias vocabulary.
            //
            // Between 7d6949f (2026-09-10 18:56) and 2848172 (2026-09-14 15:04) channel-append.sh's
            // derive_author preferred AIORCH_MEMBER over AIORCH_ROLE, and the supervisor's member id
            // is "sup" (SessionLaunch_Factory.SUPERVISOR_MEMBER_ID). So the tool itself signed those
            // entries: they are not typos and they did not bypass the append path — the VPS carries
            // both `owner-channel.md.self-write.sup` and `.self-write.supervisor` records, which only
            // channel-append.sh writes. Measured there 2026-09-15: 93 headers across fincanva-8 and
            // fincanva-12, the last at 14:12, none after the fixed script was installed at 17:45.
            //
            // It is read because those entries are otherwise Unknown, and an Unknown supervisor entry
            // is invisible to Brief_Finder — so a state pack rebuilt from disk for those
            // orchestrations hands a member no brief at all, silently. Reading a real identifier of
            // this system is not the same as accepting a misspelling.
            //
            // DO NOT EXTEND THIS TO OTHER MEMBER IDS. `imp-2`, `rev-1`, `solo-1` never reached a
            // header — only the supervisor's own appends went through the tool on the affected hosts
            // — and adding a second id here would turn one dated repair into an alias vocabulary with
            // no boundary. The writer is fixed; this list must not grow.
            "sup" => ChannelAuthors.Supervisor,

            _ => ChannelAuthors.Unknown,
        };
    }

    /// <summary>
    /// WHEN A HEADER CARRIES ONLY ONE FIELD, ITS SHAPE DECIDES WHICH FIELD IT IS.
    ///
    /// <para>
    /// Observed 2026-09-15: a header with a single em dash — <c>## [12] FROM supervisor — QUESTION:
    /// quale opzione?</c> — put the whole tail in the DATE and left the subject empty, because the
    /// split was positional. The mirror falls back to the subject when an entry's body is nothing but
    /// marker lines, so an empty subject reached the owner as a message that was only the speaker
    /// prefix, with a notification and no text. It happened seven times in one export.
    /// </para>
    /// <para>
    /// The template is <c>— {stamp} — {subject}</c>, and a stamp has a recognisable shape while a
    /// subject does not, so the one-field case asks the field what it looks like rather than guessing
    /// from its position. Text that is not a stamp is the SUBJECT: losing the date costs a "time on
    /// task" reading that <c>Describe_SinceStamp_OrNull</c> already refuses to guess at, while losing
    /// the subject costs the owner the message.
    /// </para>
    /// </summary>
    static (string DateText, string Subject) Split_DateAndSubject(string afterAuthor)
    {
        var firstDash = afterAuthor.IndexOf(EM_DASH, StringComparison.Ordinal);
        if (firstDash < 0)
            return (string.Empty, afterAuthor.Trim());

        var afterFirst = afterAuthor[(firstDash + EM_DASH.Length)..];
        var secondDash = afterFirst.IndexOf(EM_DASH, StringComparison.Ordinal);

        if (secondDash < 0)
        {
            var only = afterFirst.Trim();

            return Stamp_Regex().IsMatch(only) ? (only, string.Empty) : (string.Empty, only);
        }

        var dateText = afterFirst[..secondDash].Trim();
        var subject = afterFirst[(secondDash + EM_DASH.Length)..].Trim();

        return (dateText, subject);
    }
}
