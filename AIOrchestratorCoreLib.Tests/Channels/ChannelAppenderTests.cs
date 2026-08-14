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
        ChannelAppender.Append_AppEntry(_channelFile, AppEntryAudiences.Owner, "orchestration 'x' started", "details here", DateTime.Now);

        var entries = ChannelEntry_Parser.Parse_All(File.ReadAllText(_channelFile));

        Assert.Single(entries);
        Assert.Equal(ChannelAuthors.App, entries[0].Author);
        Assert.Equal("orchestration 'x' started", entries[0].Subject);
    }

    /// <summary>
    /// The audience has to survive the round trip through the FILE, because the mirror never sees the
    /// call that wrote the entry — it re-reads entries on a later poll. So the tag being present in the
    /// parsed subject is the whole mechanism, not a formatting detail.
    /// </summary>
    [Fact]
    public void Append_AppEntry_AgentAudience_TagsTheSubjectInTheFile()
    {
        ChannelAppender.Append_AppEntry(_channelFile, AppEntryAudiences.Agent, "unread reports waiting on you", "body", DateTime.Now);

        var entries = ChannelEntry_Parser.Parse_All(File.ReadAllText(_channelFile));

        Assert.Single(entries);
        Assert.Equal($"{AppEntryAudience_Tag.AGENT_TAG} unread reports waiting on you", entries[0].Subject);
        Assert.True(AppEntryAudience_Tag.Is_AgentTagged(entries[0].Subject));
    }

    /// <summary>
    /// And an owner-facing subject is written untouched, so everything downstream that reads a subject
    /// — the formatter's "App: {subject}" line, the STATUS prefix test — sees exactly what it always saw.
    /// </summary>
    [Fact]
    public void Append_AppEntry_OwnerAudience_LeavesTheSubjectAlone()
    {
        ChannelAppender.Append_AppEntry(_channelFile, AppEntryAudiences.Owner, "orchestration 'x' closed", "body", DateTime.Now);

        var entries = ChannelEntry_Parser.Parse_All(File.ReadAllText(_channelFile));

        Assert.Equal("orchestration 'x' closed", Assert.Single(entries).Subject);
        Assert.False(AppEntryAudience_Tag.Is_AgentTagged(entries[0].Subject));
    }

    [Fact]
    public void Append_AlternatingAuthors_NumberingStaysMonotonic()
    {
        ChannelAppender.Append_OwnerEntry(_channelFile, "one", DateTime.Now);
        ChannelAppender.Append_AppEntry(_channelFile, AppEntryAudiences.Owner, "subject", "two", DateTime.Now);
        ChannelAppender.Append_OwnerEntry(_channelFile, "three", DateTime.Now);

        var entries = ChannelEntry_Parser.Parse_All(File.ReadAllText(_channelFile));

        Assert.Equal([1, 2, 3], entries.Select(e => e.Index));
    }
}
