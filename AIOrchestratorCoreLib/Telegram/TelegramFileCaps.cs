namespace AIOrchestratorCoreLib.Telegram;

/// <summary>
/// WHAT TELEGRAM WILL ACCEPT AS A FILE — the numbers, in ONE place (brief F7).
///
/// <para>
/// They were in two-and-a-bit places: <c>EntryAttachment_Policy</c> held the two upload caps and
/// applied them to the <c>IMAGE:</c>/<c>ATTACH:</c> path only, the HTTP client held none at all —
/// so every other caller of <c>sendPhoto</c>/<c>sendDocument</c> (the screenshots, the undelivered
/// digest) reached Telegram unchecked — and the DOWNLOAD had no cap anywhere, which is a stranger's
/// file coming down a pipe with no ceiling.
/// </para>
/// <para>
/// THE DIMENSION RULE WAS MISSING ENTIRELY, and it is the one that actually bites. A photo can be
/// well under 10 MB and still be refused: Telegram caps the SUM of width and height at 10000 and
/// the RATIO at 20, and a tall screenshot or a wide banner mockup hits both while weighing almost
/// nothing. Telegram answers <c>PHOTO_INVALID_DIMENSIONS</c>, which arrived as a swallowed warning
/// — the same silent-drop shape as the 2026-09-08 incident that produced the containment policy in
/// the first place: the agent tells the owner it sent four mockups and none of them exist.
/// </para>
/// <para>
/// This file holds NUMBERS AND PREDICATES ONLY. Who is told about a refusal, and in what words, is
/// <see cref="Bridge.EntryAttachment_Policy"/>'s — it lives in Bridge because it knows about agents
/// and channels, and this lives in Telegram because the client must reach it too.
/// </para>
/// </summary>
public static class TelegramFileCaps
{
    /// <summary>Telegram's cap for <c>sendPhoto</c> [documented].</summary>
    public const long MAX_PHOTO_BYTES = 10L * 1024 * 1024;

    /// <summary>Telegram's cap for <c>sendDocument</c> by a bot [documented].</summary>
    public const long MAX_DOCUMENT_BYTES = 50L * 1024 * 1024;

    /// <summary>
    /// Telegram's cap for a bot DOWNLOADING a file [documented]. Also the app's own ceiling on what
    /// an owner may hand it: the download is streamed into memory, so an unbounded one is a way to
    /// take the bridge down from a phone.
    /// </summary>
    public const long MAX_DOWNLOAD_BYTES = 20L * 1024 * 1024;

    /// <summary>Width + height, together, may not exceed this [documented].</summary>
    public const int MAX_PHOTO_DIMENSION_SUM = 10_000;

    /// <summary>The longer side may not be more than this many times the shorter [documented].</summary>
    public const int MAX_PHOTO_RATIO = 20;

    public static bool Is_DimensionSumAcceptable(int width, int height)
    {
        return width + height <= MAX_PHOTO_DIMENSION_SUM;
    }

    /// <summary>
    /// A zero side is NOT acceptable and is not a division either — a 0-pixel image is not a photo,
    /// and asking the ratio of one is how this became an exception rather than a verdict.
    /// </summary>
    public static bool Is_RatioAcceptable(int width, int height)
    {
        if (width <= 0 || height <= 0)
            return false;

        var longer = Math.Max(width, height);
        var shorter = Math.Min(width, height);

        return longer <= (long)shorter * MAX_PHOTO_RATIO;
    }

    /// <summary>Megabytes, for the sentences a person reads. Integer division: these caps are all whole MB.</summary>
    public static long In_Megabytes(long bytes)
    {
        return bytes / (1024 * 1024);
    }
}
