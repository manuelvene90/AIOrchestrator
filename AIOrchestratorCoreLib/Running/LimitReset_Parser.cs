using System.Globalization;
using System.Text.RegularExpressions;
using AIOrchestratorCoreLib.Running.TurnResult;

namespace AIOrchestratorCoreLib.Running;

/// <summary>
/// WHEN the usage window reopens, read out of the CLI's own refusal. Nothing more: no policy, no
/// clock of its own, no side effect — the dispatcher decides what to do with the instant.
///
/// <para>
/// MEASURED on the VPS 2026-09-08/09. A turn refused for quota comes back as a result row with
/// <c>is_error: true</c>, <c>api_error_status: 429</c> and a <c>result</c> of
/// <c>"You've hit your weekly limit · resets 5am (Europe/Berlin)"</c> (the session-limit variant
/// says <c>"resets 12am (Europe/Berlin)"</c>). The dispatcher used to treat that like any other
/// error — three attempts a minute apart, then a stall until new traffic arrived — and the
/// orchestrations sat dead for 167 to 509 minutes, two of them for a whole night. The sentence
/// already carries the answer; this reads it.
/// </para>
/// <para>
/// TOLERANT LIKE <see cref="Limits.LimitData_Parser"/>, and for the same reason: this text is a
/// human sentence from a CLI that changes, so a shape it has never emitted must read as NOTHING
/// rather than as a wrong instant. Every unparseable input answers null, and null means the
/// dispatcher keeps its old behaviour exactly — which is decision 21's rule that a guard which
/// cannot evaluate its predicate says so and allows.
/// </para>
/// <para>
/// AND "NOTHING" NOW COVERS THE PLAUSIBLE-BUT-UNVERIFIABLE, not just the garbage. Review of the
/// merged feature on 2026-09-09 probed three shapes that read as a CONFIDENT WRONG INSTANT: a
/// reset that had just gone by came back as tomorrow's (24 h of silence, and it ratcheted — every
/// further refusal parsed the same sentence and bought another day); <c>"resets 5:00 p.m."</c> was
/// read as 05:00 on a 24-hour clock AND lost its zone with it (17 h out); and any wording merely
/// containing the word "limit" opened the whole path. Each of those is now null or refused —
/// see <see cref="MAX_DEFERRAL"/>, <see cref="RESETS_CLAUSE"/> and
/// <see cref="Looks_LikeUsageLimit"/> — because null costs three attempts and a stall that new
/// traffic clears, and a wrong instant costs a night.
/// </para>
/// <para>
/// TWO SIGNALS, EITHER ONE. The sentence must carry a <c>resets &lt;clock&gt;</c> clause, and then
/// either the status is 429 or the sentence itself carries a REFUSAL PHRASE (not merely the word
/// "limit" — see <see cref="Looks_LikeUsageLimit"/>). Both halves are needed because both are
/// droppable: <c>api_error_status</c> is nullable everywhere in this repo (a newer CLI may stop
/// sending it), and a 429 could arrive with wording nobody has seen. What the pair rules out is a
/// session's OWN answer being mistaken for a refusal — a failed turn whose report happens to say
/// "the cache resets 5am" must not be able to park itself until morning.
/// </para>
/// </summary>
public static class LimitReset_Parser
{
    /// <summary>The status the CLI reports for a quota refusal (measured 2026-09-08 on the VPS).</summary>
    public const int USAGE_LIMIT_STATUS = 429;

