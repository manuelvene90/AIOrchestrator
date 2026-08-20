using Xunit;

namespace AIOrchestratorCoreLib.Tests.Bridge;

/// <summary>
/// THE OWNER'S STATUS FEED MUST NOT DEPEND ON A SESSION MAINTAINING ITS LEDGER.
///
/// On 2026-08-20 `Tear-off tabs` went five hours without a periodic status while its solo worked
/// the whole time. Has_WorkInFlight asked two questions and got "no" twice: the ledger said nothing
/// was `[>]` (8 done, 3 open, none in progress), and "is a member mid-turn?" was asked once every
/// THIRTY MINUTES — which a session between turns fails almost every time. Neither answer was wrong;
/// together they silenced the feed.
///
/// A ledger nobody updates is a reason to NUDGE THE SESSION — Report_StaleInProgress does that — and
/// never a reason to stop telling the owner what is happening.
///
/// A source scan, because the engine is `internal sealed` with no InternalsVisibleTo: the suite
/// cannot call Has_WorkInFlight at all. A weak oracle that exists beats a strong one that cannot run.
/// </summary>
public class StatusDoesNotDependOnLedgerHygieneTests
{
    const string ENGINE_FILE = "BridgeEngineModel.cs";

    [Fact]
    public void WorkInFlightAsksWhetherAnyoneWorkedRECENTLY_NotOnlyAtThisInstant()
    {
        var body = Extract_Method("bool Has_WorkInFlight");

        // Proves the extraction found the right method before asserting anything about it.
        Assert.Contains("InProgress > 0", body);

        Assert.Contains("Has_AnySessionWorkedWithin", body);

        // The instant-only probe is GONE from this method. It is the half that made a working
        // orchestration look idle, and it would reintroduce the silence on its own.
        Assert.DoesNotContain("Is_MidTurn", body);
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
