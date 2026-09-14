using System.Text;
using AIOrchestratorCoreLib.Limits;
using AIOrchestratorCoreLib.SupervisionPaths;
using Xunit;

namespace AIOrchestratorCoreLib.Tests.Limits;

/// <summary>
/// Guards the number behind /limits and behind the automatic 90-100% alerts. Probe files are never
/// deleted, so this reader always sees days of history at once and has to decide which readings are
/// still about the CURRENT limit window — the whole defect class lives in that decision.
///
/// The incident, 2026-08-11: a `crm-2` orchestration closed five days earlier still reported
/// five_hour 99% while every live session sat at 19%, and 99% is the number the owner was shown.
/// The same stale reading latched `.limit-alerts.json` at 100% and, because a window only re-arms
/// below 50%, silently killed every future limit alert.
///
/// The second incident, 2026-09-11, is the same decision taken by the wrong evidence. The rule that
/// fixed the first one — "a LATER reset stamp is a NEWER window" — assumed the stamp grows with
/// time. It does not: measured across nine probe files on the live machine, the weekly window's
/// reset instant moved BACKWARDS from 09-16 05:00 to 09-14 10:00. So a two-day-old probe reporting
/// 60% outranked four live sessions reporting 90%, and the weekly figure froze there — it could not
/// move again until 09-16, whatever the account did. The owner reported it as a stuck counter.
/// The rule now orders readings by WHEN THEY WERE TAKEN.
/// </summary>
public class RateLimitsReaderTests : IDisposable
{
    static readonly DateTime NOW = new(2026, 8, 11, 22, 0, 0);

    // WHEN THE READING WAS TAKEN, which is now what decides between two window instances. Always
    // set explicitly: letting it fall out of the order the fixtures happen to be written is how a
    // test ends up with two routes to its answer and pins neither (CLAUDE.md item 20).
    static readonly DateTime TAKEN_TWO_DAYS_AGO = new DateTime(2026, 8, 9, 22, 0, 0, DateTimeKind.Utc);
    static readonly DateTime TAKEN_AN_HOUR_AGO = new DateTime(2026, 8, 11, 21, 0, 0, DateTimeKind.Utc);
    static readonly DateTime TAKEN_A_MINUTE_AGO = new DateTime(2026, 8, 11, 21, 59, 0, DateTimeKind.Utc);

    readonly string _tempFolder;

