using System.Buffers.Binary;

namespace AIOrchestratorCoreLib.Telegram;

/// <summary>
/// HOW BIG A PICTURE IS, FROM ITS FIRST FEW HUNDRED BYTES — so the dimension rule in
/// <see cref="TelegramFileCaps"/> can be applied BEFORE the upload (brief F7).
///
/// <para>
/// WHY BY HAND. This repo has no image library and adding one to read two integers out of a header
/// would be the largest dependency in the solution, taken for the smallest reason. Every format
/// below states its size in a fixed place near the start of the file, which is why every tool that
/// needs only the size does exactly this.
/// </para>
/// <para>
/// UNREADABLE MEANS ALLOW, and that is the whole error policy. This is a pre-flight courtesy: its
/// job is to turn a confident refusal into a sentence the agent can act on, not to decide what is a
/// valid image. A format it cannot parse — a WebP in lossless VP8L form, a truncated header, an
/// exotic BMP — returns null, the caller skips the dimension check, and Telegram remains the
/// authority it always was. The alternative, refusing what we cannot measure, would drop files that
/// would have gone through perfectly well.
/// </para>
/// <para>
/// NOTHING HERE MAY THROW ON A HOSTILE FILE. The bytes come off disk from a path an agent named, so
/// every read is bounds-checked and every unknown shape is null.
/// </para>
/// </summary>
public static class ImageDimensions_Reader
{
    /// <summary>How much of a file is enough. A JPEG's size marker can sit some way in, past EXIF.</summary>
    public const int HEADER_BYTES_NEEDED = 64 * 1024;

    /// <summary>The pixel size, or null when this is not a format we can measure.</summary>
    public static (int Width, int Height)? Read_OrNull(ReadOnlySpan<byte> header)
    {
        return Read_Png_OrNull(header)
            ?? Read_Gif_OrNull(header)
            ?? Read_Bmp_OrNull(header)
            ?? Read_WebP_OrNull(header)
            ?? Read_Jpeg_OrNull(header);
    }

    /// <summary>Reads at most <see cref="HEADER_BYTES_NEEDED"/> from a file; null for anything unreadable.</summary>
    public static (int Width, int Height)? Read_FromFile_OrNull(string path)
    {
        try
        {
            using var stream = File.OpenRead(path);

            var buffer = new byte[HEADER_BYTES_NEEDED];
            var read = stream.ReadAtLeast(buffer, HEADER_BYTES_NEEDED, throwOnEndOfStream: false);

            return Read_OrNull(buffer.AsSpan(0, read));
        }
        catch
        {
            // Broad by intent: a file that cannot be opened or read is one this cannot measure, and
            // "cannot measure" is already a case with an answer.
            return null;
        }
    }

    /// <summary>PNG: the signature, then IHDR, whose two big-endian widths sit at fixed offsets.</summary>
    static (int Width, int Height)? Read_Png_OrNull(ReadOnlySpan<byte> header)
    {
        ReadOnlySpan<byte> signature = [0x89, (byte)'P', (byte)'N', (byte)'G', 0x0D, 0x0A, 0x1A, 0x0A];

        if (header.Length < 24 || !header[..8].SequenceEqual(signature))
            return null;

        return Sane(
            (int)BinaryPrimitives.ReadUInt32BigEndian(header.Slice(16, 4)),
            (int)BinaryPrimitives.ReadUInt32BigEndian(header.Slice(20, 4)));
    }

    /// <summary>GIF: "GIF87a" or "GIF89a", then the logical screen size, little-endian.</summary>
    static (int Width, int Height)? Read_Gif_OrNull(ReadOnlySpan<byte> header)
    {
        if (header.Length < 10 || header[0] != 'G' || header[1] != 'I' || header[2] != 'F')
            return null;

        return Sane(
            BinaryPrimitives.ReadUInt16LittleEndian(header.Slice(6, 2)),
            BinaryPrimitives.ReadUInt16LittleEndian(header.Slice(8, 2)));
    }

