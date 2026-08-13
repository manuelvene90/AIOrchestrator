using AIOrchestratorCoreLib.Channels;
using AIOrchestratorCoreLib.Channels.ChannelEntry;
using AIOrchestratorCoreLib.Channels.DiscoveredChannel;
using AIOrchestratorCoreLib.Status;
using Xunit;

namespace AIOrchestratorCoreLib.Tests.Status;

/// <summary>
/// A hook that cannot evaluate its predicate allows the call and says so — and the saying-so is the
/// half that was missing, because the app's log panel is fed by an in-process event a separate
/// process can never raise. So the hook drops a marker and the app writes the record.
///
/// THE READER IS TOLERANT ON PURPOSE. Its writer is a shell script running on a machine that, by the
/// nature of what it is reporting, may be unable to fork — a truncated marker is a realistic input,
/// not a hypothetical one. A marker that exists at all is a fact worth reporting even when its detail
/// is missing, because the alternative is discarding the only evidence that a guard stopped working.
/// </summary>
public class GuardNotInForceMarkerTests
{
    [Fact]
    public void AWholeMarkerNamesTheHookThePredicateAndTheReason()
    {
        var description = GuardNotInForce_Marker.Describe_OrNull(
            "supervisor-awaiting-answer-check.sh\nwhich tool is being called\nno tool name could be extracted");

        Assert.NotNull(description);
        Assert.Contains("supervisor-awaiting-answer-check.sh", description);
        Assert.Contains("which tool is being called", description);
        Assert.Contains("no tool name could be extracted", description);
        Assert.Contains("not in force", description);
    }

    /// <summary>
    /// A marker cut off mid-write still reports. The machine that truncates it is the machine the
    /// marker is about, so discarding it would lose the evidence exactly when it is most true.
    /// </summary>
    [Theory]
    [InlineData("supervisor-ledger-check.sh")]
    [InlineData("supervisor-ledger-check.sh\nwhich tool is being called")]
    [InlineData("supervisor-ledger-check.sh\n\n")]
    public void ATruncatedMarkerStillReports(string markerText)
    {
        var description = GuardNotInForce_Marker.Describe_OrNull(markerText);

        Assert.NotNull(description);
        Assert.Contains("supervisor-ledger-check.sh", description);
        Assert.Contains("not in force", description);
    }

    /// <summary>Nothing at all is the one case with nothing to say.</summary>
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\n\n")]
    public void AnEmptyMarkerSaysNothing(string markerText)
    {
        Assert.Null(GuardNotInForce_Marker.Describe_OrNull(markerText));
    }

    /// <summary>Windows line endings, because the writer is a shell script on Windows.</summary>
    [Fact]
    public void CarriageReturnsDoNotLeakIntoTheMessage()
    {
        var description = GuardNotInForce_Marker.Describe_OrNull("hook.sh\r\nthe predicate\r\nthe reason\r\n");

        Assert.NotNull(description);
        Assert.DoesNotContain("\r", description);
        Assert.Contains("the predicate", description);
    }

    /// <summary>
    /// NEVER TELEGRAM. The owner cannot fix a hook mid-turn, and rule 15 keeps what they cannot act
    /// on off their phone — the supervisor's channel and the app UI are where this belongs. Asserted
    /// through the real predicate so a rename cannot be got half right.
    /// </summary>
    [Fact]
    public void TheEntrySubjectIsSuppressedFromTelegram()
    {
        var entry = ChannelEntry_Factory.Create(
            1, ChannelAuthors.App, "2026-08-12 12:00", GuardNotInForce_Marker.ENTRY_SUBJECT, "body",
            $"## [1] FROM app — 2026-08-12 12:00 — {GuardNotInForce_Marker.ENTRY_SUBJECT}");

        Assert.False(AIOrchestratorCoreLib.Mirroring.MirrorText_Formatter.Should_Mirror(
            DiscoveredChannel_Factory.Create_ForOwner("orch-1", "owner-channel.md"), entry));
    }