    public RateLimitsReaderTests()
    {
        _tempFolder = Path.Combine(Path.GetTempPath(), $"aiorch-ratelimits-tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempFolder);
    }

    public void Dispose()
    {
        Directory.Delete(_tempFolder, recursive: true);
    }

    /// <summary>The incident itself: an expired window describes an allowance already handed back.</summary>
    [Fact]
    public void Read_Worst_DiscardsAWindowThatHasAlreadyReset_EvenWhenItIsTheHighestReading()
    {
        var stale = Write_ProbeFile("five_hour", 99, NOW.AddDays(-5), "Opus 5 (1M context)");
        var live = Write_ProbeFile("five_hour", 19, NOW.AddHours(4), "Opus 5");

        var windows = RateLimits_Reader.Read_WorstAcrossSessions([stale, live], NOW);

        var window = Assert.Single(windows);
        Assert.Equal(19, window.Percent);
        Assert.Equal(NOW.AddHours(4), window.ResetsAtLocal);

        // The models tail is a claim about what is running NOW, so a dead session must not appear.
        Assert.Equal("Opus 5", window.Models);
    }

    /// <summary>
    /// The subtler half, and the one a pure expiry check misses: BOTH windows are unexpired, but
    /// they are different instances. The abandoned one's percentage says nothing about the window in
    /// force, so it must not win on being larger.
    ///
    /// THE TWO SIGNALS ARE DELIBERATELY SET AGAINST EACH OTHER, so only one route reaches the
    /// answer: the file that was WRITTEN LAST names the EARLIER reset stamp, and it is also the
    /// lower reading. Under the stamp rule this test fails with 99; under the recency rule it
    /// passes with 12. That is the 2026-09-11 incident in miniature.
    /// </summary>
    [Fact]
    public void Read_Worst_TheInstanceNamedByTheLatestReading_ReplacesTheOther_WhateverItsResetStampSays()
    {
        var abandoned = Write_ProbeFile("five_hour", 99, NOW.AddHours(5), "Opus 5", TAKEN_TWO_DAYS_AGO);
        var live = Write_ProbeFile("five_hour", 12, NOW.AddMinutes(30), "Sonnet 5", TAKEN_A_MINUTE_AGO);

        var windows = RateLimits_Reader.Read_WorstAcrossSessions([abandoned, live], NOW);

        var window = Assert.Single(windows);
        Assert.Equal(12, window.Percent);
        Assert.Equal(NOW.AddMinutes(30), window.ResetsAtLocal);
        Assert.Equal("Sonnet 5", window.Models);
    }

    /// <summary>Order must not decide the answer — the latest reading wins from either direction.</summary>
    [Fact]
    public void Read_Worst_TheLatestReadingWins_WhicheverFileIsReadFirst()
    {
        var abandoned = Write_ProbeFile("five_hour", 99, NOW.AddHours(5), "Opus 5", TAKEN_TWO_DAYS_AGO);
        var live = Write_ProbeFile("five_hour", 12, NOW.AddMinutes(30), "Sonnet 5", TAKEN_A_MINUTE_AGO);

        var forwards = RateLimits_Reader.Read_WorstAcrossSessions([abandoned, live], NOW);
        var backwards = RateLimits_Reader.Read_WorstAcrossSessions([live, abandoned], NOW);

        Assert.Equal(12, Assert.Single(forwards).Percent);
        Assert.Equal(12, Assert.Single(backwards).Percent);
    }

    /// <summary>
    /// THE REPORTED DEFECT, with the live figures. Nine probe files, one account: two idle
    /// orchestrations still name the weekly window that ended on 09-16, four live ones name the one
    /// that ends on 09-14. The 09-16 stamp is later, so the stamp rule elected `fincanva-2`'s
    /// two-day-old 60% and held it against every live session's 90% — under-reporting a weekly
    /// allowance that was nearly spent, and frozen there until 09-16 whatever the account did.
    /// </summary>
    [Fact]
    public void Read_Worst_AnIdleProbeNamingALaterWeeklyReset_NoLongerFreezesTheLiveReading()
    {
        var idleNamingALaterReset = Write_ProbeFile("seven_day", 60, NOW.AddDays(4), "Opus 5", TAKEN_TWO_DAYS_AGO);
        var alsoIdle = Write_ProbeFile("seven_day", 7, NOW.AddDays(4), "Opus 5", TAKEN_TWO_DAYS_AGO);
        var live = Write_ProbeFile("seven_day", 90, NOW.AddDays(2), "Opus 5", TAKEN_A_MINUTE_AGO);
        var alsoLive = Write_ProbeFile("seven_day", 90, NOW.AddDays(2), "Opus 5", TAKEN_A_MINUTE_AGO);

        var window = Assert.Single(RateLimits_Reader.Read_WorstAcrossSessions([idleNamingALaterReset, alsoIdle, live, alsoLive], NOW));

        Assert.Equal(90, window.Percent);
        Assert.Equal(NOW.AddDays(2), window.ResetsAtLocal);
    }

    /// <summary>
    /// And it must not merely have swapped which stale file wins. Three instances, and the answer is
    /// the one the most recent reading names — not the earliest stamp, not the latest, not the
    /// highest percent.
    /// </summary>
    [Fact]
    public void Read_Worst_WithThreeCompetingInstances_ReportsTheOneTheLatestReadingNames()
    {
        var oldest = Write_ProbeFile("five_hour", 99, NOW.AddHours(1), "Opus 5", TAKEN_TWO_DAYS_AGO);
        var middle = Write_ProbeFile("five_hour", 80, NOW.AddHours(9), "Opus 5", TAKEN_AN_HOUR_AGO);
        var latest = Write_ProbeFile("five_hour", 33, NOW.AddHours(4), "Opus 5", TAKEN_A_MINUTE_AGO);

        foreach (var order in new[] { new[] { oldest, middle, latest }, [latest, middle, oldest], [middle, latest, oldest] })
        {
            var window = Assert.Single(RateLimits_Reader.Read_WorstAcrossSessions(order, NOW));

            Assert.Equal(33, window.Percent);
            Assert.Equal(NOW.AddHours(4), window.ResetsAtLocal);
        }
    }

    /// <summary>
    /// Inside ONE window usage is cumulative and account-wide, so the highest reading is the one
    /// that constrains you — a session that has not rendered its status line lately must not make
    /// the report look rosier than reality.
    /// </summary>
    [Fact]
    public void Read_Worst_SameWindowInstance_KeepsTheHighestReading()
    {
        var resetsAt = NOW.AddHours(3);
        var quiet = Write_ProbeFile("five_hour", 40, resetsAt, "Opus 5");
        var busy = Write_ProbeFile("five_hour", 71, resetsAt, "Opus 5");

        var windows = RateLimits_Reader.Read_WorstAcrossSessions([quiet, busy], NOW);

        Assert.Equal(71, Assert.Single(windows).Percent);
    }

    /// <summary>
    /// The percentage and the "resets in ..." it is printed beside must come from the SAME file.
    /// Pairing a winning percent with a previously held stamp would tell the owner a true number
    /// and a false deadline, which is worse than either alone.
    /// </summary>
    [Fact]
    public void Read_Worst_ThePercentAndItsResetStampAlwaysTravelTogether()
    {
        var resetsAt = NOW.AddHours(2);
        var lower = Write_ProbeFile("five_hour", 30, resetsAt, "Opus 5");
        var higher = Write_ProbeFile("five_hour", 88, resetsAt, "Opus 5");

        var forwards = Assert.Single(RateLimits_Reader.Read_WorstAcrossSessions([lower, higher], NOW));
        var backwards = Assert.Single(RateLimits_Reader.Read_WorstAcrossSessions([higher, lower], NOW));

        Assert.Equal(88, forwards.Percent);
        Assert.Equal(resetsAt, forwards.ResetsAtLocal);
        Assert.Equal(88, backwards.Percent);
        Assert.Equal(resetsAt, backwards.ResetsAtLocal);
    }

    /// <summary>
    /// An older status line exposes used_percentage without resets_at. Such a reading cannot be
    /// shown to be current, so it must never displace one that can — but it is not discarded
    /// either, or the whole report would go silent on that Claude Code version.
    /// </summary>
    [Fact]
    public void Read_Worst_AnUndatedReadingNeverDisplacesADatedOne()
    {
        var undated = Write_ProbeFile_WithoutResetStamp("five_hour", 99, "Opus 5");
        var dated = Write_ProbeFile("five_hour", 21, NOW.AddHours(4), "Opus 5");

        var windows = RateLimits_Reader.Read_WorstAcrossSessions([undated, dated], NOW);

        Assert.Equal(21, Assert.Single(windows).Percent);
    }

    /// <summary>With nothing datable anywhere, the old highest-wins reading is still the best available.</summary>
    [Fact]
    public void Read_Worst_UndatedReadingsAlone_StillReportTheHighest()
    {
        var lower = Write_ProbeFile_WithoutResetStamp("five_hour", 44, "Opus 5");
        var higher = Write_ProbeFile_WithoutResetStamp("five_hour", 77, "Opus 5");

        var windows = RateLimits_Reader.Read_WorstAcrossSessions([lower, higher], NOW);

        var window = Assert.Single(windows);
        Assert.Equal(77, window.Percent);
        Assert.Null(window.ResetsAtLocal);
    }

    /// <summary>Each window name is judged on its own — an expired 5h must not take the weekly with it.</summary>
    [Fact]
    public void Read_Worst_ExpiryIsPerWindow_NotPerFile()
    {
        var mixed = Write_ProbeFile_WithTwoWindows(
            ("five_hour", 99, NOW.AddDays(-5)),
            ("seven_day", 84, NOW.AddDays(5)));

        var windows = RateLimits_Reader.Read_WorstAcrossSessions([mixed], NOW);

        var window = Assert.Single(windows);
        Assert.Equal("weekly", window.Window);
        Assert.Equal(84, window.Percent);
    }

    /// <summary>
    /// The file-level gate the ALERT scan depends on: it reads through the tolerant parser, which
    /// carries no reset stamps at all, so staleness has to be excluded before it ever sees a file.
    /// </summary>
    [Fact]
    public void Find_UsageFiles_WithLiveWindow_KeepsLiveAndUndatedFiles_AndDropsSpentAndEmptyOnes()
    {
        var paths = SupervisionPaths_Factory.Create(_tempFolder);

        var live = Write_ProbeFile("five_hour", 19, NOW.AddHours(4), "Opus 5");
        var spent = Write_ProbeFile("five_hour", 99, NOW.AddDays(-5), "Opus 5");
        var undated = Write_ProbeFile_WithoutResetStamp("five_hour", 55, "Opus 5");
        var noLimitData = Write_UsageFile("""{ "model": { "display_name": "Opus 5" } }""");

        var kept = RateLimits_Reader.Find_UsageFiles_WithLiveWindow(paths, NOW);

        Assert.Contains(live, kept);
        Assert.Contains(undated, kept);
        Assert.DoesNotContain(spent, kept);
        Assert.DoesNotContain(noLimitData, kept);
    }

    /// <summary>
    /// The real probe files on disk are UTF-8 WITH a byte-order mark — the status line writes them
    /// with PowerShell's `-Encoding utf8`, which emits one. A reader that ignored the BOM would see
    /// a leading garbage character, fail to parse, and silently report no limit data at all.
    /// </summary>
    [Fact]
    public void Read_Worst_ReadsTheByteOrderMarkedFilesTheStatusLineActuallyWrites()
    {
        var path = Path.Combine(_tempFolder, $"{Guid.NewGuid():N}.usage.json");
        var json = Build_ProbeJson("five_hour", 37, NOW.AddHours(4), "Opus 5");

        File.WriteAllText(path, json, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));

        // Proves the fixture really is BOM-prefixed, so the assertion below cannot pass by accident.
        var firstBytes = File.ReadAllBytes(path);
        Assert.Equal([0xEF, 0xBB, 0xBF], firstBytes[..3]);

        var windows = RateLimits_Reader.Read_WorstAcrossSessions([path], NOW);

        Assert.Equal(37, Assert.Single(windows).Percent);
    }

