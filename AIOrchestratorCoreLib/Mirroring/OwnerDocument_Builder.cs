using System.Text;
using AIOrchestratorCoreLib.Telegram;

namespace AIOrchestratorCoreLib.Mirroring;

/// <summary>
/// THE FILE THAT RIDES ALONGSIDE A VERY LONG ENTRY, so the owner can read it in one scroll instead of
/// stitching four chat messages together.
///
/// <para>
/// WHY: <see cref="OwnerMessage_Folder"/> makes a long entry cheap to skip and one tap to read, and
/// that is enough up to a point. Past it — a full review, a plan, a sweep — the entry is a DOCUMENT
/// the owner wants to scroll, search and keep, and Telegram already has the shape for that:
/// <c>sendDocument</c>, with the original Markdown as an attachment the phone previews and the
/// desktop opens in an editor.
/// </para>
/// <para>
/// A CONVENIENCE, NEVER A REPLACEMENT. The chunks still go, all of them, in order. An attachment that
/// replaced the messages would put the owner's answer behind a download on a train with no signal,
/// and "the message always arrives in the chat" is the one promise this whole path is built on.
/// </para>
/// </summary>
public static class OwnerDocument_Builder
{
    /// <summary>
    /// How many delivered messages an entry has to EXCEED before it is also attached. Three is the
    /// point at which the fold has stopped helping: the owner is scrolling between messages to read
    /// one statement.
    /// </summary>
    public const int DEFAULT_ATTACH_ABOVE_CHUNKS = 3;

    /// <summary>Telegram's cap on a document caption, counted after entity parsing like every other length here.</summary>
    public const int CAPTION_LIMIT = 1024;

    /// <summary>How much of the subject survives into the file name — long enough to identify, short enough to read on a phone.</summary>
    public const int SLUG_LIMIT = 48;

    public const string FALLBACK_SLUG = "entry";

    /// <summary>Zero or less disables the attachment entirely, the same way a zero fold threshold disables the fold.</summary>
    public static bool Should_Attach(int deliveredMessages, int attachAbove)
    {
        return attachAbove > 0 && deliveredMessages > attachAbove;
    }

    /// <summary>
    /// <c>&lt;subject-slug&gt;.md</c>. The subject is AGENT-WRITTEN and therefore untrusted (CLAUDE.md
    /// decision 12): everything that is not an ASCII letter or digit becomes a hyphen, so no path
    /// separator, no dot and no control character can reach a file name — and a subject that survives
    /// as nothing at all falls back to a fixed word rather than to an empty name.
    /// </summary>
    public static string Build_FileName(string? subject)
    {
        var slug = new StringBuilder();

        foreach (var character in subject ?? string.Empty)
        {
            if (char.IsAsciiLetterOrDigit(character))
            {
                slug.Append(char.ToLowerInvariant(character));
                continue;
            }

            // Collapsed, so "a — b" does not become "a---b".
            if (slug.Length > 0 && slug[^1] != '-')
                slug.Append('-');
        }

        var trimmed = slug.ToString().Trim('-');

        if (trimmed.Length > SLUG_LIMIT)
            trimmed = trimmed[..SLUG_LIMIT].TrimEnd('-');

        return (trimmed.Length == 0 ? FALLBACK_SLUG : trimmed) + ".md";
    }

    /// <summary>
    /// UTF-8 WITHOUT A BYTE-ORDER MARK. The file is Markdown an owner opens in a phone viewer, and a
    /// BOM shows up there as a stray character on the first line — on the very line the caption is
    /// also quoting.
    /// </summary>
    public static byte[] Build_Content(string markdown)
    {
        return new UTF8Encoding(encoderShouldEmitUTF8Identifier: false).GetBytes(markdown);
    }

    /// <summary>
    /// The caption: the entry's first line, rendered, and short enough that Telegram accepts it.
    ///
    /// <para>
    /// TRIMMED ON THE MARKDOWN AND RENDERED AFTERWARDS, never the other way round. Cutting HTML at
    /// 1024 characters can cut a tag in half, which is a 400 — and a 400 on the caption fails the
    /// whole <c>sendDocument</c>. Cutting the Markdown can at worst leave an unbalanced <c>**</c>,
    /// which <see cref="TelegramHtml_Renderer"/> renders as literal text by design.
    /// </para>
    /// </summary>
    public static string Build_CaptionHtml(string text)
    {
        var firstLine = Read_FirstLine(text);

        while (TelegramText_Ruler.Count_AfterEntityParsing(TelegramHtml_Renderer.Render(firstLine)) > CAPTION_LIMIT
            && firstLine.Length > 1)
        {
            // Halved rather than trimmed to the overshoot: escaping grows characters unevenly, so
            // subtracting the difference can loop many times on a line that is mostly markup.
            //
            // AND NEVER THROUGH THE MIDDLE OF AN EMOJI (brief F4): a caption that ends in half a
            // surrogate pair is a 400 on sendDocument, which fails the WHOLE upload — the file, not
            // just its caption. Truncate_WholeCharacters gives back at most one code unit, so the
            // halving still terminates.
            firstLine = TelegramText_Ruler.Truncate_WholeCharacters(firstLine, Math.Max(1, firstLine.Length / 2));
        }

        return TelegramHtml_Renderer.Render(firstLine);
    }

    static string Read_FirstLine(string text)
    {
        var normalised = text.Replace("\r\n", "\n").Replace('\r', '\n');
        var lineEnd = normalised.IndexOf('\n');

        return lineEnd < 0 ? normalised : normalised[..lineEnd];
    }
}