    /// <summary>
    /// The hook and the reader must agree on the file name, and they are in different languages. This
    /// asserts the constant against the shell that writes it, which is the only thing joining them.
    /// </summary>
    [Fact]
    public void TheHookWritesTheFileNameTheAppLooksFor()
    {
        var hookScript = Find_HookLogScript_OrNull();

        Assert.NotNull(hookScript);
        Assert.Contains(GuardNotInForce_Marker.FILE_NAME, File.ReadAllText(hookScript));
    }

    /// <summary>
    /// THE FIELDS THAT SEPARATE A TEST FROM AN INCIDENT. On 2026-08-13 three alerts named
    /// `reviewer-readonly-check.sh` and were read as the shipped guard failing; the reason text they
    /// carried existed only in an implementer's unmerged worktree copy, so a branch under test was
    /// indistinguishable from a production failure, and a reviewer was sent after a payload that did
    /// not exist.
    /// </summary>
    [Fact]
    public void AWholeMarkerNamesWhoTrippedItAndWhichCopyWroteIt()
    {
        var description = GuardNotInForce_Marker.Describe_OrNull(
            "reviewer-readonly-check.sh\nwhether this command mutates anything\na redirect target this scanner cannot resolve\nimp-1\n89c606d67e51fb6806e2b51fd394c5aa");

        Assert.NotNull(description);
        Assert.Contains("imp-1", description);
        Assert.Contains("89c606d67e51fb6806e2b51fd394c5aa", description);
        Assert.Contains("installed copy", description);
    }

    /// <summary>
    /// The tolerance that already existed must survive the two new fields: a three-line marker from an
    /// older hook, or a write that stopped early, still reports — and does so without a dangling
    /// half-sentence where the missing fields would have gone.
    /// </summary>
    [Theory]
    [InlineData("reviewer-readonly-check.sh\nthe predicate\nthe reason")]
    [InlineData("reviewer-readonly-check.sh\nthe predicate\nthe reason\n\n")]
    public void AMarkerWithoutTheNewFieldsReportsCleanly(string markerText)
    {
        var description = GuardNotInForce_Marker.Describe_OrNull(markerText);

        Assert.NotNull(description);
        Assert.Contains("the reason", description);
        Assert.EndsWith("that guard is not in force.", description);
    }

    /// <summary>
    /// Each field stands alone. A machine that cannot fork may have no md5sum, and a session outside
    /// an orchestration has no member id — either one arriving without the other still says something
    /// worth having, and neither may drag in the other's wording.
    /// </summary>
    [Fact]
    public void EitherIdentifyingFieldAloneStillSpeaks()
    {
        var memberOnly = GuardNotInForce_Marker.Describe_OrNull("hook.sh\nthe predicate\nthe reason\nrev-6\n");

        Assert.NotNull(memberOnly);
        Assert.Contains("rev-6", memberOnly);
        Assert.DoesNotContain("md5", memberOnly);

        var fingerprintOnly = GuardNotInForce_Marker.Describe_OrNull("hook.sh\nthe predicate\nthe reason\n\nabc123");

        Assert.NotNull(fingerprintOnly);
        Assert.Contains("abc123", fingerprintOnly);
        Assert.Contains("md5", fingerprintOnly);
    }

    /// <summary>
    /// The two fields are written in bash and read in C#, so nothing but this asserts that the writer
    /// still writes them. It fails LOUDLY when the script cannot be found rather than passing on an
    /// empty read — a harness that cannot find what it tests must refuse to certify it.
    /// </summary>
    [Fact]
    public void TheHookWritesTheIdentifyingFieldsTheAppReads()
    {
        var hookScript = Find_HookLogScript_OrNull();

        Assert.NotNull(hookScript);

        var shell = File.ReadAllText(hookScript);

        Assert.Contains("AIORCH_MEMBER", shell);
        Assert.Contains("md5sum", shell);
    }

    static string? Find_HookLogScript_OrNull()
    {
        var folder = AppContext.BaseDirectory;

        for (var depth = 0; depth < 8; depth++)
        {
            var candidate = Path.Combine(folder, "kit", "hooks", "hook-log.sh");

            if (File.Exists(candidate))
                return candidate;

            var parent = Directory.GetParent(folder);

            if (parent == null)
                return null;

            folder = parent.FullName;
        }

        return null;
    }
}