    /// <summary>
    /// THE HARD CAP ON SILENCE. A refusal may buy the session at most this much quiet; a clock that
    /// resolves further out than this is answered as NOTHING, and the dispatcher keeps its ordinary
    /// three attempts and its stall — which new traffic clears.
    ///
    /// <para>
    /// WHY A CAP AT ALL, and why it is the whole of the F1 fix (probe, 2026-09-09). The old rule was
    /// "today's occurrence if it is still to come, tomorrow's otherwise", unbounded. With
    /// <see cref="PrintTurn_Words.LIMIT_RESET_MARGIN"/> at 30 s the retry fires just after the named
    /// hour, so an account that has not actually crossed the boundary — clock skew, or the CLI
    /// rounding a 05:15 reset to "5am" — is refused again at 05:00:31, parses the same sentence,
    /// finds the hour a half-minute in the past and parks itself for another 24 hours. It ratchets:
    /// every refusal buys another day, and <c>Consider_Session</c> returned before it looked at
    /// pending traffic, so nothing woke it. Three attempts and a stall are strictly better than that.
    /// </para>
    /// <para>
    /// SIX HOURS, and the number is argued rather than round. The session limit — the common refusal —
    /// runs on a five-hour window, so every reset it can name is inside this. A WEEKLY limit can name
    /// an hour a day away, and that is exactly the reading nothing here can verify: the instant is
    /// derived from an hour with no date, in a zone that may not resolve, against a boundary the
    /// account may already have crossed. Buying a day of silence on it is the confident wrong number
    /// CLAUDE.md decision 12 is about; stalling costs three attempts and one alert the owner can act
    /// on. Bounding it also disposes of the DST FOLD (F8): an autumn-fold clock that lands behind us
    /// rolls to tomorrow like any other and is refused here, so the same rule that killed the ratchet
    /// killed the 24 h fold error too.
    /// </para>
    /// </summary>
    public static readonly TimeSpan MAX_DEFERRAL = TimeSpan.FromHours(6);

    /// <summary>
    /// <c>resets 5am</c>, <c>resets 12am</c>, <c>resets 3:30pm</c>, <c>resets 17:00</c>,
    /// <c>resets 5:00 p.m. (Europe/Berlin)</c>, <c>resets 5am CEST</c>. The clock is validated after
    /// the match, not in the pattern: a regex that rejected "13pm" by shape would also have to know
    /// that "13:00" is fine, and the two rules read far more clearly as code.
    ///
    /// <para>
    /// THE DOTTED MERIDIEM AND THE BARE ZONE ARE THE F2 FIX (probe, 2026-09-09). <c>(am|pm)?</c> does
    /// not match <c>p.m.</c>, so <c>"resets 5:00 p.m. (Europe/Berlin)"</c> was read as 05:00 on a
    /// 24-hour clock — and because the zone group had to follow the meridiem IMMEDIATELY, the same
    /// near-miss silently threw the zone away as well. Seventeen hours out, then a further day per
    /// refusal. <c>"resets 5am CEST"</c> lost its zone the same way, for want of parentheses.
    /// </para>
    /// <para>
    /// THE MINUTE IS TWO DIGITS AND THE CLOCK IS FOLLOWED BY A GUARD, so a shape the parser does not
    /// understand reads as nothing rather than as its own first half: <c>"resets 5:0am"</c> used to
    /// match just <c>"resets 5"</c> and answer 05:00. <c>(?![\d:])</c> makes the whole clause fail
    /// instead.
    /// </para>
    /// <para>
    /// THE BARE ZONE IS SHAPE-RESTRICTED — an all-caps abbreviation of 2 to 5 letters, or an
    /// <c>Area/City</c> id — so it cannot swallow the next ordinary word of a sentence and report it
    /// as a zone. <c>(?-i:…)</c> turns the pattern's own <c>IgnoreCase</c> off for that alternative,
    /// which is the whole reason "tomorrow" is not read as a zone.
    /// </para>
    /// <para>
    /// THE SPACE BEFORE THE MERIDIEM LIVES INSIDE THE MERIDIEM'S OWN GROUP, and that is not tidiness:
    /// written as a bare <c>\s*</c> outside it, a clause with no meridiem (<c>"resets 17:00 UTC"</c>)
    /// has its separating space eaten by that <c>\s*</c>, the zone's <c>\s+</c> then finds none, the
    /// optional zone group matches EMPTY — and since the overall match has succeeded there is no
    /// failure to backtrack from. The zone is silently gone, which is the F2 defect in its second
    /// form.
    /// </para>
    /// </summary>
    static readonly Regex RESETS_CLAUSE = new(
        @"resets\s+(?<hour>\d{1,2})(?::(?<minute>\d{2}))?(?![\d:])(?:\s*(?<meridiem>[ap]\.?m\.?))?(?:\s*\((?<zone>[^)]{1,64})\)|\s+(?<zoneBare>(?-i:[A-Z]{2,5})|[A-Za-z_]+/[A-Za-z0-9_+\-]+))?",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
        TimeSpan.FromMilliseconds(250));

