using System.Globalization;
using AIOrchestratorCoreLib.Running;
using AIOrchestratorCoreLib.Running.TurnResult;
using Xunit;

namespace AIOrchestratorCoreLib.Tests.Running;

/// <summary>
/// The usage-limit refusal, read for the one thing the dispatcher needs from it: WHEN the window
/// reopens. The two texts pinned here are the ones MEASURED on the VPS on 2026-09-08/09 —
/// "You've hit your weekly limit · resets 5am (Europe/Berlin)" and the session-limit variant with
/// "12am" — and the rest are the shapes the same sentence can obviously take (a half hour, a
/// 24-hour clock, a dotted meridiem, no zone, a bare zone, an unknown zone) plus the garbage that
/// must read as nothing at all.
///
/// <para>
/// THE TABLE IS THE POINT (F2, adversarial review 2026-09-09). The parser's stated contract is that
/// a shape it has never emitted reads as NOTHING rather than as a wrong instant, and three near
/// misses broke it: <c>"5:00 p.m."</c> was read as 05:00 on a 24-hour clock (and lost its zone with
/// it), <c>"5am CEST"</c> lost its zone for want of parentheses, and <c>"5:0am"</c> matched its own
/// first half. Every variant is now in one table WITH the ones that were already right, because a
/// table holding only the fixed cases cannot tell you the fix broke something else.
/// </para>
/// </summary>
public class LimitResetParserTests
{
    const string WEEKLY = "You've hit your weekly limit · resets 5am (Europe/Berlin)";
    const string SESSION = "You've hit your session limit · resets 12am (Europe/Berlin)";

    static DateTime Utc(int year, int month, int day, int hour, int minute)
    {
        return new DateTime(year, month, day, hour, minute, 0, DateTimeKind.Utc);
    }

    static DateTime Utc(string iso)
    {
        return DateTime.Parse(iso, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal);
    }

