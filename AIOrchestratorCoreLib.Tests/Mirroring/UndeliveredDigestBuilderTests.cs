using System.Text;
using AIOrchestratorCoreLib.Mirroring;
using Xunit;

namespace AIOrchestratorCoreLib.Tests.Mirroring;

public class UndeliveredDigestBuilderTests
{
    [Fact]
    public void Build_CaptionHtml_OneEntry_SaysEntryNotEntries_AndCarriesTheSingleTime()
    {
        var when = new DateTime(2026, 9, 10, 14, 30, 0, DateTimeKind.Utc);

        var caption = UndeliveredDigest_Builder.Build_CaptionHtml(1, when, when);

        Assert.Contains("1 entry ", caption, StringComparison.Ordinal);
        Assert.DoesNotContain("1 entries", caption, StringComparison.Ordinal);
        Assert.Contains("at 14:30", caption, StringComparison.Ordinal);
    }

    [Fact]
    public void Build_CaptionHtml_SeveralEntries_CarriesBothEndsOfTheSpan()
    {
        var first = new DateTime(2026, 9, 10, 14, 30, 0, DateTimeKind.Utc);
        var last = new DateTime(2026, 9, 10, 15, 5, 0, DateTimeKind.Utc);

        var caption = UndeliveredDigest_Builder.Build_CaptionHtml(3, first, last);

        Assert.Contains("3 entries", caption, StringComparison.Ordinal);
        Assert.Contains("from 14:30 to 15:05", caption, StringComparison.Ordinal);
    }

    [Fact]
    public void Build_Content_IsUtf8WithNoByteOrderMark()
    {
        var bytes = UndeliveredDigest_Builder.Build_Content([
            (new DateTime(2026, 9, 10, 14, 30, 0, DateTimeKind.Utc), "sup", "subject one", "body one"),
        ]);

        var bom = new UTF8Encoding(true).GetPreamble();

        Assert.True(bytes.Length >= bom.Length);
        Assert.False(bytes.Take(bom.Length).SequenceEqual(bom));
    }

    [Fact]
    public void Build_Content_CarriesEverySubjectAndBody_VerbatimAndInOrder_OldestFirst()
    {
        var entries = new List<(DateTime WhenUtc, string Author, string Subject, string Body)>
        {
            (new DateTime(2026, 9, 10, 14, 30, 0, DateTimeKind.Utc), "sup", "first subject", "first body"),
            (new DateTime(2026, 9, 10, 14, 45, 0, DateTimeKind.Utc), "imp-1", "second subject", "second body"),
            (new DateTime(2026, 9, 10, 15, 0, 0, DateTimeKind.Utc), "sup", "third subject", "third body"),
        };

        var text = new UTF8Encoding(false).GetString(UndeliveredDigest_Builder.Build_Content(entries));

        Assert.Contains("first subject", text, StringComparison.Ordinal);
        Assert.Contains("first body", text, StringComparison.Ordinal);
        Assert.Contains("second subject", text, StringComparison.Ordinal);
        Assert.Contains("second body", text, StringComparison.Ordinal);
        Assert.Contains("third subject", text, StringComparison.Ordinal);
        Assert.Contains("third body", text, StringComparison.Ordinal);

        var firstIndex = text.IndexOf("first subject", StringComparison.Ordinal);
        var secondIndex = text.IndexOf("second subject", StringComparison.Ordinal);
        var thirdIndex = text.IndexOf("third subject", StringComparison.Ordinal);

        Assert.True(firstIndex < secondIndex);
        Assert.True(secondIndex < thirdIndex);
    }

    [Fact]
    public void MaxParkedEntries_IsAPositiveNumber()
    {
        Assert.True(UndeliveredDigest_Builder.MAX_PARKED_ENTRIES > 0);
    }

    [Fact]
    public void FileName_EndsInMd()
    {
        Assert.EndsWith(".md", UndeliveredDigest_Builder.FILE_NAME, StringComparison.Ordinal);
    }
}
