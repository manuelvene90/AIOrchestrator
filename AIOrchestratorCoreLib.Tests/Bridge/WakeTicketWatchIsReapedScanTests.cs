using Xunit;

namespace AIOrchestratorCoreLib.Tests.Bridge;

/// <summary>
/// THE WATCH MAP HAS AN OWNER THAT EMPTIES IT — pinned by reading the source, in the shape
/// <see cref="AwaySuppressesAppAlertsScanTests"/> established, and for the reason that makes a scan
/// the only honest instrument here: DROPPING A MEMO HAS NO OBSERVABLE BEHAVIOUR. A reaped entry and
/// a retained one produce the same alerts, the same channels and the same Telegram traffic; the only
/// difference is a footprint that grows for the life of the process. There is nothing for an
/// integration test to assert on, and <c>BridgeEngineModel</c> is <c>internal sealed</c> with no
/// <c>InternalsVisibleTo</c>, so there is nothing to read either.
///
/// <para>
/// IT IS A WEAKER CLAIM THAN A BEHAVIOURAL TEST AND IS WRITTEN DOWN AS SUCH: it proves the reap is
/// present, is fed the sweep's own live set, and names the map — not that it runs.
/// <see cref="SessionMemoReaperTests"/> carries the half that can be executed, which is the rule
/// deciding WHICH keys go.
/// </para>
/// </summary>
public class WakeTicketWatchIsReapedScanTests
{
    const string ENGINE_FILE = "BridgeEngineModel.cs";
    const string SWEEP = "async Task Sweep_WakeTickets_Async";
    const string REAPER = "void Reap_WakeTicketWatches";

    /// <summary>A body shorter than this is an extraction that went wrong, not a method.</summary>
    const int PLAUSIBLE_BODY_FLOOR = 200;

    /// <summary>
    /// THE SWEEP READS THE LIVE SET EXACTLY ONCE, and this is the assertion with teeth. Two calls to
    /// <c>Find_All</c> would be two answers to "which sessions exist" with a close able to fall
    /// between them — the reap would then measure the watch map against a roster the walk never used,
    /// and a session registered in one reading and gone in the other would have its watch dropped and
    /// re-armed on the same tick, for ever.
    /// </summary>
    [Fact]
    public void TheSweepResolvesTheLiveSetOnce_AndHandsThatSameSetToTheReap()
    {
        var body = Read_MethodBody(SWEEP);

        Assert.Equal(1, body.Split("RegisteredSessions_Reader.Find_All").Length - 1);
        Assert.Contains("Reap_WakeTicketWatches(registrations)", body);
        Assert.Contains("foreach (var registered in registrations)", body);
    }

    /// <summary>
    /// AND THE REAP IS ABOUT THIS MAP, through the one component that decides which keys have
    /// outlived their sessions. Without the <c>Remove</c> the call would be a no-op that the test
    /// above would still applaud.
    /// </summary>
    [Fact]
    public void TheReapDropsFromTheWatchMap_ThroughTheSharedRule()
    {
        var body = Read_MethodBody(REAPER);

        Assert.Contains("SessionMemo_Reaper.Find_Orphans", body);
        Assert.Contains("_wakeTicketWatchByStateFile.Remove(", body);
    }

    static string Read_MethodBody(string signatureMark)
    {
        var source = Read_EngineSource();

        var at = source.IndexOf(signatureMark, StringComparison.Ordinal);

        Assert.True(at >= 0, $"'{signatureMark}' is not in {ENGINE_FILE} — this scan cannot prove anything about a method it cannot find");

        var open = source.IndexOf('{', at);

        Assert.True(open >= 0, $"no body found for '{signatureMark}'");

        var depth = 0;

        for (var i = open; i < source.Length; i++)
        {
            // Line comments are skipped: this file's prose is long and quotes braces.
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
                {
                    var body = source[open..(i + 1)];

                    Assert.True(
                        body.Length >= PLAUSIBLE_BODY_FLOOR,
                        $"extracted {body.Length} chars for '{signatureMark}' — that is a fragment, not a method body");

                    return body;
                }
            }
        }

        Assert.Fail($"unbalanced braces walking '{signatureMark}' — the extraction is unreliable, so this scan refuses to report");

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

        // NOT an empty string: a scan that cannot find its subject must fail loudly rather than
        // certify the absence of the thing it is testing (CLAUDE.md decision 20).
        Assert.Fail($"{ENGINE_FILE} was not found walking up from {AppContext.BaseDirectory} — this scan can prove nothing");

        return "";
    }
}
