using AIOrchestratorCoreLib.Telegram;

namespace AIOrchestratorCoreLib.Bridge;

/// <summary>
/// WHETHER THE "I HAVE YOUR MESSAGE" RECEIPT IS FIRST TRIED AS A REACTION — the catalogue's
/// <c>phone.receipts</c>, decided once per owner message (plan 03 Task 6).
///
/// <para>
/// THE TWO PHONES. Master's (<c>ticks</c>, classic, and the machine that names no preset) posts a
/// silent ✓ under the owner's message and edits it to ✓✓ when a session is handed it — the owner,
/// 2026-09-23: *"I like the double tick message to confirm that the message has arrived and then
/// that the message has been handed to the sup/solo"*, and the ✓ is also where ⏸ Wait rides. The
/// fork's (<c>reactions</c>, quiet, brief D) puts 👀 then 👌 on the owner's own bubble and adds no
/// message at all. Before this task the engine tried the reaction first whatever the setting said, so
/// classic's ✓ — and the hold button on it — only ever appeared when Telegram refused a reaction.
/// </para>
/// <para>
/// UNDER <c>ticks</c> THE REACTION IS NOT ATTEMPTED AT ALL. Attempting it and discarding the result
/// would spend a Telegram call and a rate-limit slot per owner message on an answer nobody reads.
/// </para>
/// <para>
/// THE FALLBACK IS NOT THE SETTING. Under <c>reactions</c> a refused reaction (400 for a message a
/// bot may not react to, 429 under load) or an update with no message id to react to still gets
/// the ✓ — the caller keeps that path unconditionally, because an acknowledgement that silently did
/// not happen is the owner watching their message vanish. This answers only which path is tried
/// FIRST.
/// </para>
/// <para>
/// WHAT HAPPENS AT DELIVERY FOLLOWS WHAT WAS SENT, not this answer: the engine registers the 👀 or
/// the ✓ it actually produced, and the 👌 or the ✓✓ edits that one. A hot edit of the key between
/// receipt and delivery therefore finishes the exchange in the style it started in, and takes effect
/// on the next owner message — the value is read at the point of effect and never cached.
/// </para>
/// </summary>
public static class ReceiptStyle_Decider
{
    /// <summary>
    /// True when the receipt should first be tried as a 👀 reaction on <paramref name="ownerMessageId"/>.
    /// Pure: the caller reads <c>phone.receipts</c> from the current config and hands it in.
    /// </summary>
    public static bool Should_TryReaction(ReceiptStyles receipts, long? ownerMessageId)
    {
        return receipts == ReceiptStyles.Reactions && ownerMessageId != null;
    }
}
