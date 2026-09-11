using AIOrchestratorCoreLib.Planning.PlanProgress;

namespace AIOrchestratorCoreLib.Planning.PlanBackend;

/// <summary>What writing one approved request into a plan did, or why it did nothing.</summary>
public enum PlanRequestWriteOutcomes
{
    /// <summary>The plan gained a row, a ledger line, or both.</summary>
    Written,

    /// <summary>Everything this request needs was already in the plan — an earlier tick wrote it.</summary>
    AlreadyPresent,

    /// <summary>Nothing was written and nothing may be tracked. <c>Refusal</c> says why, in words.</summary>
    Refused,
}

/// <summary>The plan as it reads after one approved request was written into it.</summary>
/// <param name="Outcome">Written, already present, or refused.</param>
/// <param name="PlanText">The whole file's new text. Equal to the input unless the outcome is Written.</param>
/// <param name="LedgerRowRef">The ledger line's text — the handle everything downstream uses. Empty when refused.</param>
/// <param name="OwnerRequestNumber">The row number given in the OWNER REQUESTS table, or 0 when no row was added.</param>
/// <param name="Refusal">Why nothing was written, when the outcome is Refused. Null otherwise.</param>
public readonly record struct PlanRequestWrite(
    PlanRequestWriteOutcomes Outcome,
    string PlanText,
    string LedgerRowRef,
    int OwnerRequestNumber,
    string? Refusal)
{
    public bool Changed => Outcome == PlanRequestWriteOutcomes.Written;

    public bool Trackable => Outcome != PlanRequestWriteOutcomes.Refused;
}

/// <summary>
/// Writes ONE approved upstream request into a PLAN.md, exactly where a supervisor would have written
/// it by hand: a row in the <c>## OWNER REQUESTS</c> table, and a line at the end of the ledger.
///
/// <para>
/// BOTH, AND THE PAIR IS THE POINT. The table is the record of the ASK — append-only, in the owner's
/// own words, never renumbered — and it is deliberately invisible to <see cref="PlanLedger_Parser"/>,
/// so writing one cannot move the progress bar. But a request that only ever exists as a table row can
/// never be reported closed either: nothing about a prose "status" cell is machine-readable, and the
/// one closure signal this codebase has is a ledger marker turning <c>x</c>. So the ledger line is what
/// makes the round trip possible, and it is legitimate work in the denominator by construction —
/// decision 22's rule is that a ledger line must trace to an owner request, and this one traces to the
/// row written beside it in the same breath.
/// </para>
/// <para>
/// A PURE FUNCTION OVER THE FILE'S TEXT. It reads and returns strings and touches no disk, so every
/// shape a hand-written plan can be in — no OWNER REQUESTS section, a section with no table, a table
/// whose numbers skip, CRLF, a plan that is nothing but a seed — is a test rather than a field report.
/// </para>
/// <para>
/// IT REFUSES RATHER THAN GUESSES. A request whose title already names a line that is DONE or NOT
/// DOING is not written: attaching to it would make the very next tick report that request closed,
/// upstream, for work finished before the request existed. Refusing names the collision instead, which
/// a person can act on.
/// </para>
/// <para>
/// IT NEVER RENUMBERS AND NEVER DELETES. The next row number is one past the highest already there,
/// so a table whose rows were hand-edited keeps every reference other text makes to "#4".
/// </para>
/// </summary>
public static class PlanRequest_Writer
{
    /// <summary>The heading written when a plan has no OWNER REQUESTS section yet — the seed's own wording.</summary>
    public const string OWNER_REQUESTS_HEADING = "## OWNER REQUESTS — written the moment they arrive, in arrival order, never deleted";

    public const string TABLE_HEADER_ROW = "| # | when | what they asked for | status |";
    public const string TABLE_SEPARATOR_ROW = "|---|---|---|---|";

