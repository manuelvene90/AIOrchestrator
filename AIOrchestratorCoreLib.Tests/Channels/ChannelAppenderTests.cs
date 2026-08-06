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
