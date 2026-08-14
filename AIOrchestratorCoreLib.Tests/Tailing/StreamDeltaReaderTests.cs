using System.Text;
using AIOrchestratorCoreLib.Tailing;
using Xunit;

namespace AIOrchestratorCoreLib.Tests.Tailing;

/// <summary>
/// <c>Stream.Read</c> is CONTRACTUALLY allowed to return fewer bytes than asked for. The tailer used
/// to make one call, take whatever came back, and then advance its cursor to the file LENGTH — so a
/// short read skipped whole entries, permanently, in the one component whose contract is
/// at-least-once delivery to the owner's phone. The cursor may only ever move by what was actually
/// read, which means the reader has to report that number.
/// </summary>
public class StreamDeltaReaderTests
{
    /// <summary>A stream that hands back at most <c>chunkSize</c> bytes per Read — legal, and rare.</summary>
    sealed class ShortReadStream(byte[] content, int chunkSize) : Stream
    {
        int _position;

        public int ReadCallCount { get; private set; }

        public override int Read(byte[] buffer, int offset, int count)
        {
            ReadCallCount++;

            var remaining = content.Length - _position;

            if (remaining <= 0)
                return 0;

            var take = Math.Min(Math.Min(count, chunkSize), remaining);
            Array.Copy(content, _position, buffer, offset, take);
            _position += take;

            return take;
        }

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => content.Length;
        public override long Position { get => _position; set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    [Fact]
    public void Read_Delta_AStreamThatReturnsOneByteAtATime_StillReadsEverything()
    {
        var content = Encoding.UTF8.GetBytes("## [1] FROM solo — hello\n");
        var stream = new ShortReadStream(content, chunkSize: 1);

        var (text, byteCount) = StreamDelta_Reader.Read_Delta(stream, content.Length);

        Assert.Equal("## [1] FROM solo — hello\n", text);
        Assert.Equal(content.Length, byteCount);
        Assert.True(stream.ReadCallCount > 1, "the reader made a single Read call and trusted it");
    }

    /// <summary>
    /// The file shrank between the length check and the read (compaction is a real writer here). What
    /// was read is valid and the reported count is what the caller may advance by — never the length
    /// it asked for, which is the byte range that used to be skipped.
    /// </summary>
    [Fact]
    public void Read_Delta_AStreamThatEndsEarly_ReportsOnlyWhatItGot()
    {
        var content = Encoding.UTF8.GetBytes("half");

        var (text, byteCount) = StreamDelta_Reader.Read_Delta(new ShortReadStream(content, chunkSize: 4), byteCount: 64);

        Assert.Equal("half", text);
        Assert.Equal(4, byteCount);
    }

    [Fact]
    public void Read_Delta_NothingToRead_ReportsZero()
    {
        var (text, byteCount) = StreamDelta_Reader.Read_Delta(new ShortReadStream([], chunkSize: 8), byteCount: 0);

        Assert.Equal("", text);
        Assert.Equal(0, byteCount);
    }

    /// <summary>
    /// The count is BYTES, not characters, and this is the reason it is returned rather than taken
    /// from the string: an offset advanced by string length would desynchronise on the first
    /// non-ASCII character, and this system's channels are full of them (— · 🟠).
    /// </summary>
    [Fact]
    public void Read_Delta_MultiByteCharacters_CountsBytesNotCharacters()
    {
        var content = Encoding.UTF8.GetBytes("— · 🟠");

        var (text, byteCount) = StreamDelta_Reader.Read_Delta(new ShortReadStream(content, chunkSize: 3), content.Length);

        Assert.Equal("— · 🟠", text);
        Assert.Equal(content.Length, byteCount);
        Assert.True(byteCount > text.Length, "the fixture no longer contains multi-byte characters");
    }
}