    /// <summary>BMP: "BM", then the DIB header's signed dimensions — a negative height means top-down.</summary>
    static (int Width, int Height)? Read_Bmp_OrNull(ReadOnlySpan<byte> header)
    {
        if (header.Length < 26 || header[0] != 'B' || header[1] != 'M')
            return null;

        var width = BinaryPrimitives.ReadInt32LittleEndian(header.Slice(18, 4));
        var height = BinaryPrimitives.ReadInt32LittleEndian(header.Slice(22, 4));

        return Sane(Math.Abs((long)width), Math.Abs((long)height));
    }

    /// <summary>
    /// WebP: a RIFF container whose first chunk says which of three encodings it is. VP8 (lossy)
    /// and VP8X (extended) state their size plainly; VP8L (lossless) packs it into a bit stream and
    /// is deliberately not decoded here — it returns null, which means "allow".
    /// </summary>
    static (int Width, int Height)? Read_WebP_OrNull(ReadOnlySpan<byte> header)
    {
        if (header.Length < 30
            || header[0] != 'R' || header[1] != 'I' || header[2] != 'F' || header[3] != 'F'
            || header[8] != 'W' || header[9] != 'E' || header[10] != 'B' || header[11] != 'P')
            return null;

        var chunk = header.Slice(12, 4);

        if (chunk.SequenceEqual("VP8 "u8))
        {
            // 14 bytes of chunk header and frame tag, then the 3-byte start code, then the sizes —
            // each a 16-bit little-endian value whose top two bits are a scale factor.
            if (header.Length < 30)
                return null;

            return Sane(
                BinaryPrimitives.ReadUInt16LittleEndian(header.Slice(26, 2)) & 0x3FFF,
                BinaryPrimitives.ReadUInt16LittleEndian(header.Slice(28, 2)) & 0x3FFF);
        }

        if (chunk.SequenceEqual("VP8X"u8))
        {
            if (header.Length < 30)
                return null;

            // Canvas size, stored as 24-bit little-endian values one LESS than the real size.
            var width = header[24] | (header[25] << 8) | (header[26] << 16);
            var height = header[27] | (header[28] << 8) | (header[29] << 16);

            return Sane(width + 1L, height + 1L);
        }

        return null;
    }

    /// <summary>
    /// JPEG: walk the marker segments to the start-of-frame, which carries the size. The three
    /// 0xFFCn markers that are NOT frame headers (DHT, DNL, DAC) are skipped by name — reading one
    /// as a frame is the classic way this is got wrong.
    /// </summary>
    static (int Width, int Height)? Read_Jpeg_OrNull(ReadOnlySpan<byte> header)
    {
        if (header.Length < 4 || header[0] != 0xFF || header[1] != 0xD8)
            return null;

        var position = 2;

        while (position + 9 < header.Length)
        {
            if (header[position] != 0xFF)
            {
                // Fill bytes are legal between segments; anything else means the stream is not
                // where we think it is, and guessing further would be inventing data.
                position++;
                continue;
            }

            var marker = header[position + 1];

            // Standalone markers: no length field to skip past.
            if (marker is 0xD8 or 0x01 || (marker >= 0xD0 && marker <= 0xD7))
            {
                position += 2;
                continue;
            }

            var segmentLength = BinaryPrimitives.ReadUInt16BigEndian(header.Slice(position + 2, 2));

            if (segmentLength < 2)
                return null;

            var isStartOfFrame = marker >= 0xC0 && marker <= 0xCF && marker is not (0xC4 or 0xC8 or 0xCC);

            if (isStartOfFrame)
            {
                if (position + 9 >= header.Length)
                    return null;

                return Sane(
                    BinaryPrimitives.ReadUInt16BigEndian(header.Slice(position + 7, 2)),
                    BinaryPrimitives.ReadUInt16BigEndian(header.Slice(position + 5, 2)));
            }

            position += 2 + segmentLength;
        }

        return null;
    }

    /// <summary>
    /// A size only counts if it is one. Zero and absurd values are read as "could not measure"
    /// rather than as a refusal — an unmeasurable file is Telegram's to judge, not ours.
    /// </summary>
    static (int Width, int Height)? Sane(long width, long height)
    {
        if (width <= 0 || height <= 0 || width > int.MaxValue || height > int.MaxValue)
            return null;

        return ((int)width, (int)height);
    }
}
