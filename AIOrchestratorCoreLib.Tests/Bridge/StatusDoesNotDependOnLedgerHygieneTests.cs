using Xunit;

namespace AIOrchestratorCoreLib.Tests.Bridge;

/// <summary>
/// THE PERIODIC STATUS IS BACK — AS A SETTING, CHANGE-GATED, AND OUT OF THE ENGINE.
///
/// <para>
/// WHAT THIS FILE HAS TESTED, in order. First `Has_WorkInFlight` — the status's trigger — after
/// `Tear-off tabs` went five hours without a status on 2026-08-20 while its solo worked: the ledger
/// said nothing was `[>]`. Then, after the fork deleted the feed on 2026-09-09 (ten messages in five
/// and a half hours, three identical), that the cadence was GONE: nothing in the engine could post a
/// status on a clock again.
/// </para>
/// <para>
/// That second claim retired on 2026-09-23. The owner: *"he removed the status message, but I liked
/// it, so we should be able to opt in"* — D1 (a), plan 03 Task 8. The feed is re-ported under
/// `phone.status.periodic` (classic on, quiet off), posted only when it CHANGED (ruling R6), and its
/// trigger, text and baseline live in `Bridge/PeriodicStatus/`, where the suite can drive them:
/// `PeriodicStatusSweepTests` carries every behavioural claim, the ledger-hygiene one included
/// (`WithNoWorkInFlight_NoStatusIsPosted` — a session that WORKED with no `[>]` line still gets its
/// status).
/// </para>
/// <para>
/// What stays a scan is what a test of the sweep cannot see: that the engine kept no second copy of
/// the cadence after handing it over, so there is one place a status is decided.
/// </para>
/// </summary>
public class StatusDoesNotDependOnLedgerHygieneTests
{
    const string ENGINE_FILE = "BridgeEngineModel.cs";

    /// <summary>
    /// No trigger, text builder, baseline or slot decision in the engine — each is the sweep's now, and
    /// a dormant copy here would be the second copy decision 12 warns about.
    /// </summary>
    [Fact]
    public void ThePeriodicStatusCadence_LivesInTheSweep_NotInTheEngine()
    {
        var source = Read_EngineSource();

        Assert.DoesNotContain("Has_WorkInFlight", source);
        Assert.DoesNotContain("Build_PeriodicStatusText", source);
        Assert.DoesNotContain("Remember_PostedProgress", source);
        Assert.DoesNotContain("PeriodicStatusSlot_Planner.Decide", source);

        Assert.Contains("_periodicStatus.Push_Async(", source);
    }

    /// <summary>
    /// THE ENGINE'S ONE STATUS POSTER IS THE HOST ADAPTER the sweep calls. If a second caller ever
    /// appears inside the engine, it posts a status that no change gate and no slot decided — the
    /// waterfall by a side door — and this test is the thing that asks why.
    /// </summary>
    [Fact]
    public void TheOnlyThingInTheEngineThatPostsAStatusEntry_IsTheSweepsHost()
    {
        var body = Extract_Method("async Task Push_PeriodicStatus_Async");

        Assert.Contains("_periodicStatus.Push_Async(", body);

        var source = Read_EngineSource();

        Assert.Equal(1, source.Split("=> Post_StatusEntry(").Length - 1);
        Assert.Equal(0, source.Split(" Post_StatusEntry(session").Length - 1);
    }

    static string Extract_Method(string signatureMark)
    {
        var source = Read_EngineSource();

        var at = source.IndexOf(signatureMark, StringComparison.Ordinal);

        Assert.True(at >= 0, $"'{signatureMark}' is not in {ENGINE_FILE} — this scan can prove nothing about a method it cannot find");

        var open = source.IndexOf('{', at);
        var depth = 0;

        for (var i = open; i < source.Length; i++)
        {
            if (source[i] == '/' && i + 1 < source.Length && source[i + 1] == '/')
            {
                while (i < source.Length && source[i] != '\n')
                    i++;

                continue;
            }

            if (source[i] == '{')
            {
                depth++;
            }
            else if (source[i] == '}')
            {
                depth--;

                if (depth == 0)
                    return source[open..(i + 1)];
            }
        }

        Assert.Fail($"unbalanced braces walking '{signatureMark}'");

        return "";
    }

    static string Read_EngineSource()
    {
        var folder = AppContext.BaseDirectory;

        for (var depth = 0; depth < 8; depth++)
        {
            var candidate = Path.Combine(folder, "AIOrchestratorCoreLib", "Bridge", "BridgeEngine", ENGINE_FILE);

            if (File.Exists(candidate))
                return File.ReadAllText(candidate);

            var parent = Directory.GetParent(folder);

            if (parent == null)
                break;

            folder = parent.FullName;
        }

        Assert.Fail($"{ENGINE_FILE} was not found walking up from {AppContext.BaseDirectory}");

        return "";
    }
}
