namespace AIOrchestratorCoreLib.Telegram;

/// <summary>
/// Whether THIS HOST polls Telegram for the owner's messages, or only mirrors outward.
///
/// <para>
/// ONE BOT TOKEN ALLOWS ONE POLLER. A second `getUpdates` on the same token gets HTTP 409 and
/// neither host receives reliably — the owner taps a button and nothing happens, or happens twice.
/// <see cref="Composition.SingleInstance_Guard"/> already excludes two hosts that share a
/// supervision root, which is the whole of the single-machine case; what it cannot see, and says so
/// in its own summary, is two hosts on DIFFERENT roots — the Windows app on a desk and the daemon on
/// a VPS. No file under either root can see the other one, so this is a per-host DECISION rather
/// than something to detect: the host that should not poll is told so in its own config.json.
/// </para>
/// <para>
/// THE DEFAULT IS <see cref="Poll"/>, deliberately. A host that silently stops polling is a phone
/// that silently stops working, and it cannot even report the reason — it is not polling, so it
/// never sees the 409 that would have explained it. Polling by default keeps today's behaviour and
/// makes the collision LOUD (one message in General naming this machine) instead of invisible.
/// </para>
/// </summary>
public enum TelegramInboundModes
{
    /// <summary>Long-poll `getUpdates` — the owner's messages and taps reach this host. The default.</summary>
    Poll,

    /// <summary>
    /// Mirror only: channel entries still reach the phone, nothing is read back. For the second
    /// host when two of them share one bot token and cannot see each other's supervision root.
    /// </summary>
    Off,
}

/// <summary>Reads the <c>telegramInbound</c> config value without a switch at each call site.</summary>
public static class TelegramInbound_Modes
{
    public const string POLL_TEXT = "poll";
    public const string OFF_TEXT = "off";

    /// <summary>
    /// An absent, empty or UNRECOGNISED value reads as <see cref="TelegramInboundModes.Poll"/>. A
    /// typo in a hand-edited key must not be the thing that stops the owner's phone from working —
    /// see the enum's own note on why the safe direction is to keep polling.
    /// </summary>
    public static TelegramInboundModes Parse_OrPoll(string? text)
    {
        return string.Equals(text?.Trim(), OFF_TEXT, StringComparison.OrdinalIgnoreCase)
            ? TelegramInboundModes.Off
            : TelegramInboundModes.Poll;
    }

    public static string Describe(TelegramInboundModes mode)
    {
        return mode == TelegramInboundModes.Off ? OFF_TEXT : POLL_TEXT;
    }
}
