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
    /// A writer that is not a hook says what it actually did.
    ///
    /// The watcher became the second writer of this marker on 2026-08-14 and it allows no call —
    /// there is no call. Rendering its failed read as "ALLOWED the call" would put a confident false
    /// clause in front of a true one, which is the class of defect this whole marker exists to stop.
    /// </summary>
    [Fact]
    public void AWriterThatIsNotAHookSaysWhatItDidInstead()
    {
        var description = GuardNotInForce_Marker.Describe_OrNull(
            "watcher\nthe channel fingerprint\nmd5sum failed\nimp-8\n\ntook the fingerprint as unknown");

        Assert.NotNull(description);
        Assert.Contains("took the fingerprint as unknown", description);
        Assert.DoesNotContain(GuardNotInForce_Marker.ALLOWED_THE_CALL, description);
        Assert.Contains("not in force", description);
        Assert.Contains("Tripped by imp-8", description);
    }

    /// <summary>
    /// And a marker that does NOT carry one still reads exactly as it always did. Every hook writes
    /// three or five lines and none of them will be changed to add a sixth: allowing IS the hook
    /// contract, so the default is true for them. Asserted across all three sentence shapes, because
    /// the consequence is interpolated into each of them separately.
    /// </summary>
    [Theory]
    [InlineData("hook.sh")]
    [InlineData("hook.sh\nwhich tool is being called")]
    [InlineData("hook.sh\nwhich tool is being called\nno tool name could be extracted")]
    [InlineData("hook.sh\nwhich tool is being called\nno tool name could be extracted\nimp-1\nabc123")]
    public void AMarkerWithoutAConsequenceStillSaysTheCallWasAllowed(string markerText)
    {
        var description = GuardNotInForce_Marker.Describe_OrNull(markerText);

        Assert.NotNull(description);
        Assert.Contains($"and {GuardNotInForce_Marker.ALLOWED_THE_CALL} —", description);
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
    ///
    /// IT ASSERTS ON THE WRITE, NOT ON THE FILE'S TEXT. The first version asserted
    /// `Contains("md5sum")` over the whole script — and `md5sum` appears twice: once as the command
    /// and once in a prose comment saying a machine "may well have no md5sum either". **The comment
    /// alone satisfied it**, so the assertion had two routes to green and pinned neither, and deleting
    /// the printf entirely would not have reddened it. That is the shape that already cost this repo a
    /// guard which stayed green with its check removed.
    ///
    /// So: the printf that writes the marker is located by the file name it redirects to, and the
    /// fields are asserted as ARGUMENTS of that one line. `Single` is deliberate — zero or two such
    /// lines is a refusal, not a pass.
    /// </summary>
    [Fact]
    public void TheHookWritesTheIdentifyingFieldsTheAppReads()
    {
        var hookScript = Find_HookLogScript_OrNull();

        Assert.NotNull(hookScript);

        var lines = File.ReadAllLines(hookScript);

        var writeLine = Assert.Single(lines, line =>
            line.Contains("printf", StringComparison.Ordinal)
            && line.Contains(GuardNotInForce_Marker.FILE_NAME, StringComparison.Ordinal));

        // The fields reach the WRITE — an argument list, not a mention anywhere in the file.
        Assert.Contains("\"$member\"", writeLine);
        Assert.Contains("\"$fingerprint\"", writeLine);

        // Five fields written, five fields the reader parses. A dropped %s silently shifts every
        // field after it, which the reader cannot detect — it would just describe the wrong things.
        Assert.Equal(5, writeLine.Split("%s").Length - 1);
    }

    /// <summary>
    /// And that the two values are COMPUTED rather than merely referenced. Asserted on assignment
    /// lines, so a comment mentioning either name cannot stand in for the code that produces it —
    /// the same two-routes-to-green failure as above, one level up.
    /// </summary>
    [Fact]
    public void TheHookComputesBothIdentifyingFields()
    {
        var hookScript = Find_HookLogScript_OrNull();

        Assert.NotNull(hookScript);

        var lines = File.ReadAllLines(hookScript).Select(line => line.Trim()).ToList();

        Assert.Contains(lines, line => line.StartsWith("member=", StringComparison.Ordinal) && line.Contains("AIORCH_MEMBER", StringComparison.Ordinal));
        Assert.Contains(lines, line => line.StartsWith("fingerprint=", StringComparison.Ordinal) && line.Contains("md5sum", StringComparison.Ordinal));
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
