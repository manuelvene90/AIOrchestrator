using AIOrchestratorCoreLib.Bridge;
using Xunit;

namespace AIOrchestratorCoreLib.Tests.Bridge;

/// <summary>
/// The pure decision behind <c>ATTACH: &lt;path&gt;</c>: which files an agent may send the owner.
/// </summary>
public class EntryAttachmentPolicyTests
{
    static readonly string Repo = Path.Combine(Path.GetTempPath(), "aiorch-attach-repo");
    static readonly string Channel = Path.Combine(Path.GetTempPath(), "aiorch-attach-channel");
    static readonly IReadOnlyList<string> Roots = [Repo, Channel];

    [Fact]
    public void AFileUnderTheRepo_IsSent()
    {
        var verdict = EntryAttachment_Policy.Decide(Path.Combine(Repo, "mockups", "plan-card.html"), Roots, exists: true, lengthBytes: 12_000);
        Assert.Equal(AttachmentVerdicts.Send, verdict);
    }

    [Fact]
    public void AFileUnderTheChannelFolder_IsSent()
    {
        var verdict = EntryAttachment_Policy.Decide(Path.Combine(Channel, "media", "report.md"), Roots, exists: true, lengthBytes: 500);
        Assert.Equal(AttachmentVerdicts.Send, verdict);
    }

    [Fact]
    public void ASiblingFolderWhoseNameMerelyStartsWithTheRoot_IsOutside()
    {
        // `…/aiorch-attach-repo-evil/x.html` starts with the root's characters and is not under it.
        var verdict = EntryAttachment_Policy.Decide(Repo + "-evil" + Path.DirectorySeparatorChar + "x.html", Roots, exists: true, lengthBytes: 10);
        Assert.Equal(AttachmentVerdicts.OutsideAllowedRoots, verdict);
    }

    [Fact]
    public void ATraversalOutOfTheRoot_IsOutside()
    {
        var verdict = EntryAttachment_Policy.Decide(Path.Combine(Repo, "..", "id_rsa"), Roots, exists: true, lengthBytes: 10);
        Assert.Equal(AttachmentVerdicts.OutsideAllowedRoots, verdict);
    }

    [Fact]
    public void AMissingFile_IsReportedBeforeAnythingElse()
    {
        var verdict = EntryAttachment_Policy.Decide(Path.Combine(Repo, "gone.html"), Roots, exists: false, lengthBytes: 0);
        Assert.Equal(AttachmentVerdicts.MissingFile, verdict);
    }

    [Fact]
    public void AFileOverTelegramsDocumentCap_IsTooLarge()
    {
        var verdict = EntryAttachment_Policy.Decide(Path.Combine(Repo, "dump.bin"), Roots, exists: true, lengthBytes: EntryAttachment_Policy.MAX_BYTES + 1);
        Assert.Equal(AttachmentVerdicts.TooLarge, verdict);
    }

    [Fact]
    public void NoAllowedRoots_MeansNothingIsSendable()
    {
        var verdict = EntryAttachment_Policy.Decide(Path.Combine(Repo, "x.html"), [], exists: true, lengthBytes: 10);
        Assert.Equal(AttachmentVerdicts.OutsideAllowedRoots, verdict);
    }

