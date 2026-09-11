namespace AIOrchestratorCoreLib.Running.SessionSandbox;

/// <summary>
/// The one reader of a memory size written the way systemd writes it — <c>3G</c>, <c>3072M</c>,
/// <c>512K</c>, or a bare byte count — and the one writer of the same shape back out.
///
/// <para>
/// IT EXISTS BECAUSE THE SWAP LIMIT IS DERIVED FROM THE MEMORY ONE, and deriving it needs
/// arithmetic on a value the owner typed as text. <c>MemoryMax=3G</c> with no
/// <c>MemorySwapMax</c> is not a memory limit on a machine with swap: the cgroup simply pages,
/// the unit stays under the ceiling, and the runaway allocator that took the VPS down on
/// 2026-09-07 would have kept going — slower, and still taking everything with it.
/// </para>
/// <para>
/// The units are BINARY (K = 1024), which is what systemd means by them: "systemd-run … K, M, G,
/// T … are to the base of 1024" (systemd.resource-control(5)). An unreadable value returns null —
/// this is owner-typed text, and a parser of untrusted input answers "I cannot read that" rather
/// than guessing a number that would silently cap a session at the wrong size.
/// </para>
/// </summary>
public static class MemorySize_Parser
{
    const long KILO = 1024L;
    const long MEGA = KILO * 1024L;
    const long GIGA = MEGA * 1024L;
    const long TERA = GIGA * 1024L;

    /// <summary>The words that mean "do not limit this at all". Empty counts, so an owner can blank the key.</summary>
    public static bool Means_NoLimit(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return true;

        var word = value.Trim();

        return string.Equals(word, "none", StringComparison.OrdinalIgnoreCase)
            || string.Equals(word, "off", StringComparison.OrdinalIgnoreCase)
            || string.Equals(word, "infinity", StringComparison.OrdinalIgnoreCase)
            || word == "0";
    }

    /// <summary>
    /// Null when the text is not a size this can read — never a guessed number. Deliberately strict:
    /// what comes out of here is handed to systemd verbatim, so a spelling this accepts and systemd
    /// does not would turn every spawn on the VPS into a failed process start.
    /// </summary>
    public static long? Parse_ToBytes_OrNull(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        var text = value.Trim();

        // "3Gi" and "3GB" are the same size written by people used to other tools; the suffix
        // letters after the unit carry no information, so they are dropped rather than refused.
        if (text.EndsWith("iB", StringComparison.OrdinalIgnoreCase))
            text = text[..^2];
        else if (text.EndsWith('B') || text.EndsWith('b') || text.EndsWith('i') || text.EndsWith('I'))
            text = text[..^1];

        if (text.Length == 0)
            return null;

        var multiplier = 1L;
        var last = char.ToUpperInvariant(text[^1]);

        switch (last)
        {
            case 'K':
                multiplier = KILO;
                text = text[..^1];
                break;

            case 'M':
                multiplier = MEGA;
                text = text[..^1];
                break;

            case 'G':
                multiplier = GIGA;
                text = text[..^1];
                break;

            case 'T':
                multiplier = TERA;
                text = text[..^1];
                break;
        }

        // NO Trim() HERE, deliberately. "3 GB" would otherwise leave "3 ", parse as 3, and be read
        // as 3 G — a spelling systemd itself refuses, so the wrapper would be built and every spawn
        // would fail. Digits and one unit letter, or it is not a size this host will hand on.
        if (!long.TryParse(text, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var amount) || amount <= 0)
            return null;

        // A size written large enough to overflow is not a size; refusing it is the safe direction.
        if (amount > long.MaxValue / multiplier)
            return null;

        return amount * multiplier;
    }

    /// <summary>
    /// Bytes back to the shortest EXACT spelling — 1610612736 becomes "1536M", never "1.5G". An
    /// approximation here would be a limit the operator did not ask for.
    /// </summary>
    public static string Describe_Bytes(long bytes)
    {
        if (bytes <= 0)
            throw new ArgumentException($"a memory size must be positive, got {bytes}");

        if (bytes % TERA == 0)
            return $"{bytes / TERA}T";

        if (bytes % GIGA == 0)
            return $"{bytes / GIGA}G";

        if (bytes % MEGA == 0)
            return $"{bytes / MEGA}M";

        if (bytes % KILO == 0)
            return $"{bytes / KILO}K";

        return bytes.ToString();
    }

    /// <summary>
    /// The value in systemd's OWN spelling — "3072M" comes back as "3G" — or null when it cannot be
    /// read. Normalising rather than passing the owner's text through is what keeps a size this host
    /// accepted from being a size systemd refuses.
    /// </summary>
    public static string? Normalise_OrNull(string? value)
    {
        var bytes = Parse_ToBytes_OrNull(value);

        return bytes == null ? null : Describe_Bytes(bytes.Value);
    }

    /// <summary>
    /// Half the given size, in systemd's spelling. Null when the size could not be read, so the
    /// caller can say what it could not do rather than invent a swap ceiling.
    /// </summary>
    public static string? Halve_OrNull(string? value)
    {
        var bytes = Parse_ToBytes_OrNull(value);

        if (bytes == null || bytes.Value < 2)
            return null;

        return Describe_Bytes(bytes.Value / 2);
    }
}