    /// <summary>A window resetting this very instant is spent, not live — the boundary belongs to the past.</summary>
    [Fact]
    public void Read_Worst_AWindowResettingExactlyNow_CountsAsExpired()
    {
        var boundary = Write_ProbeFile("five_hour", 66, NOW, "Opus 5");

        Assert.Empty(RateLimits_Reader.Read_WorstAcrossSessions([boundary], NOW));
    }

    /// <summary>A missing or unparseable file contributes nothing rather than throwing.</summary>
    [Fact]
    public void Read_Worst_MissingOrGarbageFile_ContributesNothing_NeverThrows()
    {
        var garbage = Write_UsageFile("{ not json at all");
        var missing = Path.Combine(_tempFolder, "no-such-file.usage.json");

        Assert.Empty(RateLimits_Reader.Read_WorstAcrossSessions([garbage, missing], NOW));
    }

    /// <summary>
    /// The effort dial, as Claude Code 2.1.266 reports it: `"effort":{"level":"xhigh"}`, a sibling of
    /// the `model` block. The fixture is the live payload shape trimmed to its neighbours, not an
    /// invented one — see SessionContextUsageFactoryTests for why that matters here.
    /// </summary>
    [Fact]
    public void Read_EffortLevel_ReadsTheDialClaudeCodeReports()
    {
        Assert.Equal("xhigh", RateLimits_Reader.Read_EffortLevel_OrNull(PAYLOAD_WITH_EFFORT));
    }

