using Xunit;

namespace AIOrchestratorCoreLib.Tests.Bridge;

/// <summary>
/// AWAY MODE MUST ACTUALLY SUPPRESS SOMETHING — the finding behind the 2026-08-19 fix.
///
/// Before it, `Is_AwayMode()` had four call sites in the whole library: two topic-name glyph
/// renders, one that ADDED the 30-minute digest, and the getter. It gated ZERO outbound messages.
/// So `AwayMode_Policy.AWAY_ON_NOTICE`'s promise — *"will not ask you anything else"* — was kept
/// only by the per-orchestration QUIET state, which does not cover the app's OWN alerts, and the
/// owner woke to about 33 messages.
///
/// Driving the engine into away mode from a test needs fifteen minutes of owner silence and a
/// tracker already quiet, with no seam to force either — so this pins the wiring by reading the
/// source, the same shape as <c>MemoKeyCompositionScanTests</c>. It is a WEAKER claim than an
/// integration test and is written down as such: it proves the gate is present and placed, not that
/// it fires. <see cref="AwayDigestDeciderTests"/> carries the behavioural half.
/// </summary>
public class AwaySuppressesAppAlertsScanTests
{
    const string ENGINE_FILE = "BridgeEngineModel.cs";
    const string AWAY_GATE = "Is_AwayMode()";

    /// <summary>Below this the extraction grabbed a fragment, not a method, and proves nothing.</summary>
    const int PLAUSIBLE_BODY_FLOOR = 200;

    [Fact]
    public void TheStallAlertIsSilentWhileTheOwnerIsAway()
    {
        var body = Extract_Method("async Task Send_StallAlerts_Async");

        // THE HARNESS PROVES IT FOUND THE RIGHT METHOD FIRST — this assertion is about a presence in
        // a region, and a region extracted from the wrong place would be judged just as confidently.
        Assert.Contains("waiting on your reply", body);

        Assert.Contains(AWAY_GATE, body);
    }

    [Fact]
    public void TheSilentDeadlockReleaseIsHeldWhileTheOwnerIsAway()
    {
        var body = Extract_Method("async Task Break_SilentDeadlock_Async");

        Assert.Contains("nothing has moved for", body);

        Assert.Contains(AWAY_GATE, body);
    }

    /// <summary>
    /// THE LOAD-BEARING HALF, and the one a careless edit would lose: the away gate sits ABOVE the
    /// removal, so a held entry is KEPT and released on the first tick after the owner returns.
    /// Below the removal it would still stop the message and would silently destroy it instead —
    /// the same outcome the owner is complaining about, arrived at from the opposite direction.
    /// </summary>
    [Fact]
    public void TheHeldEntryIsKeptRatherThanConsumed()
    {
        var body = Extract_Method("async Task Break_SilentDeadlock_Async");

        var gate = body.IndexOf(AWAY_GATE, StringComparison.Ordinal);
        var removal = body.IndexOf("_lastSuppressedEntry.Remove", StringComparison.Ordinal);

        Assert.True(gate >= 0, "no away gate in the deadlock release");
        Assert.True(removal >= 0, "the deadlock release no longer removes the suppressed entry — this test is reading a method it does not understand");

        Assert.True(
            gate < removal,
            "the away gate sits BELOW the removal: while away the suppressed entry is consumed and never released, so the owner loses it instead of receiving it late");
    }

    /// <summary>The digest is change-gated in exactly one place — a second copy is the defect CLAUDE.md decision 12 names.</summary>
    [Fact]
    public void TheAwayDigestIsChangeGatedExactlyOnce()
    {
        var source = Read_EngineSource();

        var occurrences = source.Split("AwayDigest_Decider.Should_Send").Length - 1;

        Assert.Equal(1, occurrences);
    }

    /// <summary>
    /// A DIGEST IS REMEMBERED ONLY AFTER IT IS WRITTEN, and this ordering is load-bearing precisely
    /// BECAUSE the digest is change-gated: remembering one that was never appended — a channel locked
    /// for the whole budget — means the identical digest is never sent again, so the away spell goes
    /// silent entirely rather than merely late. `Post_StatusEntry`'s own comment named that invariant
    /// ("nothing records it as done, so nothing is left claiming work that did not happen") while
    /// this caller was briefly the thing breaking it.
    /// </summary>
    [Fact]
    public void TheAwayDigestIsRememberedOnlyAfterAConfirmedWrite()
    {
        var body = Extract_Method("async Task Push_PeriodicStatus_Async");

        Assert.Contains("AwayDigest_Decider.Should_Send", body);

        var post = body.IndexOf("Post_StatusEntry(session.OrchId, digest", StringComparison.Ordinal);
        var remember = body.IndexOf("Remember_AwayDigest", StringComparison.Ordinal);

        Assert.True(post >= 0, "the away branch no longer posts the digest — this test is reading a method it does not understand");
        Assert.True(remember >= 0, "the away digest is no longer remembered, so the change-gate has nothing to compare against");

        Assert.True(
            post < remember,
            "the digest is remembered BEFORE the post: an append dropped by a locked channel would count as delivered, and because an unchanged digest is never re-sent that away spell goes silent entirely");
    }

    static string Extract_Method(string signatureMark)
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
