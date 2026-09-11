using AIOrchestratorCoreLib.Bridge;
using AIOrchestratorCoreLib.Telegram;
using Xunit;

namespace AIOrchestratorCoreLib.Tests.Telegram;

/// <summary>
/// WHAT TELEGRAM WILL ACCEPT AS A FILE, CHECKED BEFORE THE UPLOAD — brief F7.
///
/// The case that was missing entirely is the DIMENSION one: a photo can be a tenth of the size cap
/// and still be refused, because width + height may not exceed 10000 and the ratio may not exceed
/// 20. Telegram answers PHOTO_INVALID_DIMENSIONS, the send path logged a warning, and nobody was
/// told — the same silent drop that produced this policy on 2026-09-08.
/// </summary>
public class TelegramFileCapsTests
{
    [Theory]
    [InlineData(4000, 4000, true)]
    [InlineData(5000, 5000, true)]
    [InlineData(5000, 5001, false)]
    [InlineData(9000, 3000, false)]
    public void TheDimensionSum_IsCappedAtTenThousand(int width, int height, bool acceptable)
    {
        Assert.Equal(acceptable, TelegramFileCaps.Is_DimensionSumAcceptable(width, height));
    }

    [Theory]
    [InlineData(100, 2000, true)]
    [InlineData(100, 2001, false)]
    [InlineData(2000, 100, true)]
    [InlineData(2001, 100, false)]
    [InlineData(500, 500, true)]
    public void TheRatio_IsCappedAtTwenty_InBothOrientations(int width, int height, bool acceptable)
    {
        Assert.Equal(acceptable, TelegramFileCaps.Is_RatioAcceptable(width, height));
    }

    /// <summary>A zero side is not a photo, and asking its ratio is a division by zero — a verdict, not an exception.</summary>
    [Fact]
    public void AZeroSide_IsRefused_NotDividedBy()
    {
        Assert.False(TelegramFileCaps.Is_RatioAcceptable(0, 100));
        Assert.False(TelegramFileCaps.Is_RatioAcceptable(100, 0));
        Assert.False(TelegramFileCaps.Is_RatioAcceptable(-5, 100));
    }

    /// <summary>The caps have ONE home now; these names are the old ones, kept for their callers.</summary>
    [Fact]
    public void ThePolicyAndTheClient_ReadTheSameNumbers()
    {
        Assert.Equal(TelegramFileCaps.MAX_DOCUMENT_BYTES, EntryAttachment_Policy.MAX_BYTES);
        Assert.Equal(TelegramFileCaps.MAX_PHOTO_BYTES, EntryAttachment_Policy.MAX_PICTURE_BYTES);
        Assert.True(TelegramFileCaps.MAX_DOWNLOAD_BYTES < TelegramFileCaps.MAX_DOCUMENT_BYTES);
    }

    // ---- the verdicts ----

    [Fact]
    public void ATallScreenshot_IsRefusedAsAPhoto_AndTheAgentIsToldToUseAttach()
    {
        var verdict = Decide_Picture(width: 1200, height: 9200);

        Assert.Equal(AttachmentVerdicts.PictureTooBig, verdict);

        var reason = EntryAttachment_Policy.Describe(verdict, "/repo/shot.png", ["/repo"], asPicture: true, pictureDimensions: (1200, 9200));

        Assert.Contains("1200×9200", reason);
        Assert.Contains("ATTACH:", reason);
    }

    [Fact]
    public void AVeryWideBanner_IsRefusedOnItsRatio()
    {
        var verdict = Decide_Picture(width: 4000, height: 100);

        Assert.Equal(AttachmentVerdicts.PictureTooOblong, verdict);
        Assert.Contains("ATTACH:", EntryAttachment_Policy.Describe(verdict, "/repo/banner.png", ["/repo"], asPicture: true, pictureDimensions: (4000, 100)));
    }

    /// <summary>
    /// UNMEASURABLE MEANS ALLOW. A pre-flight check whose job is to turn an opaque 400 into a
    /// sentence must never become a second gate that drops files Telegram would have accepted.
    /// </summary>
    [Fact]
    public void APictureWhoseSizeCannotBeRead_IsStillSent()
    {
        Assert.Equal(AttachmentVerdicts.Send, Decide_Picture(dimensions: null));
    }