    /// <summary>
    /// The block is ABSENT on an older Claude Code and on a model without the dial. Absent is
    /// unknown — never a default like "high" that the owner could mistake for a setting they chose.
    /// </summary>
    [Fact]
    public void Read_EffortLevel_IsUnknownWhenThePayloadCarriesNoEffortBlock()
    {
        Assert.Null(RateLimits_Reader.Read_EffortLevel_OrNull("""{ "model": { "id": "claude-opus-5", "display_name": "Opus 5" } }"""));
    }

    /// <summary>Half a file, an empty one, or a level that is not a string: unknown, never a throw.</summary>
    [Fact]
    public void Read_EffortLevel_IsUnknownForMalformedPayloads_NeverThrows()
    {
        Assert.Null(RateLimits_Reader.Read_EffortLevel_OrNull("{ not json at all"));
        Assert.Null(RateLimits_Reader.Read_EffortLevel_OrNull(""));
        Assert.Null(RateLimits_Reader.Read_EffortLevel_OrNull("""{ "effort": { "lev"""));
        Assert.Null(RateLimits_Reader.Read_EffortLevel_OrNull("""{ "effort": { "level": 3 } }"""));
        Assert.Null(RateLimits_Reader.Read_EffortLevel_OrNull("""{ "effort": "xhigh" }"""));
    }

    /// <summary>
    /// A BOM at the front of the TEXT is refused quietly, exactly as its siblings refuse it: the
    /// JSON parser throws on a leading U+FEFF and the catch turns that into unknown. This is not
    /// where the BOM every probe file carries gets handled — the shared file read strips it before
    /// any of these readers see the text (SessionModelReadingFactoryTests proves the file-level
    /// path reads through one). Pinned beside the model reader so the two cannot drift apart on it.
    /// </summary>
    [Fact]
    public void Read_EffortLevel_RefusesABomPrefixedStringTheWayTheModelReaderDoes()
    {
        var bomPrefixed = "\uFEFF" + PAYLOAD_WITH_EFFORT;

        Assert.Null(RateLimits_Reader.Read_EffortLevel_OrNull(bomPrefixed));
        Assert.Null(RateLimits_Reader.Read_ModelName_OrNull(bomPrefixed));
    }

