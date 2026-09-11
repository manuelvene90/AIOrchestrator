using System.Security.Cryptography;
using System.Text;
using AIOrchestratorCoreLib.Channels.ChannelEntry;

namespace AIOrchestratorCoreLib.Channels;

/// <summary>
/// THE IDENTITY OF ONE CHANNEL ENTRY, derived from what it SAYS rather than from where it sits or
/// what number it claims.
///
/// <para>
/// Neither of the two obvious identities survives this system. <b>Position</b> does not:
/// <see cref="Channel_Compactor"/> moves older entries into a sibling archive, so an entry's offset
/// in the live file is not stable and a count of entries is not monotonic — CLAUDE.md decision 13,
/// which cost this repo a nudge loop that could never clear. <b>The <c>[n]</c> in the header</b> does
/// not either: it is written by the agent and is a guess unless the writer re-read the file —
/// decision 12, with a live incident of two <c>[80]</c> and two <c>[81]</c> in one channel.
/// </para>
/// <para>
/// WHY THAT MATTERS HERE AND NOT BEFORE. While a bridge-driven session was woken by ONE channel, every
/// entry in it was written by the bridge itself, so its numbering was the bridge's own and a cursor
/// could trust it. A supervisor woken by its members' spokes reads entries written by TERMINAL
/// sessions, whose headers are exactly the untrusted ones decision 12 describes — and a duplicate or
/// lower <c>[n]</c> under an index cursor means an implementer's filed report is never handed over and
/// nothing anywhere says so. That is the silent direction, so the cursor stopped being an index.
/// </para>
/// <para>
/// THE REPO ALREADY DECIDED THIS ONCE, in <see cref="Status.Nudge_Decider.Identify_NudgeSubject"/>,
/// which keys on the entry's raw text for the same reasons in the same words. This hashes rather than
/// carrying the text because a cursor holds one identity per live entry and is written to disk on
/// every turn; the rule is identical, the storage is not.
/// </para>
/// <para>
/// TWO ENTRIES WITH THE SAME DIGEST ARE THE SAME ENTRY, deliberately. The raw text includes the whole
/// header — index, author, timestamp, subject — so a collision needs a byte-identical entry, header
/// and all. Two of those are indistinguishable to every reader in this system, including a human, and
/// delivering one of them once is the right answer.
/// </para>
/// </summary>
public static class ChannelEntry_Digest
{
    /// <summary>
    /// Hex characters kept. 16 is 64 bits against the at most
    /// <see cref="Channel_Compactor.COMPACT_ABOVE_ENTRIES"/> entries a live file holds — a birthday
    /// collision there is on the order of 10^-16, and the consequence of one would be a single
    /// undelivered entry, not corruption.
    /// </summary>
    public const int LENGTH = 16;

    public static string Compute(IChannelEntry entry)
    {
        return Compute(entry.RawText);
    }

    /// <summary>
    /// Line endings are normalised and the ends trimmed before hashing: the same entry read back from
    /// a file written on another platform is the same entry, and a cursor that disagreed with itself
    /// across a line-ending change would re-deliver a whole channel.
    /// </summary>
    public static string Compute(string rawText)
    {
        var normalized = (rawText ?? string.Empty).Replace("\r\n", "\n").Trim();

        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(normalized)))[..LENGTH].ToLowerInvariant();
    }
}
