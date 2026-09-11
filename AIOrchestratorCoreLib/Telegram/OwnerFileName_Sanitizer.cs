namespace AIOrchestratorCoreLib.Telegram;

/// <summary>
/// TURNS A NAME THE OWNER'S PHONE CHOSE INTO A NAME THIS MACHINE MAY WRITE.
///
/// <para>
/// THE FILE NAME IS REMOTE INPUT. It arrives inside a Telegram update as `document.file_name` and is
/// whatever the sending device put there — so it is not a name, it is a string, and combining it with
/// a folder is how a write lands outside that folder. `../../.ssh/authorized_keys` is the canonical
/// shape; on Windows so is `..\\..\\startup\\run.bat`, and `C:\\keys.txt` or `/etc/passwd` need no
/// traversal at all because <see cref="Path.Combine(string, string)"/> DISCARDS the folder it was
/// given the moment the second argument is rooted. The photo path never had to think about this: it
/// names its own file `tg-{updateId}.jpg` and ignores whatever the sender called it.
/// </para>
/// <para>
/// SO ONLY THE LAST SEGMENT SURVIVES, and only its safe characters. What comes back is always a bare
/// file name — never a path, never rooted, never empty.
/// </para>
/// </summary>
public static class OwnerFileName_Sanitizer
{
    /// <summary>Long enough to stay recognisable, short enough to leave room under any path limit.</summary>
    public const int MAX_LENGTH = 80;

    /// <summary>
    /// <paramref name="fallbackStem"/> is used when the name is absent, or when nothing safe
    /// survives it — it is composed by the caller (the update id), never by the sender.
    /// </summary>
    public static string Sanitize(string? fileName, string fallbackStem)
    {
        var stem = Keep_SafeCharacters(Take_LastSegment(fileName));

        return stem.Length == 0 ? Keep_SafeCharacters(fallbackStem) : stem;
    }

    /// <summary>
    /// Everything before the final separator is dropped — both separators, on every host. Reading
    /// only <see cref="Path.DirectorySeparatorChar"/> would leave `..\\..\\x` intact on Linux, and a
    /// path built there is still a path when the file is later read on Windows.
    /// </summary>
    static string Take_LastSegment(string? fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName))
            return "";

        var trimmed = fileName.Trim();
        var lastSeparator = trimmed.LastIndexOfAny(['/', '\\', ':']);

        return lastSeparator < 0 ? trimmed : trimmed[(lastSeparator + 1)..];
    }

    /// <summary>
    /// An allow-list, not a deny-list: letters, digits, dot, dash, underscore and space. A
    /// deny-list has to know every character every filesystem treats specially — and the one it
    /// forgets is the one that matters. A name that is all dots (`.`, `..`) keeps nothing, so the
    /// caller's fallback takes over.
    /// </summary>
    static string Keep_SafeCharacters(string text)
    {
        var kept = new System.Text.StringBuilder(text.Length);

        foreach (var character in text)
        {
            if (char.IsLetterOrDigit(character) || character is '.' or '-' or '_' or ' ')
                kept.Append(character);
        }

        var result = kept.ToString().Trim().Trim('.').Trim();

        return result.Length <= MAX_LENGTH ? result : result[..MAX_LENGTH].Trim();
    }
}
