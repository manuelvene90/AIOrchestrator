using Xunit;

namespace AIOrchestratorCoreLib.Tests.Bridge;

/// <summary>
/// THE HALF-HOURLY STATUS MESSAGE IS GONE, AND CANNOT COME BACK BY ACCIDENT.
///
/// <para>
/// WHAT THIS FILE USED TO TEST, and why that claim retired. It pinned `Has_WorkInFlight` — the
/// trigger of the periodic status feed — after `Tear-off tabs` went five hours without a status on
/// 2026-08-20: the ledger said nothing was `[>]`, and "is a member mid-turn?" asked once every
/// thirty minutes fails almost every time. Both fixes were right for a feed that POSTS.
/// </para>
/// <para>
/// The owner removed the feed instead (2026-09-09). It sent a fresh fifteen-line message every half
/// hour — ten in five and a half hours in one topic, three identical with every member closed — and
/// a cadence that posts is a waterfall by construction, however good its content is. So the trigger
/// this file guarded no longer exists, and the claim that replaces it is that the CADENCE is gone:
/// nothing in the engine may post a status entry on a clock again.
/// </para>
/// <para>
/// The original concern — the owner's view of the work must not depend on a session maintaining its
/// ledger — did not retire with it. It moved to the one surface that remains, PULSE, and is tested
/// where that is built (`TopicStatusLineBuilderTests`).
/// </para>
/// <para>
/// A source scan, because the engine is `internal sealed` with no InternalsVisibleTo: the suite
/// cannot call these methods at all. A weak oracle that exists beats a strong one that cannot run.
/// </para>
/// </summary>
public class StatusDoesNotDependOnLedgerHygieneTests
{
    const string ENGINE_FILE = "BridgeEngineModel.cs";

    /// <summary>
    /// The half-hourly poster and its trigger are absent from the engine — not merely unreachable.
    /// A dormant `Has_WorkInFlight` and a `Build_PeriodicStatusText` left in the file are an
    /// invitation to wire them up again, and the owner's decision was about the cadence existing at
    /// all.
    /// </summary>
    [Fact]
    public void ThePeriodicStatusFeed_AndItsTrigger_AreGoneFromTheEngine()
    {
        var source = Read_EngineSource();

        Assert.DoesNotContain("Has_WorkInFlight", source);
        Assert.DoesNotContain("Build_PeriodicStatusText", source);
        Assert.DoesNotContain("Remember_PostedProgress", source);
    }

    /// <summary>
    /// The ONE surviving caller of the status-entry poster is the AWAY digest, which is not a
    /// cadence: it fires only while the owner is away, and only when its content has changed. If a
    /// second caller ever appears, this test is the thing that asks why.
    /// </summary>
    [Fact]
    public void TheOnlyThingThatStillPostsAStatusEntry_IsTheAwayDigest()
    {
        var body = Extract_Method("async Task Push_AwayDigests_Async");

        var posts = body.Split("Post_StatusEntry(").Length - 1;

        Assert.Equal(1, posts);
        Assert.Contains("Is_AwayMode()", body);
        Assert.Contains("AwayDigest_Decider.Should_Send", body);
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
