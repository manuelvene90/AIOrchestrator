using System.Globalization;
using System.Security.Cryptography;

namespace AIOrchestratorCoreLib.Telegram;

/// <summary>
/// The payload behind a question's option button: an unguessable NONCE naming the decision, plus the
/// INDEX of the option within it.
///
/// <para>
/// WHAT IT REPLACES AND WHY. The payload used to be <c>opt-{counter}</c>, where the counter was a
/// field on the engine that restarts at zero with the process. Two consequences, both live:
/// a keyboard still sitting on the phone after a restart pointed at whatever decision happened to
/// take that number next — so a tap on a question from yesterday could answer a question asked five
/// minutes ago — and the payload was guessable, which matters the moment anything but the owner can
/// reach the chat. The nonce is drawn from a cryptographic RNG and never reissued.
/// </para>
/// <para>
/// THE INDEX IS IN THE PAYLOAD, NOT LOOKED UP. Omnara's form protocol answers with option indices
/// for the same reason: an index is stable under relabelling, and it means the tap handler can
/// report WHICH option was taken even for a decision it can no longer find — the difference between
/// "expired" and "expired, and it was option 2 of the deploy question".
/// </para>
/// <para>
/// SIZE IS A HARD CONSTRAINT. Telegram caps <c>callback_data</c> at 64 BYTES and silently rejects
/// the whole keyboard beyond it — an over-long payload is not a truncated button, it is a message
/// with no buttons at all, under a question the owner is being asked to tap. The prefix, a 12-hex
/// nonce, a separator and an index leave a wide margin, and <see cref="Build"/> asserts it rather
/// than trusting the arithmetic.
/// </para>
/// <para>
/// THE "opt-" PREFIX IS KEPT DELIBERATELY. Four other button families fly around this bridge —
/// "hold:"/"go:", "cmd:", "close-yes-"/"close-no-" — and each parser returns null for anything that
/// is not its own so a tap falls THROUGH untouched rather than being swallowed. Changing the prefix
/// would have made every one of those documented non-collisions a claim about a string that no
/// longer exists.
/// </para>
/// </summary>
public static class CallbackToken
{
    /// <summary>What marks a payload as a question option. Not a prefix of any other family's.</summary>
    public const string PREFIX = "opt-";

    /// <summary>Splits the nonce from the option index.</summary>
    public const char INDEX_SEPARATOR = ':';

    /// <summary>Telegram's own limit on callback_data, in BYTES of UTF-8.</summary>
    public const int TELEGRAM_CALLBACK_DATA_MAX_BYTES = 64;

    /// <summary>
    /// 12 hex characters — 48 bits. Not a key, a name: it has to be unguessable within the lifetime
    /// of one question and unique across every question the app has ever asked, and 48 bits is
    /// several orders past both for a system whose whole population of buttons is a few thousand.
    /// </summary>
    public const int NONCE_HEX_LENGTH = 12;

    /// <summary>A fresh, never-reissued decision name.</summary>
    public static string New_Nonce()
    {
        // 6 bytes → 12 hex characters, from the cryptographic RNG rather than Random: the whole
        // point of the nonce is that a third party holding the phone cannot construct one.
        var bytes = RandomNumberGenerator.GetBytes(NONCE_HEX_LENGTH / 2);
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    public static string Build(string nonce, int optionIndex)
    {
        if (string.IsNullOrWhiteSpace(nonce))
            throw new ArgumentException("A callback token needs a nonce", nameof(nonce));

        if (optionIndex < 0)
            throw new ArgumentOutOfRangeException(nameof(optionIndex), optionIndex, "An option index is never negative");

        var data = $"{PREFIX}{nonce}{INDEX_SEPARATOR}{optionIndex.ToString(CultureInfo.InvariantCulture)}";

        // ASSERTED, NOT ASSUMED. Over the cap Telegram drops the entire keyboard, so a question
        // would reach the phone with nothing to tap and no error anywhere. Failing here puts the
        // fault in the log next to the question that caused it.
        var byteCount = System.Text.Encoding.UTF8.GetByteCount(data);

        if (byteCount > TELEGRAM_CALLBACK_DATA_MAX_BYTES)
            throw new Exception($"callback_data '{data}' is {byteCount} bytes — Telegram's limit is {TELEGRAM_CALLBACK_DATA_MAX_BYTES} and rejects the whole keyboard past it");

        return data;
    }

    /// <summary>
    /// Null for anything that is not one of ours — including a well-formed prefix with a body this
    /// cannot read. A tap that cannot be parsed must fall through to the next handler, never be
    /// answered as a malformed option.
    /// </summary>
    public static (string Nonce, int OptionIndex)? Parse_OrNull(string? callbackData)
    {
        if (callbackData == null || !callbackData.StartsWith(PREFIX, StringComparison.Ordinal))
            return null;

        var body = callbackData[PREFIX.Length..];
        var separatorIndex = body.IndexOf(INDEX_SEPARATOR);

        if (separatorIndex <= 0 || separatorIndex == body.Length - 1)
            return null;

        var nonce = body[..separatorIndex];

        if (!int.TryParse(body[(separatorIndex + 1)..], NumberStyles.None, CultureInfo.InvariantCulture, out var optionIndex))
            return null;

        return (nonce, optionIndex);
    }
}