    /// <summary>
    /// WORDS ONLY A REFUSAL CARRIES. The gate used to be a case-insensitive <c>Contains("limit")</c>,
    /// which is true of <c>rate limiter</c>, <c>unlimited</c>, <c>no limit</c> and the Italian
    /// <c>limite</c> — and two probes on 2026-09-09 parked healthy sessions through it: a turn that
    /// SUCCEEDED but whose reply could not be appended (the model's own words become the failure's
    /// result text) sat 14 hours with <c>FailedAttempts = 0</c>, and an ordinary repeatable error
    /// whose text merely mentioned a rate-limit window was parked without spending an attempt — so it
    /// could never reach the attempt limit, never stall and never alert.
    ///
    /// <para>
    /// THE MEASURED SHAPES AND NOTHING ELSE. "You've hit your weekly limit", the session-limit
    /// variant, and the "usage limit reached" wording. Anything else must come with the 429, which is
    /// the signal a model quoting a sentence cannot produce. This deliberately also keeps THE APP'S
    /// OWN deferral entry out of the gate — that entry quotes the reset clause into the channel, and
    /// a session echoing it used to be able to re-park itself.
    /// </para>
    /// </summary>
    static readonly Regex REFUSAL_PHRASE = new(
        @"\byou(?:'ve|’ve|\s+have)\s+(?:hit|reached)\s+your\b[^.\n]{0,40}?\blimit\b|\busage\s+limit\s+reached\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
        TimeSpan.FromMilliseconds(250));

    /// <summary>
    /// Zone words that mean UTC exactly and are ambiguous in no time zone database — the one family
    /// of abbreviations that can be honoured rather than caveated.
    /// </summary>
    static readonly string[] UTC_WORDS = ["UTC", "GMT", "UT", "Z", "ZULU"];

    /// <summary>2 to 5 letters and nothing else: <c>CEST</c>, <c>EST</c>, <c>PST</c>, <c>IST</c>.</summary>
    static readonly Regex ZONE_ABBREVIATION = new(
        @"^[A-Za-z]{1,5}$",
        RegexOptions.CultureInvariant,
        TimeSpan.FromMilliseconds(250));

    /// <summary>
    /// Whether this text is a quota refusal AT ALL — the question the dispatcher asks before it
    /// complains that it could not read one. Deliberately looser than
    /// <see cref="Read_OrNull"/>: a refusal that names no clock still answers true here, because a
    /// limit the app cannot schedule around is exactly the thing that has to reach the log.
    ///
    /// <para>
    /// ONE IMPLEMENTATION, TWO CALLERS. <see cref="Read_OrNull"/> and <see cref="Is_Refusal"/> both
    /// gate on this, so "does this text name a limit" is answered in one place — the alternative was
    /// the dispatcher keeping its own copy of the same question, which CLAUDE.md decision 12 names as
    /// the way two answers start to differ.
    /// </para>
    /// </summary>
    public static bool Looks_LikeUsageLimit(string? resultText, int? apiErrorStatus)
    {
        if (apiErrorStatus == USAGE_LIMIT_STATUS)
            return true;

        if (string.IsNullOrWhiteSpace(resultText))
            return false;

        try
        {
            return REFUSAL_PHRASE.IsMatch(resultText);
        }
        catch (RegexMatchTimeoutException)
        {
            // A pathological result text is not a refusal we can prove; the caller keeps its ordinary
            // attempts, which is the behaviour that has an alert at the end of it.
            return false;
        }
    }

