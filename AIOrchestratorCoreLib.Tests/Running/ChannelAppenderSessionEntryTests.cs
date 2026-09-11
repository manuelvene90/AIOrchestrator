using AIOrchestratorCoreLib.Channels;
using Xunit;

namespace AIOrchestratorCoreLib.Tests.Running;

public class ChannelAppenderSessionEntryTests : IDisposable
{
    readonly string _tempRoot;
    readonly string _channel;

    public ChannelAppenderSessionEntryTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), $"aiorch-session-entry-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempRoot);
        _channel = Path.Combine(_tempRoot, "channel.md");
        File.WriteAllText(_channel, "# seed\n\n---\n## [4] FROM supervisor — 2026-09-05 10:00 — brief\n\ndo it\n");
    }

    public void Dispose()
    {
        Directory.Delete(_tempRoot, recursive: true);
    }

    [Fact]
    public void ASessionEntry_ContinuesTheNumbering_UnderTheSessionsAuthorWord()
    {
        var now = new DateTime(2026, 9, 5, 11, 22, 0);

        Assert.True(ChannelAppender.Append_SessionEntry(_channel, ChannelAuthors.Implementer, "REPORT — done", "all green", now));

        var entries = ChannelEntry_Parser.Parse_All(File.ReadAllText(_channel));
        var appended = entries[^1];
        Assert.Equal(5, appended.Index);
        Assert.Equal(ChannelAuthors.Implementer, appended.Author);
        Assert.Equal("2026-09-05 11:22", appended.DateText);
        Assert.Equal("REPORT — done", appended.Subject);
        Assert.Equal("all green", appended.Body);
    }

    [Fact]
    public void OwnerAndApp_AreNotSessionAuthors()
    {
        Assert.Throws<ArgumentException>(() => ChannelAppender.Append_SessionEntry(_channel, ChannelAuthors.Owner, "s", "b", DateTime.Now));
        Assert.Throws<ArgumentException>(() => ChannelAppender.Append_SessionEntry(_channel, ChannelAuthors.App, "s", "b", DateTime.Now));
        Assert.Throws<ArgumentException>(() => ChannelAppender.Append_SessionEntry(_channel, ChannelAuthors.Unknown, "s", "b", DateTime.Now));
    }
}