    [Fact]
    public void EveryRefusal_NamesTheRuleAndThePath()
    {
        foreach (var verdict in new[] { AttachmentVerdicts.MissingFile, AttachmentVerdicts.OutsideAllowedRoots, AttachmentVerdicts.TooLarge })
        {
            var text = EntryAttachment_Policy.Describe(verdict, "/x/y.html", Roots);
            Assert.Contains("/x/y.html", text, StringComparison.Ordinal);
            Assert.Contains("ATTACH", text, StringComparison.Ordinal);
        }

        Assert.Contains(Repo, EntryAttachment_Policy.Describe(AttachmentVerdicts.OutsideAllowedRoots, "/x/y.html", Roots), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(".png")]
    [InlineData(".jpg")]
    [InlineData(".JPEG")]
    [InlineData(".webp")]
    [InlineData(".gif")]
    public void APictureIsSentAsAPicture(string extension)
    {
        var verdict = EntryAttachment_Policy.Decide(Path.Combine(Repo, "shot" + extension), Roots, exists: true, lengthBytes: 500_000, asPicture: true);
        Assert.Equal(AttachmentVerdicts.Send, verdict);
    }

    [Theory]
    [InlineData(".html")]
    [InlineData(".csv")]
    [InlineData(".md")]
    [InlineData("")]
    public void ANonPictureUnderIMAGE_IsRefused_RatherThanHandedToTelegramToReject(string extension)
    {
        // Measured 2026-09-08: four HTML mockups went out as IMAGE:, Telegram answered
        // `400 IMAGE_PROCESS_FAILED` to each, and the only trace was a log line nobody reads.
        var verdict = EntryAttachment_Policy.Decide(Path.Combine(Repo, "mockup" + extension), Roots, exists: true, lengthBytes: 30_000, asPicture: true);
        Assert.Equal(AttachmentVerdicts.NotAPicture, verdict);

        // The same file is perfectly sendable as a document — which is what the refusal must say.
        Assert.Equal(
            AttachmentVerdicts.Send,
            EntryAttachment_Policy.Decide(Path.Combine(Repo, "mockup" + extension), Roots, exists: true, lengthBytes: 30_000, asPicture: false));
    }

    [Fact]
    public void TheRefusalForANonPicture_NamesTheOtherMarker_BecauseThatIsTheFix()
    {
        var text = EntryAttachment_Policy.Describe(AttachmentVerdicts.NotAPicture, "/x/mockup.html", Roots, asPicture: true);

        Assert.Contains("ATTACH: /x/mockup.html", text, StringComparison.Ordinal);
        Assert.Contains("IMAGE_PROCESS_FAILED", text, StringComparison.Ordinal);
    }

    [Fact]
    public void APictureHasTelegramsSmallerCap_AndTheRefusalPointsAtTheOtherMarker()
    {
        Assert.Equal(
            AttachmentVerdicts.TooLarge,
            EntryAttachment_Policy.Decide(Path.Combine(Repo, "huge.png"), Roots, exists: true, lengthBytes: EntryAttachment_Policy.MAX_PICTURE_BYTES + 1, asPicture: true));

        // The same size is fine as a document, and the refusal says so rather than leaving the agent
        // to guess that the two markers have different ceilings.
        Assert.Equal(
            AttachmentVerdicts.Send,
            EntryAttachment_Policy.Decide(Path.Combine(Repo, "huge.png"), Roots, exists: true, lengthBytes: EntryAttachment_Policy.MAX_PICTURE_BYTES + 1, asPicture: false));

        Assert.Contains(
            "ATTACH",
            EntryAttachment_Policy.Describe(AttachmentVerdicts.TooLarge, "/x/huge.png", Roots, asPicture: true),
            StringComparison.Ordinal);
    }

    [Fact]
    public void EveryRefusal_NamesTheMarkerItIsAbout()
    {
        foreach (var verdict in new[] { AttachmentVerdicts.MissingFile, AttachmentVerdicts.OutsideAllowedRoots, AttachmentVerdicts.TooLarge, AttachmentVerdicts.NotAPicture })
        {
            Assert.Contains("IMAGE", EntryAttachment_Policy.Describe(verdict, "/x/y.png", Roots, asPicture: true), StringComparison.Ordinal);
        }

        foreach (var verdict in new[] { AttachmentVerdicts.MissingFile, AttachmentVerdicts.OutsideAllowedRoots, AttachmentVerdicts.TooLarge })
        {
            Assert.Contains("ATTACH", EntryAttachment_Policy.Describe(verdict, "/x/y.html", Roots, asPicture: false), StringComparison.Ordinal);
        }
    }
}
