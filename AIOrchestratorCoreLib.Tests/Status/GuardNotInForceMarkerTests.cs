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
