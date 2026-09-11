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
///
/// It also pins the away DECISION's wiring, for the same reason and by the same means: on
/// 2026-09-07 the owner reported away mode declaring them absent while they sat at a terminal with
/// `/pc` set, and the fix is a flag the pure policy cannot prove anybody passes it.
/// </summary>
public class AwaySuppressesAppAlertsScanTests
{
    const string ENGINE_FILE = "BridgeEngineModel.cs";
    const string AWAY_GATE = "Is_AwayMode()";

    /// <summary>A body shorter than this is an extraction that went wrong, not a method.</summary>
    const int PLAUSIBLE_BODY_FLOOR = 200;

    /// <summary>
    /// THE SILENT-DEADLOCK NET IS GONE, and these two tests pinned its away-mode behaviour: that the
    /// release was HELD while the owner was away, and that the held entry was kept rather than
    /// consumed. Both were right about a net that existed to rescue entries
    /// <c>OwnerPush_Policy</c> had suppressed as narration.
    ///
    /// <para>
    /// Nothing is suppressed since 2026-09-09 (the owner's ruling: everything the supervisor writes
    /// reaches the phone), so nothing could ever feed the net again — and its one remaining input
    /// was an EMPTY entry, which it would have released five minutes later, ringing, wearing
    /// "nothing has moved". The machinery was removed rather than left as a net under a filled hole.
    /// </para>
    /// <para>
    /// What replaces the coverage is the absence itself: if any of it comes back, the entry it would
    /// rescue is one the owner has already read.
    /// </para>
    /// </summary>
    [Fact]
    public void TheSilentDeadlockNet_IsGone_NowThatNothingIsEverSuppressed()
    {
        var source = Read_EngineSource();

        Assert.DoesNotContain("Break_SilentDeadlock_Async", source);
        Assert.DoesNotContain("_lastSuppressedEntry", source);
        Assert.DoesNotContain("SILENT_DEADLOCK_MINUTES", source);
    }

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
        var body = Extract_Method("async Task Push_AwayDigests_Async");

        Assert.Contains("AwayDigest_Decider.Should_Send", body);

        var post = body.IndexOf("Post_StatusEntry(session.OrchId, digest", StringComparison.Ordinal);
        var remember = body.IndexOf("Remember_AwayDigest", StringComparison.Ordinal);

        Assert.True(post >= 0, "the away branch no longer posts the digest — this test is reading a method it does not understand");
        Assert.True(remember >= 0, "the away digest is no longer remembered, so the change-gate has nothing to compare against");

        Assert.True(
            post < remember,
            "the digest is remembered BEFORE the post: an append dropped by a locked channel would count as delivered, and because an unchanged digest is never re-sent that away spell goes silent entirely");
    }

    /// <summary>
    /// THE PC GUARD MUST BE WIRED, not merely written. <see cref="AwayModePolicyTests"/> pins that
    /// <c>Should_EnterAway</c> refuses while the owner is at a pc — but a policy nothing hands the
    /// flag to is a green suite sitting on top of the untouched bug, which is exactly the failure
    /// <c>EveryTopicButtonIsWiredTests</c> was written for in another corner of this engine.
    ///
    /// The owner's report, 2026-09-07: *"when I'm at the pc... automatic away mode should never
    /// happen"*. Their log: da-vinci-fintech-suite-26 held Terminal presence from 09:53 and away
    /// mode called them unresponsive app-wide at 10:23:50.
    /// </summary>
    [Fact]
    public void TheAwayCheck_AsksWhetherTheOwnerIsAtAPc_AndPassesTheAnswerOn()
    {
        var body = Extract_Method("async Task Check_AwayMode_Async");

        // The harness proves it found the right method before judging what is inside it.
        Assert.Contains("_awayTrackers", body);

        Assert.Contains("Is_OwnerAtThePc()", body);

        // COMPUTED IS NOT PASSED. Asserting only on the call above would stay green if the result
        // were dropped on the floor, which is the likeliest way for this fix to rot.
        Assert.Contains("Should_EnterAway(anyQuiet, ownerAtAPc", body);
        Assert.Contains("Should_LeaveAway(_awayActive, ownerAtAPc)", body);
    }

    /// <summary>
    /// AND THE PREDICATE HAS TO SEE GENERAL. It answers "is the owner at a terminal ANYWHERE", but
    /// it walks the session roster and General keeps no session.json — so a `/pc` held only in
    /// General, the topic the owner talks to most, used to read as nobody being at a keyboard.
    ///
    /// <para>
    /// THE ROSTER IS READ THROUGH <c>Sessions_ThisTick()</c>, which is the tick's own snapshot while a
    /// tick is running and a fresh <c>Load_All()</c> otherwise. This assertion named <c>Load_All()</c>
    /// until 2026-09-09; what it is actually about is that EVERY orchestration is consulted and not
    /// only General, and the accessor is that. Both callers of this predicate are the tick's own
    /// (<c>Check_AwayMode_Async</c> and the status push), so there is no flow here that could be
    /// handed a roster belonging to somebody else's tick.
    /// </para>
    /// </summary>
    [Fact]
    public void BeingAtAPc_CountsTheGeneralTopicToo()
    {
        var body = Extract_Method("bool Is_OwnerAtThePc()");

        Assert.Contains("Sessions_ThisTick()", body);

        Assert.Contains("GENERAL_ORCH_ID", body);
    }

    /// <summary>
    /// A COMMAND-BAR TAP IS THE OWNER SPEAKING. The close-confirmation tap already said so and this
    /// bar never did, which stopped mattering the moment `/pc` became one of its buttons on
    /// 2026-09-07: tapping "I am at my pc" would set presence and leave the away spell standing.
    /// </summary>
    [Fact]
    public void ACommandBarTap_CountsAsTheOwnerSpeaking()
    {
        var body = Extract_Method("async Task<bool> Try_HandleTopicCommandTap_Async");

        Assert.Contains("TopicCommandButtons.Parse_OrNull", body);

        Assert.Contains("Note_OwnerSpoke_AndWasAway()", body);
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
