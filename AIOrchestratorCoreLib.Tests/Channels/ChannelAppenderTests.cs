using AIOrchestratorCoreLib.Channels;
using Xunit;

namespace AIOrchestratorCoreLib.Tests.Channels;

public class ChannelAppenderTests : IDisposable
{
    readonly string _tempFolder;
    readonly string _channelFile;

    public ChannelAppenderTests()
    {
        _tempFolder = Path.Combine(Path.GetTempPath(), $"aiorch-appender-tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempFolder);
        _channelFile = Path.Combine(_tempFolder, "channel.md");
    }

    public void Dispose()
    {
        Directory.Delete(_tempFolder, recursive: true);
    }

    [Fact]
    public void Append_OwnerEntry_ContinuesExistingNumbering()
    {
        File.WriteAllText(_channelFile,
            "seed\n\n## [1] FROM supervisor — d — greeting\n\nhello\n");

        ChannelAppender.Append_OwnerEntry(_channelFile, "please check the tests", new DateTime(2026, 8, 6, 15, 30, 0));

        var entries = ChannelEntry_Parser.Parse_All(File.ReadAllText(_channelFile));

        Assert.Equal(2, entries.Count);
        Assert.Equal(2, entries[1].Index);
        Assert.Equal(ChannelAuthors.Owner, entries[1].Author);
        Assert.Equal("please check the tests", entries[1].Body);
    }

    [Fact]
    public void Append_OwnerEntry_MissingFile_StartsAtOne()
    {
        ChannelAppender.Append_OwnerEntry(_channelFile, "first message", DateTime.Now);

        var entries = ChannelEntry_Parser.Parse_All(File.ReadAllText(_channelFile));

        Assert.Single(entries);
        Assert.Equal(1, entries[0].Index);
    }

    [Fact]
    public void Append_AppEntry_CarriesSubjectAndAppAuthor()
    {
        ChannelAppender.Append_AppEntry(_channelFile, "orchestration 'x' started", "details here", DateTime.Now);

        var entries = ChannelEntry_Parser.Parse_All(File.ReadAllText(_channelFile));

        Assert.Single(entries);
        Assert.Equal(ChannelAuthors.App, entries[0].Author);
        Assert.Equal("orchestration 'x' started", entries[0].Subject);
    }

    /// <summary>
    /// The parser matches its header regex per line with NO lookback, so the invariant is that an
    /// append BEGINS A LINE — nothing about blank lines, which was believed briefly on 2026-08-13
    /// and disproved by reading the parser. The case that could break it is a channel not ending in
    /// a newline (a fresh seed stops at its "---" rule): the header must still start its own line
    /// rather than continue that one.
    /// </summary>
    [Fact]
    public void Append_ToAFileNotEndingInANewline_StillStartsTheHeaderOnItsOwnLine()
    {
        File.WriteAllText(_channelFile, "seed\n\n---");

        ChannelAppender.Append_AppEntry(_channelFile, "a subject", "a body", DateTime.Now);

        var text = File.ReadAllText(_channelFile);

        // The positive assertion: the entry is readable, and the "---" it was appended after is
        // still its own intact line rather than the head of a run-together one.
        Assert.Single(ChannelEntry_Parser.Parse_All(text));
        Assert.Contains("\n---\n", text);
        Assert.DoesNotContain("---##", text);
    }

    /// <summary>
    /// The other half of the same invariant, and the reason to state it THIS way: "ends with a
    /// newline" is checkable in one command, where "has a blank line before the header" is
    /// invisible in review. A rule nobody can check is a rule that drifts.
    /// </summary>
    [Fact]
    public void Append_LeavesTheFileEndingInANewline_SoTheNextAppendCannotRunOnFromIt()
    {
        File.WriteAllText(_channelFile, "seed\n\n---");

        ChannelAppender.Append_AppEntry(_channelFile, "first", "body one", DateTime.Now);
        ChannelAppender.Append_AppEntry(_channelFile, "second", "body two", DateTime.Now);

        var text = File.ReadAllText(_channelFile);

        Assert.EndsWith("\n", text);
        Assert.Equal(2, ChannelEntry_Parser.Parse_All(text).Count);
    }

    [Fact]
    public void Append_AlternatingAuthors_NumberingStaysMonotonic()
    {
        ChannelAppender.Append_OwnerEntry(_channelFile, "one", DateTime.Now);
        ChannelAppender.Append_AppEntry(_channelFile, "subject", "two", DateTime.Now);
        ChannelAppender.Append_OwnerEntry(_channelFile, "three", DateTime.Now);

        var entries = ChannelEntry_Parser.Parse_All(File.ReadAllText(_channelFile));

        Assert.Equal([1, 2, 3], entries.Select(e => e.Index));
    }
}
