using AIOrchestratorCoreLib.Limits;
using AIOrchestratorCoreLib.SupervisionPaths;
using Xunit;

namespace AIOrchestratorCoreLib.Tests.Limits;

/// <summary>
/// The silent failure this whole branch exists to prevent: the owner is AT their limit and hears
/// nothing. Every other test here checks that stale readings are discarded — these check the
/// opposite direction, that a discard rule never eats a true one.
///
/// The hazard is specific and it is not hypothetical. A session that actually hits the limit STOPS,
/// so its status line stops rewriting `.usage.json` and the file goes stale within minutes — while
/// carrying the 100%. Any freshness rule based on FILE AGE would therefore throw the reading away at
/// exactly the moment it became true, and silently: with nothing left to report, the alert scan
/// simply returns. The rule keys on the window's reset stamp instead, and at 100% the window has by
/// definition not reset.
/// </summary>
public class LiveLimitStillAlertsTests : IDisposable
{
    static readonly DateTime NOW = new(2026, 8, 11, 22, 0, 0, DateTimeKind.Utc);

    readonly string _tempFolder;

    public LiveLimitStillAlertsTests()
    {
        _tempFolder = Path.Combine(Path.GetTempPath(), $"aiorch-livelimit-tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempFolder);
    }

    public void Dispose()
    {
        Directory.Delete(_tempFolder, recursive: true);
    }

    /// <summary>A stopped session's probe is stale by definition — and it is the one carrying the 100%.</summary>
    [Fact]
    public void ASessionStoppedAtItsLimit_IsStillReportedByLimits_HoweverStaleItsProbeIs()
    {
        var atTheLimit = Write_ProbeFile("five_hour", 100, NOW.AddHours(3));

        File.SetLastWriteTimeUtc(atTheLimit, NOW.AddDays(-2));

        var windows = RateLimits_Reader.Read_WorstAcrossSessions([atTheLimit], NOW);

        Assert.Equal(100, Assert.Single(windows).Percent);
    }

    /// <summary>The same file must survive the ALERT path's gate, which is where silence would bite.</summary>
    [Fact]
    public void ASessionStoppedAtItsLimit_IsStillSelectedForTheAlertScan()
    {
        var paths = SupervisionPaths_Factory.Create(_tempFolder);
        var atTheLimit = Write_ProbeFile("five_hour", 100, NOW.AddHours(3));

        File.SetLastWriteTimeUtc(atTheLimit, NOW.AddDays(-2));

        Assert.Contains(atTheLimit, RateLimits_Reader.Find_UsageFiles_WithLiveWindow(paths, NOW));
    }

    /// <summary>And the alert itself must actually fire — a 100% with nothing latched is a 100% alert.</summary>
    [Fact]
    public void ALiveHundredPercent_Alerts()
    {
        var windows = LimitData_Parser.Extract_LimitWindows(Build_ProbeJson("five_hour", 100, NOW.AddHours(3)));
        var window = windows["rate_limits.five_hour.used_percentage"];

        Assert.False(RateLimits_Reader.Is_ExpiredWindow(window.WindowResetsAtUtc, NOW));

        var lastAlerted = LimitAlert_Tracker.Resolve_LastAlertedThreshold_ForCurrentWindow(
            storedThreshold: 0,
            storedWindowIdentity: null,
            currentWindowIdentity: window.WindowResetsAtUtc,
            currentPercent: window.Percent);

        Assert.Equal(100, LimitAlert_Tracker.Get_NewlyCrossedThreshold_OrNull(window.Percent, lastAlerted));
    }

    /// <summary>
    /// The reviewer's F2: the file-level gate keeps a file when ANY window is live, so a SPENT
    /// five_hour was riding into the alert scan on the live weekly's stamp — and could fire a 90%
    /// alert about an allowance already handed back. The file is kept; the spent window is not.
    /// </summary>
    [Fact]
    public void AFileKeptForItsLiveWeekly_DoesNotSmuggleInItsExpiredFiveHour()
    {
        var paths = SupervisionPaths_Factory.Create(_tempFolder);

        var mixed = Write_UsageFile($$"""
            {
              "rate_limits": {
                "five_hour": { "used_percentage": 91, "resets_at": {{To_UnixSeconds(NOW.AddHours(-1))}} },
                "seven_day": { "used_percentage": 62, "resets_at": {{To_UnixSeconds(NOW.AddDays(5))}} }
              }
            }
            """);

        // Kept, correctly — its weekly is live.
        Assert.Contains(mixed, RateLimits_Reader.Find_UsageFiles_WithLiveWindow(paths, NOW));

        var windows = LimitData_Parser.Extract_LimitWindows(File.ReadAllText(mixed));

        // ...but the five_hour inside it is spent, and the per-window rule is what says so.
        Assert.True(RateLimits_Reader.Is_ExpiredWindow(windows["rate_limits.five_hour.used_percentage"].WindowResetsAtUtc, NOW));
        Assert.False(RateLimits_Reader.Is_ExpiredWindow(windows["rate_limits.seven_day.used_percentage"].WindowResetsAtUtc, NOW));

        // /limits already applied it per window; the alert scan now uses the same predicate.
        // NOTE the clock: this reader converts reset stamps to LOCAL for display, so it must be
        // asked in local time. Passing UTC here silently reported the spent five_hour as live —
        // the exact hazard Is_ExpiredWindow's doc warns about, walked into while writing its test.
        var reported = Assert.Single(RateLimits_Reader.Read_WorstAcrossSessions([mixed], NOW.ToLocalTime()));
        Assert.Equal("weekly", reported.Window);
    }

    /// <summary>An undated window is never assumed spent — that would silence an old status line entirely.</summary>
    [Fact]
    public void AWindowWithNoResetStamp_IsNeverTreatedAsExpired()
    {
        Assert.False(RateLimits_Reader.Is_ExpiredWindow(null, NOW));
    }

    static long To_UnixSeconds(DateTime utc)
    {
        return new DateTimeOffset(utc, TimeSpan.Zero).ToUnixTimeSeconds();
    }

    static string Build_ProbeJson(string windowKey, double percent, DateTime resetsAtUtc)
    {
        return $$"""
            {
              "rate_limits": {
                "{{windowKey}}": { "used_percentage": {{percent}}, "resets_at": {{To_UnixSeconds(resetsAtUtc)}} }
              }
            }
            """;
    }

    string Write_ProbeFile(string windowKey, double percent, DateTime resetsAtUtc)
    {
        return Write_UsageFile(Build_ProbeJson(windowKey, percent, resetsAtUtc));
    }

    string Write_UsageFile(string content)
    {
        var path = Path.Combine(_tempFolder, $"{Guid.NewGuid():N}.usage.json");
        File.WriteAllText(path, content);

        return path;
    }
}