    /// <summary>
    /// EVERY CLOCK SHAPE, against one sentence, resolved or refused.
    ///
    /// <para>
    /// Europe/Berlin is CEST (UTC+2) on every date used here, so "5am Berlin" is 03:00 UTC. Each row
    /// carries its own <c>now</c> because the reading is now BOUNDED — see
    /// <see cref="LimitReset_Parser.MAX_DEFERRAL"/> — and a variant has to be read against a clock
    /// where its own answer is inside that bound, or the row is only re-testing the cap.
    /// </para>
    /// </summary>
    [Theory]
    // ----- the measured shape, and the spellings of the same hour -----
    [InlineData("resets 5am (Europe/Berlin)", "2026-09-09T00:30:00Z", "2026-09-09T03:00:00Z")]
    [InlineData("resets 5 am (Europe/Berlin)", "2026-09-09T00:30:00Z", "2026-09-09T03:00:00Z")]
    [InlineData("resets 5AM (Europe/Berlin)", "2026-09-09T00:30:00Z", "2026-09-09T03:00:00Z")]
    [InlineData("resets 5a.m. (Europe/Berlin)", "2026-09-09T00:30:00Z", "2026-09-09T03:00:00Z")]
    [InlineData("resets 5 a.m. (Europe/Berlin)", "2026-09-09T00:30:00Z", "2026-09-09T03:00:00Z")]
    [InlineData("resets 5:00am (Europe/Berlin)", "2026-09-09T00:30:00Z", "2026-09-09T03:00:00Z")]
    [InlineData("resets 5:00 a.m. (Europe/Berlin)", "2026-09-09T00:30:00Z", "2026-09-09T03:00:00Z")]
    [InlineData("resets 5:00 A.M. (Europe/Berlin)", "2026-09-09T00:30:00Z", "2026-09-09T03:00:00Z")]
    [InlineData("resets 05:00 (Europe/Berlin)", "2026-09-09T00:30:00Z", "2026-09-09T03:00:00Z")]
    [InlineData("resets 5:30am (Europe/Berlin)", "2026-09-09T00:30:00Z", "2026-09-09T03:30:00Z")]
    [InlineData("resets 5:30 (Europe/Berlin)", "2026-09-09T00:30:00Z", "2026-09-09T03:30:00Z")]
    // ----- THE F2 HEADLINE: a dotted PM meridiem, which used to read as 05:00 on a 24-hour clock -----
    [InlineData("resets 5:00 p.m. (Europe/Berlin)", "2026-09-09T10:00:00Z", "2026-09-09T15:00:00Z")]
    [InlineData("resets 5 p.m. (Europe/Berlin)", "2026-09-09T10:00:00Z", "2026-09-09T15:00:00Z")]
    [InlineData("resets 5pm (Europe/Berlin)", "2026-09-09T10:00:00Z", "2026-09-09T15:00:00Z")]
    [InlineData("resets 5P.M. (Europe/Berlin)", "2026-09-09T10:00:00Z", "2026-09-09T15:00:00Z")]
    [InlineData("resets 17:00 (Europe/Berlin)", "2026-09-09T10:00:00Z", "2026-09-09T15:00:00Z")]
    // ----- noon and midnight, the two a naive meridiem gets backwards -----
    [InlineData("resets 12pm (Europe/Berlin)", "2026-09-09T06:00:00Z", "2026-09-09T10:00:00Z")]
    [InlineData("resets 12am (Europe/Berlin)", "2026-09-09T18:00:00Z", "2026-09-09T22:00:00Z")]
    [InlineData("resets 12:00am (Europe/Berlin)", "2026-09-09T18:00:00Z", "2026-09-09T22:00:00Z")]
    // ----- THE F2 SECOND FORM: a zone with no parentheses round it -----
    [InlineData("resets 5am Europe/Berlin", "2026-09-09T00:30:00Z", "2026-09-09T03:00:00Z")]
    [InlineData("resets 17:00 UTC", "2026-09-09T12:00:00Z", "2026-09-09T17:00:00Z")]
    [InlineData("resets 5am (UTC)", "2026-09-09T00:30:00Z", "2026-09-09T05:00:00Z")]
    [InlineData("resets 5am", "2026-09-09T00:30:00Z", "2026-09-09T05:00:00Z")]
    // An abbreviation cannot be resolved unambiguously, so the clock is read in UTC and SAYS so (the
    // caveat is asserted below) — but it still yields an instant rather than nothing.
    [InlineData("resets 5am CEST", "2026-09-09T00:30:00Z", "2026-09-09T05:00:00Z")]
    // ----- the shapes that must read as NOTHING -----
    [InlineData("resets 13pm (Europe/Berlin)", "2026-09-09T00:30:00Z", null)]
    [InlineData("resets 25 (Europe/Berlin)", "2026-09-09T00:30:00Z", null)]
    [InlineData("resets 5:70am (Europe/Berlin)", "2026-09-09T00:30:00Z", null)]
    // F2: a malformed minute used to match just "resets 5" and answer 05:00 with total confidence.
    [InlineData("resets 5:0am (Europe/Berlin)", "2026-09-09T00:30:00Z", null)]
    [InlineData("resets soon", "2026-09-09T00:30:00Z", null)]
    [InlineData("resets", "2026-09-09T00:30:00Z", null)]
    // F9: two clauses in one sentence is a choice by position, which is not a reading.
    [InlineData("resets 5am (Europe/Berlin), or resets 6am (Europe/Berlin)", "2026-09-09T00:30:00Z", null)]
    // F1: the same measured sentence, read after the hour it names has gone by. Tomorrow's occurrence
    // is 24 h of silence bought on a boundary the account may be standing on — and it RATCHETED, one
    // day per refusal, with nothing able to wake the session.
    [InlineData("resets 5am (Europe/Berlin)", "2026-09-09T03:00:31Z", null)]
    // F1: and simply too far out to stand behind, which is the same refusal by the other road.
    [InlineData("resets 5am (Europe/Berlin)", "2026-09-09T10:00:00Z", null)]
    public void EveryClockShape_ResolvesToTheInstantItNames_OrToNothing(string clause, string nowIso, string? expectedIso)
    {
        var reading = LimitReset_Parser.Read_OrNull($"You've hit your weekly limit · {clause}", 429, Utc(nowIso));

        if (expectedIso == null)
        {
            Assert.Null(reading);
            return;
        }

        Assert.NotNull(reading);
        Assert.Equal(Utc(expectedIso), reading.ResetsAtUtc);
        Assert.Equal(DateTimeKind.Utc, reading.ResetsAtUtc.Kind);
    }

    [Fact]
    public void TheMeasuredWeeklyRefusal_ResolvesFiveAmBerlinToThreeAmUtc_AndQuotesTheWordsItReadItFrom()
    {
        var reading = LimitReset_Parser.Read_OrNull(WEEKLY, 429, Utc(2026, 9, 9, 1, 0));

        Assert.NotNull(reading);
        Assert.Equal(Utc(2026, 9, 9, 3, 0), reading.ResetsAtUtc);
        Assert.Equal("Europe/Berlin", reading.ZoneId);
        Assert.False(reading.ZoneAssumedUtc);
        Assert.Equal("resets 5am (Europe/Berlin)", reading.ResetsPhrase);
    }

