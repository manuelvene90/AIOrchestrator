namespace AIOrchestratorCoreLib.Telegram;

/// <summary>
/// THE TWO REACTIONS THE BRIDGE PUTS ON THE OWNER'S OWN MESSAGE — brief D.
///
/// <para>
/// The owner asked for it in those words (2026-09-09): *"acknowledge my messages with a reaction on
/// my bubble instead of a ✓ message. 👀 → 👌 is fine."* A receipt that is a MESSAGE costs a line in
/// the topic for every line the owner writes; a reaction costs none and sits on the thing it is
/// about.
/// </para>
/// <para>
/// TELEGRAM ALLOWS ONE REACTION PER MESSAGE FROM A BOT, AND ONLY FROM A FIXED SET. <c>✅</c> — the
/// obvious first choice for "done" — is NOT in it, which is exactly the kind of thing that is
/// discovered as a 400 in production. The permitted set is encoded here so a later edit picks from
/// it rather than from taste, and <see cref="Is_Permitted"/> is asserted against the emoji this
/// file itself ships.
/// </para>
/// <para>
/// SETTING a reaction needs no <c>allowed_updates</c> change. READING the owner's reactions would
/// need <c>message_reaction</c> added there, and that is deliberately not in scope: nothing here
/// listens for the owner reacting back.
/// </para>
/// </summary>
public static class OwnerReaction_Emoji
{
    /// <summary>Appended to the channel — the bridge has it, nobody has read it yet.</summary>
    public const string RECEIVED = "👀";

    /// <summary>A session started a turn with it. The owner's message has landed somewhere that acts.</summary>
    public const string PICKED_UP = "👌";

    /// <summary>
    /// The reactions a bot may set (Bot API 7.0+). Not the full list Telegram publishes — the subset
    /// this app would ever plausibly reach for, plus the two it uses. The point of the constant is
    /// the EXCLUSION: a check mark is not on it.
    /// </summary>
    public static readonly IReadOnlyList<string> PERMITTED = ["👀", "👌", "👍", "🫡", "✍", "🤝"];

    public static bool Is_Permitted(string emoji)
    {
        return PERMITTED.Contains(emoji);
    }
}