    /// <summary>A DOCUMENT has no dimension rule — Telegram does not look inside it.</summary>
    [Fact]
    public void ADocumentIsNeverJudgedOnItsPixels()
    {
        var verdict = EntryAttachment_Policy.Decide(
            "/repo/huge.png", ["/repo"], exists: true, lengthBytes: 1024, asPicture: false, pictureDimensions: (1200, 9200));

        Assert.Equal(AttachmentVerdicts.Send, verdict);
    }

    /// <summary>Size is judged before pixels: "shrink the file" is the wrong advice for a 40 MB photo.</summary>
    [Fact]
    public void SizeIsJudgedBeforePixels()
    {
        var verdict = EntryAttachment_Policy.Decide(
            "/repo/huge.png", ["/repo"], exists: true, lengthBytes: 40L * 1024 * 1024, asPicture: true, pictureDimensions: (1200, 9200));

        Assert.Equal(AttachmentVerdicts.TooLarge, verdict);
    }

    static AttachmentVerdicts Decide_Picture(int width = 800, int height = 600)
    {
        return Decide_Picture((width, height));
    }

    static AttachmentVerdicts Decide_Picture((int Width, int Height)? dimensions)
    {
        return EntryAttachment_Policy.Decide(
            "/repo/shot.png", ["/repo"], exists: true, lengthBytes: 1024, asPicture: true, pictureDimensions: dimensions);
    }
}

/// <summary>
/// READING A PICTURE'S SIZE FROM ITS HEADER, without an image library — brief F7.
///
/// Every fixture below is a real, minimal file of its format, built byte by byte, so a change that
/// mis-reads an offset fails here rather than as a refused upload on the owner's phone.
/// </summary>
public class ImageDimensionsReaderTests
{
    [Fact]
    public void APng_IsMeasuredFromItsIhdr()
    {
        Assert.Equal((1200, 9200), ImageDimensions_Reader.Read_OrNull(Build_Png(1200, 9200)));
    }

    [Fact]
    public void AGif_IsMeasuredFromItsLogicalScreen()
    {
        Assert.Equal((640, 480), ImageDimensions_Reader.Read_OrNull(Build_Gif(640, 480)));
    }

    /// <summary>A bottom-up BMP stores a NEGATIVE height; the picture is not negative pixels tall.</summary>
    [Theory]
    [InlineData(320, 240)]
    [InlineData(320, -240)]
    public void ABmp_IsMeasuredWithItsSignHandled(int width, int storedHeight)
    {
        Assert.Equal((Math.Abs(width), 240), ImageDimensions_Reader.Read_OrNull(Build_Bmp(width, storedHeight)));
    }

    [Fact]
    public void AJpeg_IsMeasuredFromItsStartOfFrame_PastTheMetadata()
    {
        Assert.Equal((1024, 768), ImageDimensions_Reader.Read_OrNull(Build_Jpeg(1024, 768, precedingMetadataBytes: 900)));
    }

    /// <summary>
    /// 0xFFC4 is a Huffman table, not a frame header — reading one as a frame is the classic way a
    /// hand-rolled JPEG reader goes wrong, and it yields confident nonsense rather than null.
    /// </summary>
    [Fact]
    public void AJpegWithAHuffmanTableFirst_IsNotMeasuredFromIt()
    {
        Assert.Equal((640, 400), ImageDimensions_Reader.Read_OrNull(Build_JpegWithHuffmanTableFirst(640, 400)));
    }

    [Fact]
    public void ALossyWebP_IsMeasured()
    {
        Assert.Equal((300, 200), ImageDimensions_Reader.Read_OrNull(Build_LossyWebP(300, 200)));
    }

    [Theory]
    [InlineData(new byte[0])]
    [InlineData(new byte[] { 1, 2, 3 })]
    [InlineData(new byte[] { 0x89, 0x50, 0x4E, 0x47 })]
    public void SomethingThatIsNotAPictureAtAll_IsNull_NotAnException(byte[] bytes)
    {
        Assert.Null(ImageDimensions_Reader.Read_OrNull(bytes));
    }

