using System.Globalization;
using System.Security.Cryptography;

namespace AIOrchestratorCoreLib.Bridge.Decisions;

/// <summary>
/// The four digits the owner has to type back before a high-risk decision is taken — the read-back
/// half of the second gesture.
///
/// <para>
/// IT IS A READ-BACK, NOT A SECRET. The code is shown in the very message that asks for it, exactly
/// as an air traffic read-back repeats the clearance that was just transmitted: what it proves is
/// not that the person knows something, but that a second, deliberate action was taken by someone
/// looking at the screen. That is the defence against the failure it exists for — an unlocked phone
/// in the wrong hand, or a thumb on the wrong button — and it is why four digits are enough.
/// </para>
/// <para>
/// IT STILL NEVER GOES IN A LOG OR A CHANNEL. A code that is visible for ten minutes in one Telegram
/// message is a different object from a code sitting for ever in an append-only file that agents
/// read. The rule is stated here because this is where someone would reach for a debug line.
/// </para>
/// <para>
/// COMPARED IN CONSTANT TIME anyway. There is nothing to steal by timing four digits an attacker can
/// already see, so this is not the defence — it is the habit, kept because the cost is one call and
/// the alternative teaches the wrong reflex in the one file about approving a production push.
/// </para>
/// </summary>
public static class ConfirmationCode
{
    public const int DIGITS = 4;

    /// <summary>
    /// A fresh code. Drawn from the cryptographic RNG rather than <c>Random</c> — not because the
    /// code is a secret, but because <c>Random</c> seeded per-process would repeat across a restart,
    /// and a code the owner has seen before is a code they can type without reading.
    /// </summary>
    public static string Generate()
    {
        // 0..9999, rendered with leading zeros so every code is the same shape on the screen.
        var value = RandomNumberGenerator.GetInt32(0, 10_000);
        return value.ToString(CultureInfo.InvariantCulture).PadLeft(DIGITS, '0');
    }

    /// <summary>
    /// True when the owner's message IS the code. Deliberately strict about content and forgiving
    /// about presentation: surrounding whitespace is theirs, anything else is not the code — a
    /// message that merely CONTAINS four digits ("push 1234 commits?") must never approve a deploy.
    /// </summary>
    public static bool Matches(string? ownerText, string expectedCode)
    {
        if (ownerText == null)
            return false;

        var typed = ownerText.Trim();

        if (typed.Length != expectedCode.Length)
            return false;

        return CryptographicOperations.FixedTimeEquals(
            System.Text.Encoding.UTF8.GetBytes(typed),
            System.Text.Encoding.UTF8.GetBytes(expectedCode));
    }

    /// <summary>
    /// Whether a message is SHAPED like a confirmation attempt — all digits, right length. Used to
    /// tell "they typed the wrong code" from "they changed the subject": the first deserves a reply
    /// saying so, the second is an ordinary message and must reach the session untouched.
    /// </summary>
    public static bool Looks_LikeACode(string? ownerText)
    {
        if (ownerText == null)
            return false;

        var typed = ownerText.Trim();

        return typed.Length == DIGITS && typed.All(char.IsAsciiDigit);
    }
}
