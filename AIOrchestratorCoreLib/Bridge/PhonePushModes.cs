namespace AIOrchestratorCoreLib.Bridge;

/// <summary>
/// WHAT OF AN ORCHESTRATION'S OWNER CHANNEL REACHES THE PHONE — the catalogue's <c>phone.push</c>.
///
/// <para>
/// TWO WAYS THE SAME MACHINE HAS SHIPPED. Master filtered: only what asks, is blocked, carries a file,
/// or is THE answer rang the owner (<c>OwnerPush_Policy</c>'s predicates). The fork abolished the
/// filter on 2026-09-09 on the owner's ruling — "if the supervisor writes to me, I must know it" — and
/// mirrors every entry (CLAUDE.md decision 25). Both are now a choice rather than a build, and the
/// shipped default is the filter.
/// </para>
/// </summary>
public enum PhonePushModes
{
    /// <summary>Only the entries <c>OwnerPush_Policy</c> says ask, block, attach or answer. The shipped default.</summary>
    Filtered,

    /// <summary>Every owner-channel entry except an empty body and the owner's own words quoted back.</summary>
    Everything,
}

/// <summary>Reads a <c>phone.push</c> word without a switch at each call site.</summary>
public static class PhonePush_Modes
{
    /// <summary>The catalogue's words for <c>phone.push</c> — the drift between the two is pinned by <c>PhoneSettingsJsonTests</c>.</summary>
    public const string FILTERED_TEXT = "filtered";
    public const string EVERYTHING_TEXT = "everything";

    /// <summary>
    /// Parses the catalogue path <c>phone.push</c>. Ordinal-case-insensitive; an absent, blank or
    /// UNRECOGNISED word reads as <see cref="PhonePushModes.Filtered"/>, the catalogue's shipped
    /// default — a typo must cost the owner the default, never the phone.
    /// </summary>
    public static PhonePushModes Parse_OrFiltered(string? word)
    {
        return string.Equals(word?.Trim(), EVERYTHING_TEXT, StringComparison.OrdinalIgnoreCase)
            ? PhonePushModes.Everything
            : PhonePushModes.Filtered;
    }
}