    /// <summary>
    /// Whether this TURN was refused for a usage limit — the whole question, asked of the whole
    /// result, and the only form the dispatcher should use.
    ///
    /// <para>
    /// A TURN THAT REPORTED SUCCESS IS NEVER A REFUSAL, however its prose reads. Probe, 2026-09-09: a
    /// turn that exited 0 with <c>is_error</c> false and no 429 reached the failure path only because
    /// its reply could not be appended (the channel was locked) — and <c>Record_Failure</c> puts the
    /// MODEL'S OWN WORDS into the result text, which then went through a text-only gate and parked a
    /// healthy session for 14 hours with no attempt spent. The exit code is the CLI's own answer to
    /// "was this a refusal", and it outranks any reading of prose.
    /// </para>
    /// </summary>
    public static bool Is_Refusal(ITurnResult result)
    {
        if (TurnOutcomes.Is_Success(result))
            return false;

        return Looks_LikeUsageLimit(result.ResultText, result.ApiErrorStatus);
    }

    /// <summary>
    /// The reset instant this text names, or null when it names none this parser is sure of.
    /// <paramref name="now"/> may be of any <see cref="DateTimeKind"/> — it is converted to UTC
    /// before use, because the dispatcher's clock is local and its state file is not.
    /// </summary>
    public static LimitResetReading? Read_OrNull(string? resultText, int? apiErrorStatus, DateTime now)
    {
        if (string.IsNullOrWhiteSpace(resultText))
            return null;

        if (!Looks_LikeUsageLimit(resultText, apiErrorStatus))
            return null;

        Match match;
        bool moreThanOneClause;

        try
        {
            match = RESETS_CLAUSE.Match(resultText);
            moreThanOneClause = match.Success && match.NextMatch().Success;
        }
        catch (RegexMatchTimeoutException)
        {
            // A pathological result text is not worth a turn: no reading, so the caller keeps its
            // old behaviour and logs which predicate it could not evaluate.
            return null;
        }

        if (!match.Success)
            return null;

        // TWO CLAUSES ARE A CHOICE THIS CANNOT MAKE (F9, 2026-09-09). "resets 5am … resets 6am" used
        // to take the first silently. WHICH of the two governs is exactly the kind of question a
        // parser of someone else's prose must not answer by position, so it answers nothing and the
        // session keeps its attempts.
        if (moreThanOneClause)
            return null;

        var clock = Read_Clock_OrNull(match);

        if (clock == null)
            return null;

        var zoneText = Read_ZoneText_OrNull(match);
        var zone = Find_Zone_OrNull(zoneText);
        var resolved = Resolve_NextOccurrence(clock.Value.Hour, clock.Value.Minute, zone ?? TimeZoneInfo.Utc, now.ToUniversalTime());

        if (resolved == null)
            return null;

        return new LimitResetReading(
            resolved.Value,
            match.Value.Trim(),
            zone == null ? zoneText ?? "UTC" : zoneText!,
            zone == null);
    }

    /// <summary>The parenthesised zone if the text gave one, else the bare token, else nothing.</summary>
    static string? Read_ZoneText_OrNull(Match match)
    {
        if (match.Groups["zone"].Success)
            return match.Groups["zone"].Value.Trim();

        return match.Groups["zoneBare"].Success ? match.Groups["zoneBare"].Value.Trim() : null;
    }