    [Fact]
    public void TheZoneIsReadFromTheText_NotAssumedFromSummer_SoTheSameFiveAmIsFourAmUtcInWinter()
    {
        var reading = LimitReset_Parser.Read_OrNull(WEEKLY, 429, Utc(2026, 1, 15, 1, 0));

        Assert.NotNull(reading);
        Assert.Equal(Utc(2026, 1, 15, 4, 0), reading.ResetsAtUtc);
    }

    [Fact]
    public void TheMeasuredSessionRefusal_ReadsTwelveAmAsMidnight_NotAsNoon()
    {
        // Midnight BERLIN on the 10th is 22:00 UTC on the 9th — the date rolls in the zone, not in UTC.
        var reading = LimitReset_Parser.Read_OrNull(SESSION, 429, Utc(2026, 9, 9, 18, 0));

        Assert.NotNull(reading);
        Assert.Equal(Utc(2026, 9, 9, 22, 0), reading.ResetsAtUtc);
    }

    [Fact]
    public void AMidnightWrap_IsTomorrowsOccurrence_AndSurvivesTheCap()
    {
        // 23:50 Berlin, reset "12:30am": today's occurrence is twenty-three hours behind, tomorrow's is
        // forty minutes ahead. This is why the roll to tomorrow was KEPT rather than deleted along with
        // the ratchet it caused — the cap refuses the ratchet, not the wrap.
        var reading = LimitReset_Parser.Read_OrNull("You've hit your weekly limit · resets 12:30am (Europe/Berlin)", 429, Utc(2026, 9, 9, 21, 50));

        Assert.NotNull(reading);
        Assert.Equal(Utc(2026, 9, 9, 22, 30), reading.ResetsAtUtc);
    }

    [Fact]
    public void FivePm_WithNoZone_IsReadInUtcAndSaysSo()
    {
        var reading = LimitReset_Parser.Read_OrNull("You've hit your weekly limit · resets 5pm", 429, Utc(2026, 9, 9, 12, 0));

        Assert.NotNull(reading);
        Assert.Equal(Utc(2026, 9, 9, 17, 0), reading.ResetsAtUtc);
        Assert.True(reading.ZoneAssumedUtc);
        Assert.Equal("UTC", reading.ZoneId);
        Assert.Equal("resets 5pm", reading.ResetsPhrase);
        Assert.Contains("named no zone", reading.Describe_Source());
    }

    [Fact]
    public void AZoneThisMachineDoesNotKnow_FallsBackToUtcAndSaysWhichZoneWasAskedFor()
    {
        var reading = LimitReset_Parser.Read_OrNull("You've hit your weekly limit · resets 5am (Mars/Olympus)", 429, Utc(2026, 9, 9, 1, 0));

        Assert.NotNull(reading);
        Assert.Equal(Utc(2026, 9, 9, 5, 0), reading.ResetsAtUtc);
        Assert.True(reading.ZoneAssumedUtc);
        Assert.Equal("Mars/Olympus", reading.ZoneId);
    }

    /// <summary>
    /// F9. <c>EST</c> and <c>MST</c> exist in the zoneinfo database as FIXED offsets with no daylight
    /// rule, so <c>FindSystemTimeZoneById("EST")</c> succeeds and answers UTC-5 all year — an hour
    /// wrong from March to November, with <c>ZoneAssumedUtc</c> FALSE, so the log carried no caveat at
    /// all. <c>CST</c> is worse: two zones fourteen hours apart. An abbreviation is now refused as a
    /// zone, which puts the reading in UTC WITH the caveat attached.
    /// </summary>
    [Theory]
    [InlineData("EST")]
    [InlineData("CEST")]
    [InlineData("PST")]
    [InlineData("CST")]
    public void AZoneAbbreviation_IsNeverResolvedSilently_ItFallsBackToUtcWithTheCaveat(string abbreviation)
    {
        var reading = LimitReset_Parser.Read_OrNull($"You've hit your weekly limit · resets 5am ({abbreviation})", 429, Utc(2026, 9, 9, 1, 0));

        Assert.NotNull(reading);
        Assert.True(reading.ZoneAssumedUtc, $"'{abbreviation}' was resolved to a concrete offset and the reading claims no caveat");
        Assert.Equal(abbreviation, reading.ZoneId);
        Assert.Equal(Utc(2026, 9, 9, 5, 0), reading.ResetsAtUtc);
        Assert.Contains(abbreviation, reading.Describe_Source());
    }

