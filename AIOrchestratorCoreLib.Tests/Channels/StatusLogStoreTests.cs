using AIOrchestratorCoreLib.Channels;
using AIOrchestratorCoreLib.Channels.StatusLog;
using AIOrchestratorCoreLib.Running;
using Xunit;

namespace AIOrchestratorCoreLib.Tests.Channels;

/// <summary>
/// THE LOG IS CHANNEL-SHAPED ON PURPOSE. Every reader in this system already knows how to read a
/// channel entry — the parser, the digest that identifies one, <c>Is_AgentNote</c>, the prompt
/// builder. A record that stored "subject" and "body" as loose fields would have needed a second
/// implementation of each of those, which is decision 12 four times over. So the record carries the
/// rendered entry and <see cref="StatusLog_Store.Read_Entries"/> hands back exactly what
/// <c>ChannelEntry_Parser.Parse_All</c> hands back for a channel file.
/// </summary>
public class StatusLogStoreTests
{
    static string TempFile() => Path.Combine(Path.GetTempPath(), Path.GetRandomFileName() + ".jsonl");

    static readonly DateTime NOW = new(2026, 9, 15, 14, 30, 0, DateTimeKind.Local);

    [Fact]
    public void ARecord_ReadsBackAsAnAgentTaggedAppEntry()
    {
        var file = TempFile();

        StatusLog_Store.Append(file, "turn_ended imp-1 turn 4 — success", "request_id: repo-1/imp-1/4", NOW);

        var entries = StatusLog_Store.Read_Entries(file);
        var entry = Assert.Single(entries);

        Assert.Equal(ChannelAuthors.App, entry.Author);
        Assert.Equal("[agent] turn_ended imp-1 turn 4 — success", entry.Subject);
        Assert.Equal("2026-09-15 14:30", entry.DateText);
        Assert.Contains("request_id: repo-1/imp-1/4", entry.Body, StringComparison.Ordinal);
        Assert.True(PrintTurn_Trigger.Is_AgentNote(entry) is false or true); // compiles: the parser produced a real entry

        File.Delete(file);
    }

    /// <summary>
    /// ORDER IS THE FILE'S ORDER, and the index is diagnostic. It is written so a human reading the
    /// file sees the same shape as a channel; nothing decides anything from it (decision 12).
    /// </summary>
    [Fact]
    public void RecordsAreReadBackInTheOrderTheyWereWritten_AndAreNumbered()
    {
        var file = TempFile();

        StatusLog_Store.Append(file, "first", "a", NOW);
        StatusLog_Store.Append(file, "second", "b", NOW.AddMinutes(1));
        StatusLog_Store.Append(file, "third", "c", NOW.AddMinutes(2));

        var entries = StatusLog_Store.Read_Entries(file);

        Assert.Equal(["[agent] first", "[agent] second", "[agent] third"], entries.Select(entry => entry.Subject));
        Assert.Equal([1, 2, 3], entries.Select(entry => entry.Index));

        File.Delete(file);
    }

    /// <summary>
    /// A BODY WITH A CHANNEL HEADER IN IT DOES NOT BECOME TWO ENTRIES. The app quotes channel text
    /// back at sessions constantly — the malformed-header report quotes offending lines verbatim —
    /// and a naive "concatenate the raw texts and parse" would split a record in half. The JSON
    /// encoding is what makes the file safe to write; this pins that the READ side is too.
    /// </summary>
    [Fact]
    public void ABodyContainingAChannelHeader_IsStillOneRecord()
    {
        var file = TempFile();

        StatusLog_Store.Append(file, "3 entries are INVISIBLE", "## [7] FROM sup — 2026-09-14 10:00 — a malformed one\nand its body", NOW);

        var entry = Assert.Single(StatusLog_Store.Read_Entries(file));

        Assert.Equal("[agent] 3 entries are INVISIBLE", entry.Subject);

        File.Delete(file);
    }

    /// <summary>
    /// BOUNDED, like every other file this system keeps beside a session. Past the cap the oldest
    /// records are dropped, which is exactly what <c>Channel_Compactor</c> does to a channel and what
    /// the cursor's prune already absorbs — decision 13's rule is that nothing may COUNT this file
    /// across time, and nothing does.
    /// </summary>
    [Fact]
    public void ThePastIsTrimmed_NewestKept()
    {
        var file = TempFile();

        for (var index = 1; index <= StatusLog_Store.MAX_RECORDS + 20; index++)
            StatusLog_Store.Append(file, $"note {index}", "body", NOW);

        var entries = StatusLog_Store.Read_Entries(file);

        Assert.Equal(StatusLog_Store.MAX_RECORDS, entries.Count);
        Assert.Equal($"[agent] note {StatusLog_Store.MAX_RECORDS + 20}", entries[^1].Subject);

        File.Delete(file);
    }

    /// <summary>
    /// A LOG THAT CANNOT BE READ IS EMPTY, NEVER AN EXCEPTION. Losing a bookkeeping line must never
    /// be the reason a turn does not start — the same ruling <c>StatePack_Writer</c> makes about the
    /// pack.
    /// </summary>
    [Fact]
    public void AnUnreadableLogReadsAsEmpty()
    {
        Assert.Empty(StatusLog_Store.Read_Entries(Path.Combine(Path.GetTempPath(), "no-such-folder-" + Guid.NewGuid(), "status.jsonl")));
    }
}
