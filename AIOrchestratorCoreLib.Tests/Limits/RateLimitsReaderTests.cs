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
/// </summary>
public class RateLimitsReaderTests : IDisposable
{
    static readonly DateTime NOW = new(2026, 8, 11, 22, 0, 0);

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
    /// they are different instances. The older one's percentage says nothing about the newer one,
    /// so it must not win on being larger.
    /// </summary>
    [Fact]
    public void Read_Worst_ANewerWindowInstanceReplacesAnOlderOne_RatherThanCompetingOnPercent()
    {
        var older = Write_ProbeFile("five_hour", 99, NOW.AddMinutes(30), "Opus 5");
        var newer = Write_ProbeFile("five_hour", 12, NOW.AddHours(5), "Sonnet 5");

        var windows = RateLimits_Reader.Read_WorstAcrossSessions([older, newer], NOW);

        var window = Assert.Single(windows);
        Assert.Equal(12, window.Percent);
        Assert.Equal(NOW.AddHours(5), window.ResetsAtLocal);
        Assert.Equal("Sonnet 5", window.Models);
    }

    /// <summary>Order must not decide the answer — the newer instance wins from either direction.</summary>
    [Fact]
    public void Read_Worst_TheNewerInstanceWins_WhicheverFileIsReadFirst()
    {
        var older = Write_ProbeFile("five_hour", 99, NOW.AddMinutes(30), "Opus 5");
        var newer = Write_ProbeFile("five_hour", 12, NOW.AddHours(5), "Sonnet 5");

        var forwards = RateLimits_Reader.Read_WorstAcrossSessions([older, newer], NOW);
        var backwards = RateLimits_Reader.Read_WorstAcrossSessions([newer, older], NOW);

        Assert.Equal(12, Assert.Single(forwards).Percent);
        Assert.Equal(12, Assert.Single(backwards).Percent);
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
