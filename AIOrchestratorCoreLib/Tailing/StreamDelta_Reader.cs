using System.Text;

namespace AIOrchestratorCoreLib.Tailing;

/// <summary>
/// Reads a byte range and reports HOW MANY BYTES IT ACTUALLY GOT.
///
/// <c>Stream.Read</c> is contractually allowed to return fewer bytes than asked for. The tailer used
/// to make one call, keep whatever came back, and advance its cursor to the file LENGTH regardless —
/// so a short read moved the cursor past bytes nobody had seen and they were never read again. In
/// the one component whose contract is at-least-once delivery to the owner's phone, that is silent
/// loss, and it leaves no trace anywhere: the channel file on disk stays perfectly intact.
///
/// Separated from the tailer so the short read can be TESTED — forcing a real FileStream to return a
/// partial read is not something a test can arrange, while a stream that does it on purpose is three
/// lines. The count is bytes rather than characters on purpose: an offset advanced by string length
/// desynchronises on the first multi-byte character, and these channels are full of them.
/// </summary>
public static class StreamDelta_Reader
{
    public static (string Text, long ByteCount) Read_Delta(Stream stream, long byteCount)
    {
        if (byteCount <= 0)
            return ("", 0L);

        var buffer = new byte[byteCount];
        var read = 0;

        while (read < buffer.Length)
        {
            var readNow = stream.Read(buffer, read, buffer.Length - read);

            // End of stream: the file shrank between the length check and this read — compaction is
            // a real writer here. What was read is valid, and the caller advances only that far.
            if (readNow == 0)
                break;

            read += readNow;
        }

        return (Encoding.UTF8.GetString(buffer, 0, read), read);
    }
}
