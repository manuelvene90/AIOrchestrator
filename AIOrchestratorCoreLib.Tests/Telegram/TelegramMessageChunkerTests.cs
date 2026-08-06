using AIOrchestratorCoreLib.Telegram;
using Xunit;

namespace AIOrchestratorCoreLib.Tests.Telegram;

public class TelegramMessageChunkerTests
{
    [Fact]
    public void Chunk_ShortText_SingleChunkUnchanged()
    {
        var chunks = TelegramMessage_Chunker.Chunk("hello world");

        Assert.Single(chunks);
        Assert.Equal("hello world", chunks[0]);
    }

    [Fact]
    public void Chunk_LongText_SplitsOnLineBoundaries()
    {
        var line = new string('a', 100);
        var text = string.Join('\n', Enumerable.Repeat(line, 10));

        var chunks = TelegramMessage_Chunker.Chunk(text, 250);

        Assert.True(chunks.Count > 1);

        foreach (var chunk in chunks)
        {
            Assert.True(chunk.Length <= 250, $"chunk length {chunk.Length} exceeds 250");
            Assert.DoesNotContain(chunk.Split('\n'), l => l.Length != 100);
        }
    }

    [Fact]
    public void Chunk_SingleOverlongLine_HardSplits()
    {
        var text = new string('x', 9000);

        var chunks = TelegramMessage_Chunker.Chunk(text);

        Assert.Equal(3, chunks.Count);
        Assert.Equal(9000, chunks.Sum(c => c.Length));
    }

    [Fact]
    public void Chunk_ReassembledChunks_LoseNoContentLines()
    {
        var lines = Enumerable.Range(1, 200).Select(i => $"line {i}").ToList();
        var text = string.Join('\n', lines);

        var chunks = TelegramMessage_Chunker.Chunk(text, 300);
        var reassembledLines = chunks.SelectMany(c => c.Split('\n')).ToList();

        Assert.Equal(lines, reassembledLines);
    }
}
