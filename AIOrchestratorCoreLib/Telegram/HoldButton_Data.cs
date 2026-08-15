namespace AIOrchestratorCoreLib.Telegram;

/// <summary>What the ⏸/▶ button under a receipt asks the app to do.</summary>
public enum HoldButtonActions
{
    /// <summary>Hold delivery — the owner has more to say. Same effect as typing WAIT.</summary>
    Hold,

    /// <summary>Release — the owner is done typing. Same effect as typing GO.</summary>
    Go,
}

/// <summary>
/// The callback payload behind the one-tap hold.
///
/// The owner asked for it in the words that make the case: "clicking a button is faster than typing
/// wait". Typing WAIT loses a race with the aggregation window often enough to matter, and every
/// second spent typing is a second the message is closer to going out — so the button is not a
/// convenience, it is the difference between the hold working and not.
///
/// It carries the TOPIC, because the app's whole routing is per topic and a tap arrives with no
/// text to infer one from. Telegram caps callback_data at 64 bytes; a numeric thread id and a
/// four-letter verb are nowhere near it.
///
/// Deliberately NOT registered in the single-use button registry the question flow uses. Those
/// options are a decision taken once — first tap consumes the group. This button is a TOGGLE the
/// owner may press repeatedly across a conversation, and expiring it after one press would leave a
/// dead button sitting exactly where they were told to press.
/// </summary>
public static class HoldButton_Data
{
    const string HOLD_PREFIX = "hold:";
    const string GO_PREFIX = "go:";

    /// <summary>Shown while delivery is running — pressing it holds.</summary>
    public const string HOLD_LABEL = "⏸ Wait";

    /// <summary>Shown while holding — pressing it releases.</summary>
    public const string GO_LABEL = "▶ GO";

    public static string Build(HoldButtonActions action, long? messageThreadId)
    {
        var prefix = action == HoldButtonActions.Hold ? HOLD_PREFIX : GO_PREFIX;

        // 0 for the General topic, which has no thread id — the same convention the receipt registry
        // uses, so both sides agree on what "no topic" is keyed as.
        return $"{prefix}{messageThreadId ?? 0}";
    }

    /// <summary>
    /// Null for anything that is not one of ours. A tap this cannot read must fall through to the
    /// generic option handler untouched, never be swallowed as a malformed hold.
    /// </summary>
    public static (HoldButtonActions Action, long? MessageThreadId)? Parse_OrNull(string? callbackData)
    {
        if (callbackData == null)
            return null;

        if (callbackData.StartsWith(HOLD_PREFIX, StringComparison.Ordinal))
            return Parse_WithPrefix(callbackData, HOLD_PREFIX, HoldButtonActions.Hold);

        if (callbackData.StartsWith(GO_PREFIX, StringComparison.Ordinal))
            return Parse_WithPrefix(callbackData, GO_PREFIX, HoldButtonActions.Go);

        return null;
    }

    static (HoldButtonActions Action, long? MessageThreadId)? Parse_WithPrefix(string callbackData, string prefix, HoldButtonActions action)
    {
        if (!long.TryParse(callbackData[prefix.Length..], out var threadId))
            return null;

        return (action, threadId == 0 ? null : threadId);
    }
}