    /// <summary>
    /// The hour and minute the clause names, or null when they are not a time of day. With a
    /// meridiem the hour is 1-12 (<c>12am</c> is midnight, <c>12pm</c> is noon); without one it is a
    /// 24-hour clock. "13pm" and "25" are the two shapes this rejects, and both are why the
    /// validation is here rather than in the pattern.
    /// </summary>
    static (int Hour, int Minute)? Read_Clock_OrNull(Match match)
    {
        if (!int.TryParse(match.Groups["hour"].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var hour))
            return null;

        var minute = 0;

        if (match.Groups["minute"].Success && !int.TryParse(match.Groups["minute"].Value, NumberStyles.None, CultureInfo.InvariantCulture, out minute))
            return null;

        if (minute is < 0 or > 59)
            return null;

        if (!match.Groups["meridiem"].Success)
            return hour is >= 0 and <= 23 ? (hour, minute) : null;

        if (hour is < 1 or > 12)
            return null;

        // THE DOTS ARE NOISE, NOT MEANING: "pm", "p.m." and "P.M." are one word, and only the first
        // letter carries the answer. Reading them with an equality against "pm" is what made the
        // dotted form fall through to the 24-hour branch (F2).
        var pm = match.Groups["meridiem"].Value.StartsWith("p", StringComparison.OrdinalIgnoreCase);

        return (pm ? hour == 12 ? 12 : hour + 12 : hour == 12 ? 0 : hour, minute);
    }

    /// <summary>
    /// The zone the text named, or null when it named none, named one this machine does not have, or
    /// named an ABBREVIATION. The caller then says in its log line that the instant was read in UTC
    /// instead.
    ///
    /// <para>
    /// ABBREVIATIONS ARE REFUSED ON PURPOSE (F9, 2026-09-09). <c>EST</c> and <c>MST</c> resolve on a
    /// zoneinfo host as FIXED offsets with no daylight rule, so <c>"resets 5am (EST)"</c> in summer
    /// used to answer an instant an hour wrong with <see cref="LimitResetReading.ZoneAssumedUtc"/>
    /// FALSE — a wrong number with no caveat attached, which is the pairing CLAUDE.md decision 12 is
    /// about. And <c>CST</c> is two zones fourteen hours apart. Refusing them puts the reading back in
    /// UTC WITH the caveat, and <see cref="MAX_DEFERRAL"/> bounds what a zone-sized error can cost.
    /// The one family kept is <see cref="UTC_WORDS"/>, which is unambiguous everywhere.
    /// </para>
    /// <para>
    /// IANA ids resolve on every platform this app runs on (.NET carries ICU), but a zone database
    /// that has never heard of the id is a fact to report and fall back from, not to throw over.
    /// </para>
    /// </summary>
    static TimeZoneInfo? Find_Zone_OrNull(string? zoneText)
    {
        if (string.IsNullOrWhiteSpace(zoneText))
            return null;

        var trimmed = zoneText.Trim();

        if (UTC_WORDS.Contains(trimmed, StringComparer.OrdinalIgnoreCase))
            return TimeZoneInfo.Utc;

        if (ZONE_ABBREVIATION.IsMatch(trimmed))
            return null;

        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById(trimmed);
        }
        catch (Exception)
        {
            // TimeZoneNotFoundException, InvalidTimeZoneException and (on a locked-down host) a
            // security or IO failure reading the database — all of them mean the same thing here.
            return null;
        }
    }

    /// <summary>
    /// The next time that clock strikes in that zone, as a UTC instant — or NULL when that instant is
    /// further out than <see cref="MAX_DEFERRAL"/>, which is the longest appointment this app keeps.
    /// Today's occurrence if it is still to come (equal counts as still to come — a window that has
    /// just reopened is not a reason to wait), tomorrow's otherwise; the roll survives the cap because
    /// of the midnight wrap, where at 23:50 a "12:30am" reset is forty minutes away and not a day.
    ///
    /// <para>
    /// A CLOCK THAT HAS JUST GONE BY IS ANSWERED WITH NOTHING, not with tomorrow and not with "now".
    /// Tomorrow is the ratchet <see cref="MAX_DEFERRAL"/> describes. "Now" is worse: the appointment
    /// would already be due, the dispatcher would re-run the turn on the very next tick, be refused
    /// again, and loop — spending quota at the tick rate with no attempt counted and no stall to end
    /// it. Falling through to the ordinary attempts gives the retry a backoff, a counter and an alert,
    /// which is everything that loop lacks.
    /// </para>
    /// <para>
    /// DST IS ANSWERED, NOT ASSUMED AWAY. A clock that the spring-forward skips does not exist, and
    /// <see cref="TimeZoneInfo.ConvertTimeToUtc(DateTime, TimeZoneInfo)"/> throws on it; the answer
    /// is the first instant after the gap, which is when the window is certainly open. An AMBIGUOUS
    /// clock (autumn, struck twice) resolves to the standard-time reading, the LATER of the two in
    /// UTC — late is a wait, early is a second refusal.
    /// </para>
    /// </summary>
    static DateTime? Resolve_NextOccurrence(int hour, int minute, TimeZoneInfo zone, DateTime nowUtc)
    {
        var nowInZone = TimeZoneInfo.ConvertTimeFromUtc(nowUtc, zone);
        var candidate = new DateTime(nowInZone.Year, nowInZone.Month, nowInZone.Day, 0, 0, 0, DateTimeKind.Unspecified).AddHours(hour).AddMinutes(minute);

        if (candidate < nowInZone)
            candidate = candidate.AddDays(1);

        while (zone.IsInvalidTime(candidate))
            candidate = candidate.AddMinutes(1);

        var resolvedUtc = TimeZoneInfo.ConvertTimeToUtc(candidate, zone);

        // BEHIND US IS NOT AN APPOINTMENT. The comparison above is on the zone's wall clock; the fold
        // resolution here is in UTC, so on the one night a year the clocks go back the two can
        // disagree by an hour. An appointment already due is the tick-rate loop described above, so it
        // reads as nothing like everything else this parser cannot stand behind.
        if (resolvedUtc < nowUtc)
            return null;

        return resolvedUtc - nowUtc > MAX_DEFERRAL ? null : resolvedUtc;
    }
}