    /// <summary>UTC and GMT are the one abbreviation family ambiguous nowhere, so they carry no caveat.</summary>
    [Theory]
    [InlineData("UTC")]
    [InlineData("GMT")]
    public void TheUtcFamily_IsAZoneAndNotACaveat(string word)
    {
        var reading = LimitReset_Parser.Read_OrNull($"You've hit your weekly limit · resets 5am ({word})", 429, Utc(2026, 9, 9, 1, 0));

        Assert.NotNull(reading);
        Assert.False(reading.ZoneAssumedUtc);
        Assert.Equal(Utc(2026, 9, 9, 5, 0), reading.ResetsAtUtc);
    }

    [Fact]
    public void TheCaseOfTheSentence_DoesNotMatter()
    {
        var reading = LimitReset_Parser.Read_OrNull("YOU'VE HIT YOUR WEEKLY LIMIT · Resets 5AM (Europe/Berlin)", 429, Utc(2026, 9, 9, 1, 0));

        Assert.NotNull(reading);
        Assert.Equal(Utc(2026, 9, 9, 3, 0), reading.ResetsAtUtc);
    }

    [Fact]
    public void AnExactHit_OnTheResetInstant_IsTodaysAndNotTomorrows()
    {
        // 03:00 UTC IS 5am Berlin: the window has just reopened, so the answer is now — pushing it a
        // day out here would cost 24 hours for being one tick early.
        var reading = LimitReset_Parser.Read_OrNull(WEEKLY, 429, Utc(2026, 9, 9, 3, 0));

        Assert.NotNull(reading);
        Assert.Equal(Utc(2026, 9, 9, 3, 0), reading.ResetsAtUtc);
    }

    /// <summary>
    /// F1, THE RATCHET, stated as behaviour rather than as one row of the table. Thirty-one seconds
    /// after the named hour — exactly where <c>LIMIT_RESET_MARGIN</c> puts the retry — the old rule
    /// answered TOMORROW, so a second refusal on the same sentence bought another whole day and a
    /// third bought another. Nothing woke it: <c>Consider_Session</c> returned on the appointment
    /// before it looked at pending traffic.
    /// </summary>
    [Fact]
    public void AResetThatHasJustGoneBy_ReadsAsNothing_SoTheRatchetHasNothingToTurn()
    {
        Assert.Null(LimitReset_Parser.Read_OrNull(WEEKLY, 429, Utc(2026, 9, 9, 3, 0).AddSeconds(31)));
        Assert.Null(LimitReset_Parser.Read_OrNull(WEEKLY, 429, Utc(2026, 9, 9, 3, 5)));
        Assert.Null(LimitReset_Parser.Read_OrNull(WEEKLY, 429, Utc(2026, 9, 9, 4, 0)));
    }

    /// <summary>
    /// F8, THE AUTUMN FOLD. On 2026-10-25 Europe/Berlin strikes 02:30 twice — once at 00:30 UTC on
    /// CEST and again at 01:30 UTC on CET. An ambiguous clock resolves to the STANDARD reading, the
    /// later of the two, because late is a wait and early is a second refusal. And once the fold has
    /// passed, the same rule that killed the ratchet answers the same way here: today's 02:30 is
    /// behind us, tomorrow's is a day out, and a day is not an appointment this app will keep.
    /// </summary>
    [Fact]
    public void AnAmbiguousFoldClock_TakesTheLaterReading_AndOnceItHasPassedItReadsAsNothing()
    {
        const string folded = "You've hit your weekly limit · resets 2:30am (Europe/Berlin)";

        // 02:20 CEST, before either striking: the answer is the standard-time one, 01:30 UTC.
        Assert.Equal(Utc(2026, 10, 25, 1, 30), LimitReset_Parser.Read_OrNull(folded, 429, Utc(2026, 10, 25, 0, 20))!.ResetsAtUtc);

        // 02:00 CET, between the two: still ahead on the wall clock, still the standard reading.
        Assert.Equal(Utc(2026, 10, 25, 1, 30), LimitReset_Parser.Read_OrNull(folded, 429, Utc(2026, 10, 25, 1, 0))!.ResetsAtUtc);

        // 02:40 CET, both strikings gone: tomorrow's is 23 h 50 m out, which is not an appointment.
        Assert.Null(LimitReset_Parser.Read_OrNull(folded, 429, Utc(2026, 10, 25, 1, 40)));
    }