    /// <summary>
    /// How an ingested row names its upstream request — a DELIMITED token, matched whole.
    ///
    /// The first version matched the bare id anywhere in the row, which is a substring search against a
    /// line containing a row number, a clock time and the owner's own words: an id of "42" matched the
    /// timestamp "09:42". The row was then judged present and skipped while the ledger line was still
    /// written — a line in the owner's denominator with no request to trace to, which is decision 22
    /// inverted. CLOSED at both ends for the same reason one end was not enough: "(upstream:REQ-1)" is
    /// otherwise a prefix of "(upstream:REQ-11)".
    /// </summary>
    public const string UPSTREAM_TOKEN_PREFIX = "(upstream:";

    /// <summary>
    /// The status a freshly-ingested request carries. It says what is TRUE FOR THE OWNER — the thing is
    /// in the plan and nobody has started it — rather than anything about a branch, which is the table's
    /// own rule. Whoever runs the orchestration edits it from here on, and
    /// <see cref="Describe_ReportedStatus"/> replaces it when the line closes.
    /// </summary>
    public static string Describe_InitialStatus(string requestId)
    {
        return $"approved upstream {Build_Token(requestId)} — in the ledger, not started";
    }

    /// <summary>
    /// What the row says once its ledger line has closed and the backend has been told.
    ///
    /// The app writes this because the app wrote the row. Left at "not started" for ever, an ingested
    /// row answers "nobody is on this" at every check-in the supervisor is required to run over the
    /// whole table — so the app would be manufacturing permanent false alarms on the one artefact whose
    /// purpose is catching neglected requests.
    /// </summary>
    public static string Describe_ReportedStatus(string requestId)
    {
        return $"done in the ledger, reported upstream {Build_Token(requestId)}";
    }

    public static string Build_Token(string requestId)
    {
        return $"{UPSTREAM_TOKEN_PREFIX}{Escape_Cell(requestId)})";
    }

    public static PlanRequestWrite Write_Request(string planText, ApprovedPlanRequest request, DateTime whenLocal)
    {
        var source = planText ?? string.Empty;
        var ledgerRowRef = Flatten(request.Title);

        if (ledgerRowRef.Length == 0)
            return Refuse(source, $"request '{request.RequestId}' has no title, so it cannot become a ledger line");

        if (string.IsNullOrWhiteSpace(request.RequestId))
            return Refuse(source, "an approved request arrived with no id, so nothing could be tracked against it");

        var newline = Detect_Newline(source);
        List<string> lines = Split_Lines(source);

        // ALREADY THERE means an earlier tick wrote it and its state file never landed, or a supervisor
        // typed the same line. Either way the request is in the plan and a second copy of it is the one
        // damage this class can do. TOP-LEVEL ONLY, exactly as the closure check reads it: matching a
        // SUB-task here would suppress the line while the reporter — which never looks at sub-tasks —
        // waited for a closure that could not arrive.
        var existing = Find_ExistingLine_OrNull(lines, ledgerRowRef);

        if (existing is { Marker: "x" or "-" })
        {
            return Refuse(
                source,
                $"'{ledgerRowRef}' already exists in the ledger marked [{existing.Value.Marker}], so it would be reported closed for work that predates the request");
        }

        var ownerRequestNumber = 0;

        if (!Has_OwnerRequestRow(lines, request.RequestId))
            ownerRequestNumber = Append_OwnerRequestRow(lines, request, whenLocal);

        var wroteLedgerLine = existing == null;

        if (wroteLedgerLine)
            Insert_LedgerLine(lines, ledgerRowRef);

        var changed = ownerRequestNumber > 0 || wroteLedgerLine;

        return new PlanRequestWrite(
            changed ? PlanRequestWriteOutcomes.Written : PlanRequestWriteOutcomes.AlreadyPresent,
            changed ? string.Join(newline, lines) : source,
            ledgerRowRef,
            ownerRequestNumber,
            null);
    }