/// <summary>
/// One reading of a usage-limit refusal: the instant, the words it was read from, and whether the
/// zone was the one the text asked for. All three are carried because the dispatcher's log line and
/// its channel entry must both quote the SOURCE — an instant with no provenance is the kind of
/// confident number this repo has been burnt by (CLAUDE.md decisions 12 and 18).
/// </summary>
/// <param name="ResetsAtUtc">When the window reopens. Always <see cref="DateTimeKind.Utc"/>.</param>
/// <param name="ResetsPhrase">The matched words, verbatim — e.g. <c>resets 5am (Europe/Berlin)</c>.</param>
/// <param name="ZoneId">The zone the text asked for, or <c>UTC</c> when it named none.</param>
/// <param name="ZoneAssumedUtc">True when <see cref="ResetsAtUtc"/> was computed in UTC because the named zone was absent, unknown here, or an ambiguous abbreviation.</param>
public sealed record LimitResetReading(DateTime ResetsAtUtc, string ResetsPhrase, string ZoneId, bool ZoneAssumedUtc)
{
    /// <summary>
    /// Where the instant came from, in words — the words themselves, plus the zone caveat when there
    /// is one. ONE implementation, because the log line and the channel entry must say the same
    /// thing (CLAUDE.md decision 12: never add a second copy of a formatter), and the caveat is the
    /// half a reader needs most: an instant read in UTC because the named zone could not be resolved
    /// here can be hours off, and saying so is what makes that recoverable instead of mysterious.
    /// </summary>
    public string Describe_Source()
    {
        if (!ZoneAssumedUtc)
            return $"'{ResetsPhrase}'";

        return ZoneId == "UTC"
            ? $"'{ResetsPhrase}' — it named no zone, so the clock was read in UTC"
            : $"'{ResetsPhrase}' — the zone '{ZoneId}' is not one this machine can resolve unambiguously, so the clock was read in UTC";
    }
}
