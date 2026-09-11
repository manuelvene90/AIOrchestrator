using System.Text;
using AIOrchestratorCoreLib.Mirroring;
using AIOrchestratorCoreLib.Telegram;
using Xunit;

namespace AIOrchestratorCoreLib.Tests.Mirroring;

/// <summary>
/// THE ATTACHMENT IS A CONVENIENCE AND ITS NAME COMES FROM AN AGENT.
///
/// <para>
/// Two things are pinned here. The first is that the subject — which CLAUDE.md decision 12 calls
/// untrusted input, because it is whatever a session typed into a channel header — cannot become a
/// path: no separator, no dot, no control character reaches the file name. The second is that the
/// caption is trimmed on the MARKDOWN and rendered afterwards, because cutting HTML at Telegram's
/// 1024 can cut a tag in half, and a caption that will not parse fails the whole upload.
/// </para>
/// </summary>
public class OwnerDocumentBuilderTests
{
    [Fact]
    public void TheFileName_IsTheSubjectSlugged()
    {
        Assert.Equal("the-sweep.md", OwnerDocument_Builder.Build_FileName("The Sweep"));
        Assert.Equal("goal-2-v1-live.md", OwnerDocument_Builder.Build_FileName("GOAL 2: V1 LIVE"));
    }

    /// <summary>
    /// A SUBJECT IS NOT A PATH. A slash, a backslash, a dot-dot or a NUL in a name handed to a
    /// multipart upload is the kind of thing that stops being cosmetic somewhere downstream.
    /// </summary>
    [Fact]
    public void ASubjectThatLooksLikeAPath_CannotBecomeOne()
    {
        var name = OwnerDocument_Builder.Build_FileName("../../etc/passwd\0");

        Assert.Equal("etc-passwd.md", name);
        Assert.DoesNotContain('/', name);
        Assert.DoesNotContain('\\', name);
        Assert.Equal(1, name.Count(character => character == '.'));
    }

    [Fact]
    public void ASubjectWithNothingUsableInIt_FallsBackToAFixedName()
    {
        Assert.Equal("entry.md", OwnerDocument_Builder.Build_FileName("— — —"));
        Assert.Equal("entry.md", OwnerDocument_Builder.Build_FileName(null));
        Assert.Equal("entry.md", OwnerDocument_Builder.Build_FileName(string.Empty));
    }

    [Fact]
    public void ALongSubject_IsCappedAndNeverEndsOnAHyphen()
    {
        var name = OwnerDocument_Builder.Build_FileName(string.Join(' ', Enumerable.Repeat("word", 40)));

        Assert.True(name.Length <= OwnerDocument_Builder.SLUG_LIMIT + 3, name);
        Assert.DoesNotContain("-.md", name, StringComparison.Ordinal);
    }

    [Fact]
    public void TheContent_IsTheMarkdownAsUtf8_WithNoByteOrderMark()
    {
        var bytes = OwnerDocument_Builder.Build_Content("**bold** — è");

        Assert.Equal("**bold** — è", Encoding.UTF8.GetString(bytes));
        Assert.False(bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF, "the file opens with a BOM");
    }

    [Fact]
    public void TheCaption_IsTheFirstLineRendered()
    {
        Assert.Equal("<b>THE SWEEP</b>", OwnerDocument_Builder.Build_CaptionHtml("**THE SWEEP**\nand then the body"));
    }

    /// <summary>
    /// TRIMMED ON THE MARKDOWN, so what Telegram receives is always a balanced set of tags. Rendering
    /// first and cutting the HTML would eventually cut one in half — a 400 that fails the upload.
    /// </summary>
    [Fact]
    public void AFirstLineLongerThanTelegramsCaption_IsTrimmedWithoutEverCuttingATag()
    {
        var caption = OwnerDocument_Builder.Build_CaptionHtml(new string('z', 4_000) + "\nbody");

        Assert.True(caption.Length <= OwnerDocument_Builder.CAPTION_LIMIT, $"caption is {caption.Length} characters");
        Assert.Equal(new string('z', caption.Length), caption);
    }

    /// <summary>
    /// MEASURED AS TELEGRAM MEASURES IT (brief F4, 2026-09-10): the caption cap, like the message
    /// cap, counts characters AFTER entity parsing [documented], so the <c>&amp;amp;</c> expansions
    /// this fixture is made of do not each cost five. The property is unchanged — the caption must
    /// be one Telegram accepts, because a refused caption fails the whole sendDocument and takes
    /// the file with it.
    /// </summary>
    [Fact]
    public void AnEscapableFirstLine_StillFitsTheCaptionCap()
    {
        var caption = OwnerDocument_Builder.Build_CaptionHtml(string.Concat(Enumerable.Repeat("<a> & ", 600)) + "\nbody");

        var parsed = TelegramText_Ruler.Count_AfterEntityParsing(caption);

        Assert.True(parsed <= OwnerDocument_Builder.CAPTION_LIMIT, $"caption is {parsed} characters after entity parsing");
        Assert.DoesNotContain("<a>", caption, StringComparison.Ordinal);
    }

    [Fact]
    public void TheAttachment_FiresOnlyPastTheConfiguredNumberOfMessages()
    {
        Assert.False(OwnerDocument_Builder.Should_Attach(3, OwnerDocument_Builder.DEFAULT_ATTACH_ABOVE_CHUNKS));
        Assert.True(OwnerDocument_Builder.Should_Attach(4, OwnerDocument_Builder.DEFAULT_ATTACH_ABOVE_CHUNKS));
    }

    /// <summary>Zero is the owner's off switch, and it must hold for any number of messages.</summary>
    [Fact]
    public void AZeroAttachThreshold_NeverAttaches()
    {
        Assert.False(OwnerDocument_Builder.Should_Attach(1, 0));
        Assert.False(OwnerDocument_Builder.Should_Attach(50, 0));
    }
}