    /// <summary>
    /// Rewrites one ingested row's status cell. Returns the input unchanged when the row is not there —
    /// a supervisor may have removed it, and re-adding it would be this class arguing with the person
    /// who owns the file.
    /// </summary>
    public static (string PlanText, bool Changed) Set_RowStatus(string planText, string requestId, string status)
    {
        var source = planText ?? string.Empty;
        var newline = Detect_Newline(source);

        var lines = Split_Lines(source);

        var (start, end) = PlanLedger_Sections.Find_SectionRange(lines, PlanLedger_Sections.OWNER_REQUESTS_HEADING_PREFIX);

        if (start < 0)
            return (source, false);

        var token = Build_Token(requestId);

        for (var index = start; index < end; index++)
        {
            if (!Is_TableRow(lines[index]) || !lines[index].Contains(token, StringComparison.Ordinal))
                continue;

            var cells = Read_Cells(lines[index]);

            if (cells.Count < 4)
                return (source, false);

            cells[^1] = $" {status} ";

            lines[index] = $"|{string.Join('|', cells)}|";

            return (string.Join(newline, lines), true);
        }

        return (source, false);
    }

    /// <summary>
    /// One line, one space between words. A ledger line that carried a newline would split into two
    /// lines the moment it was written, and the second half would not be a task line at all.
    /// </summary>
    public static string Flatten(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return string.Empty;

        return string.Join(' ', text.Split([' ', '\t', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries));
    }

    static PlanRequestWrite Refuse(string planText, string refusal)
    {
        return new PlanRequestWrite(PlanRequestWriteOutcomes.Refused, planText, string.Empty, 0, refusal);
    }

    /// <summary>
    /// The file's own line ending. Read once, and used once — every line is carried WITHOUT its
    /// carriage return and the ending is put back by the final join, so no path can produce a file with
    /// two kinds of line break in it. Splicing the return in by hand was that bug: a plan whose last
    /// line was empty gained one LF-only blank line at the join.
    /// </summary>
    static string Detect_Newline(string text)
    {
        return text.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
    }

    static List<string> Split_Lines(string text)
    {
        return [.. text.Split('\n').Select(line => line.TrimEnd('\r'))];
    }

    /// <summary>A pipe inside a cell ends the cell. Escaped, so the owner's own words survive the table.</summary>
    static string Escape_Cell(string text)
    {
        return Flatten(text).Replace("|", "\\|");
    }

    static PlanLedgerLine? Find_ExistingLine_OrNull(IReadOnlyList<string> lines, string ledgerRowRef)
    {
        var progress = PlanLedger_Parser.Parse_OrNull(string.Join("\n", lines));

        return progress == null ? null : PlanLedger_Lines.Find_TopLevel_OrNull(progress, ledgerRowRef);
    }

    /// <summary>
    /// Keyed on the delimited upstream token this writer emits itself — never on the raw id, and never
    /// on the words, which whoever runs the orchestration is expected to edit as the status changes.
    /// </summary>
    static bool Has_OwnerRequestRow(IReadOnlyList<string> lines, string requestId)
    {
        var (start, end) = PlanLedger_Sections.Find_SectionRange(lines, PlanLedger_Sections.OWNER_REQUESTS_HEADING_PREFIX);

        if (start < 0)
            return false;

        var token = Build_Token(requestId);

        for (var index = start; index < end; index++)
        {
            if (Is_TableRow(lines[index]) && lines[index].Contains(token, StringComparison.Ordinal))
                return true;
        }

        return false;
    }

    /// <summary>Returns the row number written, and appends the section itself when the plan has none.</summary>
    static int Append_OwnerRequestRow(List<string> lines, ApprovedPlanRequest request, DateTime whenLocal)
    {
        var (start, end) = PlanLedger_Sections.Find_SectionRange(lines, PlanLedger_Sections.OWNER_REQUESTS_HEADING_PREFIX);

        if (start < 0)
        {
            Ensure_TrailingBlankLine(lines);
            lines.Add(OWNER_REQUESTS_HEADING);
            lines.Add(string.Empty);
            lines.Add(TABLE_HEADER_ROW);
            lines.Add(TABLE_SEPARATOR_ROW);
            lines.Add(Build_Row(1, request, whenLocal));

            return 1;
        }

        var lastRowIndex = -1;
        var highestNumber = 0;

        for (var index = start; index < end; index++)
        {
            if (!Is_TableRow(lines[index]))
                continue;

            lastRowIndex = index;
            highestNumber = Math.Max(highestNumber, Read_RowNumber(lines[index]));
        }

        var number = highestNumber + 1;

        if (lastRowIndex < 0)
        {
            // A section with no table yet: give it one, at the end of the section's own block.
            var insertAt = Skip_TrailingBlankLines(lines, start, end);

            lines.Insert(insertAt, TABLE_HEADER_ROW);
            lines.Insert(insertAt + 1, TABLE_SEPARATOR_ROW);
            lines.Insert(insertAt + 2, Build_Row(number, request, whenLocal));

            return number;
        }

        lines.Insert(lastRowIndex + 1, Build_Row(number, request, whenLocal));

        return number;
    }

    /// <summary>
    /// EVERY cell escaped, the id included. The id used to be interpolated raw into the status cell
    /// while only the owner's words were escaped, so an id carrying a pipe wrote a five-cell row under a
    /// four-column header.
    /// </summary>
    static string Build_Row(int number, ApprovedPlanRequest request, DateTime whenLocal)
    {
        return $"| {number} | {whenLocal:HH:mm} | {Escape_Cell(request.Words)} | {Describe_InitialStatus(request.RequestId)} |";
    }

    /// <summary>
    /// At the end of the ledger — immediately above the first non-ledger heading, or at the end of the
    /// file when there is none.
    ///
    /// SAID AS IT BEHAVES, not as it intends: in a plan whose PARKED section sits in the middle (the
    /// shape <see cref="PlanShape_Validator"/> records from `ai-orchestrator-3`, heading at line 253 of
    /// 550) the line lands there, above hundreds of later ledger lines. That is still inside the
    /// ledger's own region and still counted; it is simply not the bottom of the file.
    /// </summary>
    static void Insert_LedgerLine(List<string> lines, string ledgerRowRef)
    {
        var taskLine = $"- [ ] {ledgerRowRef}";

        for (var index = 0; index < lines.Count; index++)
        {
            if (!PlanLedger_Sections.Opens_NonLedgerSection(lines[index]))
                continue;

            var insertAt = index;

            // Keep the blank line that separates the ledger from the heading below it.
            while (insertAt > 0 && lines[insertAt - 1].Trim().Length == 0)
                insertAt--;

            lines.Insert(insertAt, taskLine);
            return;
        }

        Ensure_TrailingBlankLine(lines);
        lines.Add(taskLine);
    }

    static bool Is_TableRow(string line)
    {
        return line.TrimStart().StartsWith('|');
    }

    /// <summary>The cells between the outer pipes, escaped pipes left alone.</summary>
    static List<string> Read_Cells(string line)
    {
        var trimmed = line.TrimEnd('\r').Trim();
        var inner = trimmed.Trim('|');

        List<string> cells = [];
        var current = new System.Text.StringBuilder();

        for (var index = 0; index < inner.Length; index++)
        {
            if (inner[index] == '\\' && index + 1 < inner.Length && inner[index + 1] == '|')
            {
                current.Append("\\|");
                index++;
                continue;
            }

            if (inner[index] == '|')
            {
                cells.Add(current.ToString());
                current.Clear();
                continue;
            }

            current.Append(inner[index]);
        }

        cells.Add(current.ToString());

        return cells;
    }

    /// <summary>The leading cell as a number, or 0 — a hand-written "| — |" must not stop the count.</summary>
    static int Read_RowNumber(string line)
    {
        var cells = Read_Cells(line);

        return cells.Count > 0 && int.TryParse(cells[0].Trim(), out var number) ? number : 0;
    }

    static int Skip_TrailingBlankLines(IReadOnlyList<string> lines, int start, int end)
    {
        var index = end;

        while (index > start + 1 && lines[index - 1].Trim().Length == 0)
            index--;

        return index;
    }

    static void Ensure_TrailingBlankLine(List<string> lines)
    {
        if (lines.Count > 0 && lines[^1].Trim().Length != 0)
            lines.Add(string.Empty);
    }
}