    /// <summary>
    /// F1's other half: whatever the sentence says, an appointment is never further out than the cap
    /// and never behind us. Nothing here asserts the cap's VALUE — the assertion is the invariant, so
    /// the number can be argued down later without a test having to be edited to agree with it.
    /// </summary>
    [Fact]
    public void NoReadingIsEverBehindUsOrFurtherOutThanTheCap_AtAnyHourOfTheDay()
    {
        string[] texts =
        [
            WEEKLY,
            SESSION,
            "You've hit your weekly limit · resets 17:00 (Europe/Berlin)",
            "You've hit your weekly limit · resets 12:30am",
        ];

        for (var minutesFromMidnight = 0; minutesFromMidnight < 24 * 60; minutesFromMidnight += 7)
        {
            var now = Utc(2026, 9, 9, 0, 0).AddMinutes(minutesFromMidnight);

            foreach (var text in texts)
            {
                var reading = LimitReset_Parser.Read_OrNull(text, 429, now);

                if (reading == null)
                    continue;

                Assert.InRange(reading.ResetsAtUtc - now, TimeSpan.Zero, LimitReset_Parser.MAX_DEFERRAL);
            }
        }
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("DONE\n\nThe parser is written and the suite is green.")]
    [InlineData("Error: connection reset by peer")]
    [InlineData("You've hit your weekly limit")]
    [InlineData("{\"is_error\":true,")]
    public void GarbageAndSilence_ReadAsNothing(string? resultText)
    {
        Assert.Null(LimitReset_Parser.Read_OrNull(resultText, 429, Utc(2026, 9, 9, 1, 0)));
    }

    [Fact]
    public void AResetsClauseInAnAnswerThatNamesNoLimit_IsNotAUsageLimit_UnlessTheStatusSaysSo()
    {
        const string prose = "REPORT\n\nThe cache resets 5am (Europe/Berlin), which is why the test flaked.";

        Assert.Null(LimitReset_Parser.Read_OrNull(prose, null, Utc(2026, 9, 9, 1, 0)));
        Assert.Null(LimitReset_Parser.Read_OrNull(prose, 529, Utc(2026, 9, 9, 1, 0)));
        Assert.NotNull(LimitReset_Parser.Read_OrNull(prose, 429, Utc(2026, 9, 9, 1, 0)));
    }

    [Fact]
    public void TheMeasuredRefusalPhrase_IsEnough_WhenTheStatusIsMissing()
    {
        // MEASURED SHAPE, MISSING FIELD. api_error_status is nullable everywhere in this repo because a
        // newer CLI may stop sending it; the sentence is the stronger signal and must stand on its own.
        var reading = LimitReset_Parser.Read_OrNull(WEEKLY, null, Utc(2026, 9, 9, 1, 0));

        Assert.NotNull(reading);
        Assert.Equal(Utc(2026, 9, 9, 3, 0), reading.ResetsAtUtc);
    }

    /// <summary>
    /// F3, THE ENTRY GATE. It was a case-insensitive <c>Contains("limit")</c>, so every one of these
    /// sentences opened the deferral path — and two of them parked healthy sessions on the VPS. The
    /// word "limit" is not a refusal: either the 429 says so, or the sentence carries wording only a
    /// refusal carries.
    /// </summary>
    [Theory]
    [InlineData("REPORT\n\nAdded a rate limiter to the client.")]
    [InlineData("REPORT\n\nThe plan is unlimited retries, and the window resets 5am (Europe/Berlin) anyway.")]
    [InlineData("REPORT\n\nThere is no limit on the queue depth; the window resets 5am (Europe/Berlin).")]
    [InlineData("REPORT\n\nAbbiamo superato il limite di richieste.")]
    [InlineData("Error: upstream timeout while the rate limit window resets 5am (Europe/Berlin)")]
    public void TheWordLimit_IsNotARefusal(string resultText)
    {
        Assert.False(LimitReset_Parser.Looks_LikeUsageLimit(resultText, null));
        Assert.Null(LimitReset_Parser.Read_OrNull(resultText, null, Utc(2026, 9, 9, 1, 0)));
    }

    [Theory]
    [InlineData("You've hit your weekly limit · resets 5am (Europe/Berlin)")]
    [InlineData("You've hit your session limit · resets 12am (Europe/Berlin)")]
    [InlineData("You have reached your usage limit")]
    [InlineData("Claude usage limit reached")]
    public void TheWordingOfARealRefusal_StillOpensTheGate_WithNoStatusAtAll(string resultText)
    {
        Assert.True(LimitReset_Parser.Looks_LikeUsageLimit(resultText, null));
    }