    /// <summary>A truncated header must be null, never an index out of range on a file an agent named.</summary>
    [Fact]
    public void ATruncatedHeader_IsNull()
    {
        var png = Build_Png(100, 100);

        for (var length = 0; length < png.Length; length++)
            Assert.Null(ImageDimensions_Reader.Read_OrNull(png.AsSpan(0, length)));
    }

    /// <summary>A zero-sized image is unmeasurable, not a refusal — Telegram judges it.</summary>
    [Fact]
    public void AZeroSizedImage_ReadsAsUnmeasurable()
    {
        Assert.Null(ImageDimensions_Reader.Read_OrNull(Build_Png(0, 100)));
    }

    static byte[] Build_Png(int width, int height)
    {
        var bytes = new byte[24];
        byte[] signature = [0x89, (byte)'P', (byte)'N', (byte)'G', 0x0D, 0x0A, 0x1A, 0x0A];
        signature.CopyTo(bytes, 0);

        // Length + "IHDR" occupy 8..15; the two dimensions are big-endian at 16 and 20.
        System.Buffers.Binary.BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(16, 4), (uint)width);
        System.Buffers.Binary.BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(20, 4), (uint)height);

        return bytes;
    }

    static byte[] Build_Gif(int width, int height)
    {
        var bytes = new byte[13];
        "GIF89a"u8.CopyTo(bytes);
        System.Buffers.Binary.BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(6, 2), (ushort)width);
        System.Buffers.Binary.BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(8, 2), (ushort)height);

        return bytes;
    }

    static byte[] Build_Bmp(int width, int storedHeight)
    {
        var bytes = new byte[30];
        bytes[0] = (byte)'B';
        bytes[1] = (byte)'M';
        System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(18, 4), width);
        System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(22, 4), storedHeight);

        return bytes;
    }

    static byte[] Build_Jpeg(int width, int height, int precedingMetadataBytes)
    {
        List<byte> bytes = [0xFF, 0xD8];

        // An APP1 segment standing in for EXIF, so the frame header is not at a fixed offset.
        bytes.AddRange([0xFF, 0xE1]);
        bytes.AddRange([(byte)((precedingMetadataBytes + 2) >> 8), (byte)((precedingMetadataBytes + 2) & 0xFF)]);
        bytes.AddRange(new byte[precedingMetadataBytes]);

        bytes.AddRange(Build_StartOfFrame(width, height));
        bytes.AddRange(new byte[16]);

        return [.. bytes];
    }

    static byte[] Build_JpegWithHuffmanTableFirst(int width, int height)
    {
        List<byte> bytes = [0xFF, 0xD8];

        // 0xFFC4 — a DHT, whose bytes at the frame header's offsets are deliberately wrong sizes.
        bytes.AddRange([0xFF, 0xC4, 0x00, 0x0C]);
        bytes.AddRange([0x11, 0x22, 0x33, 0x44, 0x55, 0x66, 0x77, 0x88, 0x99, 0xAA]);

        bytes.AddRange(Build_StartOfFrame(width, height));
        bytes.AddRange(new byte[16]);

        return [.. bytes];
    }

    /// <summary>An SOF0: marker, length, precision, then height and width — height FIRST.</summary>
    static byte[] Build_StartOfFrame(int width, int height)
    {
        return
        [
            0xFF, 0xC0,
            0x00, 0x11,
            0x08,
            (byte)(height >> 8), (byte)(height & 0xFF),
            (byte)(width >> 8), (byte)(width & 0xFF),
            0x03, 0x01, 0x22, 0x00, 0x02, 0x11, 0x01, 0x03, 0x11, 0x01,
        ];
    }

    static byte[] Build_LossyWebP(int width, int height)
    {
        var bytes = new byte[32];
        "RIFF"u8.CopyTo(bytes.AsSpan(0));
        "WEBP"u8.CopyTo(bytes.AsSpan(8));
        "VP8 "u8.CopyTo(bytes.AsSpan(12));
        System.Buffers.Binary.BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(26, 2), (ushort)width);
        System.Buffers.Binary.BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(28, 2), (ushort)height);

        return bytes;
    }
}