    /// <summary>
    /// The session's own id, straight from the status line — what `claude --resume` takes. Read
    /// beside the transcript path because a respawn needs both: the id to resume, the path to prove
    /// there is still a conversation behind it.
    /// </summary>
    [Fact]
    public void Read_SessionId_ReadsTheIdClaudeCodeReports()
    {
        Assert.Equal("7f34ac2b", RateLimits_Reader.Read_SessionId_OrNull(PAYLOAD_WITH_EFFORT));
    }

    /// <summary>Absent, half a file, or not a string: unknown, never a throw — the respawn path runs on it.</summary>
    [Fact]
    public void Read_SessionId_IsUnknownWhenAbsentOrMalformed_NeverThrows()
    {
        Assert.Null(RateLimits_Reader.Read_SessionId_OrNull("""{ "model": { "id": "claude-opus-5", "display_name": "Opus 5" } }"""));
        Assert.Null(RateLimits_Reader.Read_SessionId_OrNull("{ not json at all"));
        Assert.Null(RateLimits_Reader.Read_SessionId_OrNull(""));
        Assert.Null(RateLimits_Reader.Read_SessionId_OrNull("""{ "session_id": 42 }"""));
        Assert.Null(RateLimits_Reader.Read_SessionId_OrNull("\uFEFF" + PAYLOAD_WITH_EFFORT));
    }

    /// <summary>The live shape (Claude Code 2.1.266), trimmed to the two blocks these readers look at.</summary>
    const string PAYLOAD_WITH_EFFORT =
        """{"session_id":"7f34ac2b","effort":{"level":"xhigh"},"model":{"id":"claude-fable-5-1","display_name":"Fable 5.1"},"version":"2.1.266"}""";

    static string Build_ProbeJson(string windowKey, double percent, DateTime resetsAtLocal, string modelName)
    {
        return $$"""
            {
              "model": { "display_name": "{{modelName}}" },
              "rate_limits": {
                "{{windowKey}}": { "used_percentage": {{percent}}, "resets_at": {{To_UnixSeconds(resetsAtLocal)}} }
              }
            }
            """;
    }

    static long To_UnixSeconds(DateTime localTime)
    {
        var utc = TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(localTime, DateTimeKind.Unspecified));

        return new DateTimeOffset(utc, TimeSpan.Zero).ToUnixTimeSeconds();
    }

    string Write_ProbeFile(string windowKey, double percent, DateTime resetsAtLocal, string modelName)
    {
        return Write_UsageFile(Build_ProbeJson(windowKey, percent, resetsAtLocal, modelName));
    }

    /// <summary>The same probe, with the moment its reading was taken stated rather than implied.</summary>
    string Write_ProbeFile(string windowKey, double percent, DateTime resetsAtLocal, string modelName, DateTime takenAtUtc)
    {
        var path = Write_ProbeFile(windowKey, percent, resetsAtLocal, modelName);

        File.SetLastWriteTimeUtc(path, takenAtUtc);

        return path;
    }

    string Write_ProbeFile_WithoutResetStamp(string windowKey, double percent, string modelName)
    {
        return Write_UsageFile($$"""
            {
              "model": { "display_name": "{{modelName}}" },
              "rate_limits": { "{{windowKey}}": { "used_percentage": {{percent}} } }
            }
            """);
    }

    string Write_ProbeFile_WithTwoWindows(
        (string Key, double Percent, DateTime ResetsAtLocal) first,
        (string Key, double Percent, DateTime ResetsAtLocal) second)
    {
        return Write_UsageFile($$"""
            {
              "model": { "display_name": "Opus 5" },
              "rate_limits": {
                "{{first.Key}}": { "used_percentage": {{first.Percent}}, "resets_at": {{To_UnixSeconds(first.ResetsAtLocal)}} },
                "{{second.Key}}": { "used_percentage": {{second.Percent}}, "resets_at": {{To_UnixSeconds(second.ResetsAtLocal)}} }
              }
            }
            """);
    }

    string Write_UsageFile(string content)
    {
        var path = Path.Combine(_tempFolder, $"{Guid.NewGuid():N}.usage.json");
        File.WriteAllText(path, content);

        return path;
    }
}