    /// <summary>
    /// F3 again, and the reason it matters most: the app's OWN deferral entry quotes the reset clause
    /// into the channel. A session echoing that entry back — a supervisor summarising why a member is
    /// quiet, say — must not be able to park itself on its own words.
    /// </summary>
    [Fact]
    public void TheAppsOwnDeferralWording_DoesNotOpenTheGate()
    {
        const string echoed = "REPORT\n\nimp-1 is waiting on a usage limit: turn repo-1/imp-1/3 was refused for a usage limit "
            + "and is scheduled to run again in 2 h 15 min (03:00 UTC), read from 'resets 5am (Europe/Berlin)'.";

        Assert.False(LimitReset_Parser.Looks_LikeUsageLimit(echoed, null));
        Assert.Null(LimitReset_Parser.Read_OrNull(echoed, null, Utc(2026, 9, 9, 1, 0)));
    }

    /// <summary>
    /// F3, PROBE (a). A turn that exited 0 with <c>is_error</c> false and no 429 reaches the failure
    /// path when its reply cannot be appended, and <c>Record_Failure</c> hands the MODEL'S OWN WORDS to
    /// the gate as the result text. The turn reported success; whatever it wrote, it is not a refusal.
    /// </summary>
    [Fact]
    public void ATurnThatReportedSuccess_IsNeverARefusal_WhateverItsReplySays()
    {
        var succeeded = Result(exitCode: 0, isError: false, apiErrorStatus: null,
            resultText: "REPORT\n\nYou've hit your weekly limit · resets 5am (Europe/Berlin) — that is the sentence I taught the parser to read.");

        Assert.False(LimitReset_Parser.Is_Refusal(succeeded));
    }

    /// <summary>
    /// F3, PROBE (b). An ordinary repeatable error whose text merely mentions a rate-limit window. It
    /// used to be parked WITHOUT an attempt being spent, so it could never reach the attempt limit,
    /// never stall and never alert — a session quietly out of the world with nothing to end it.
    /// </summary>
    [Fact]
    public void AnOrdinaryErrorThatMentionsARateLimitWindow_IsNotARefusal()
    {
        var failed = Result(exitCode: 1, isError: true, apiErrorStatus: 500,
            resultText: "Error: upstream 500 while the provider's rate limit window resets 5am (Europe/Berlin)");

        Assert.False(LimitReset_Parser.Is_Refusal(failed));
    }

    [Fact]
    public void TheMeasuredRefusal_IsARefusal_WhicheverSignalCarriesIt()
    {
        Assert.True(LimitReset_Parser.Is_Refusal(Result(exitCode: 1, isError: true, apiErrorStatus: 429, resultText: WEEKLY)));

        // The status alone, with wording nobody has seen.
        Assert.True(LimitReset_Parser.Is_Refusal(Result(exitCode: 1, isError: true, apiErrorStatus: 429, resultText: "quota exhausted, try later")));

        // The wording alone, with the status dropped by a newer CLI.
        Assert.True(LimitReset_Parser.Is_Refusal(Result(exitCode: 1, isError: true, apiErrorStatus: null, resultText: WEEKLY)));
    }

    [Fact]
    public void ANonUtcNow_IsAccepted_AndAnsweredInUtc()
    {
        // The dispatcher hands it DateTime.UtcNow, but the tracker's clock is local — a reading that
        // silently treated an unspecified stamp as UTC would be wrong by the machine's offset.
        var reading = LimitReset_Parser.Read_OrNull(WEEKLY, 429, Utc(2026, 9, 9, 1, 0).ToLocalTime());

        Assert.NotNull(reading);
        Assert.Equal(Utc(2026, 9, 9, 3, 0), reading.ResetsAtUtc);
        Assert.Equal(DateTimeKind.Utc, reading.ResetsAtUtc.Kind);
    }

    static ITurnResult Result(int exitCode, bool isError, int? apiErrorStatus, string resultText)
    {
        return TurnResult_Factory.Create(exitCode, timedOut: false, isError, subtype: null, resultText, sessionId: "s", totalCostUsd: null,
            durationMs: null, durationApiMs: null, numTurns: null, apiErrorStatus, rawStdout: string.Empty, rawStderr: string.Empty, elapsed: TimeSpan.FromSeconds(1));
    }
}
