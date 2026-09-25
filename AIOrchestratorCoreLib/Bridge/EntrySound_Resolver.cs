using AIOrchestratorCoreLib.Channels;
using AIOrchestratorCoreLib.Channels.ChannelEntry;
using AIOrchestratorCoreLib.Telegram;

namespace AIOrchestratorCoreLib.Bridge;

/// <summary>
/// WHETHER ONE MIRRORED CHANNEL ENTRY RINGS THE OWNER'S PHONE — the one helper every mirror send of an
/// entry takes its sound from (the body pieces, the long-entry document, the photos, the attachments).
/// Moved out of the engine when plan 03 Task 3 gave it a setting to read.
///
/// <para>
/// WHO WROTE IT COMES FIRST — the owner's ruling of 2026-09-09: *"If the supervisor writes to me, I must
/// know it — that rings. Status, receipts and app bookkeeping do not ring."* An entry whose author does
/// not SPEAK TO THE OWNER (<see cref="ChannelAuthor_Kinds.Speaks_ToOwner"/>: the supervisor, or the solo
/// that stands in for one) is silent under every setting. That is the same predicate the away detection
/// and the stall alert use, so a new author kind cannot ring here while counting as silence there.
/// </para>
/// <para>
/// THEN <c>phone.appMessagesRing</c>, READ UNDER D7's ANSWER (b) (coordinator, 2026-09-14). The catalogue
/// first described the key as "the app's own messages", and Task 3's measurement confirmed what the plan
/// feared: no receipt, busy narration or turn-ended line rang on this tree, so that reading would have
/// made the key — and quiet, which sets it false — do nothing. Under (b) <c>false</c> silences the
/// session's NARRATION: an entry still rings when <c>phone.push = filtered</c> would have sent it at once
/// — a question, a <c>BLOCKED ON OWNER</c>, a file for the owner, the boot greeting, THE answer — and
/// arrives silently otherwise. So quiet's phone is "everything arrives, quietly" and classic's is "only
/// what matters arrives, and it rings".
/// </para>
/// <para>
/// THE PREDICATE IS ASKED, NEVER RESTATED: <see cref="OwnerPush_Policy.Decide"/> under
/// <see cref="PhonePushModes.Filtered"/>, whatever the channel's own push mode is (decision 12). The mode
/// is deliberately NOT a parameter. Under <c>everything</c> <c>Decide</c> answers "send" for every entry,
/// so asking it in the channel's mode would make <c>false</c> silence nothing that is not already
/// dropped; under <c>filtered</c> every entry that reaches a send has already passed this same predicate.
/// Delivery and sound are two questions (§7.1), and an owner may pair either answer with either.
/// </para>
/// <para>
/// GENERAL IS NOT EXEMPT HERE, unlike from <c>phone.push</c> (D8). D8's reason is delivery — filtering
/// General would stop the concierge's words reaching the phone at all — and a silent message still
/// arrives. The owner who sets <c>false</c> gets General's narration quietly too.
/// </para>
/// </summary>
public static class EntrySound_Resolver
{
    /// <param name="entry">The channel entry being mirrored.</param>
    /// <param name="ownerIsWaitingForAReply">
    /// The owner's answer credit for this orchestration, as the push block read it for THIS entry — the
    /// "answer" member of the predicate. Always false off an owner channel, where no credit exists.
    /// </param>
    /// <param name="appMessagesRing"><c>phone.appMessagesRing</c>, resolved by the caller at the point of effect.</param>
    public static TelegramSendSounds Resolve(IChannelEntry entry, bool ownerIsWaitingForAReply, bool appMessagesRing)
    {
        if (!ChannelAuthor_Kinds.Speaks_ToOwner(entry.Author))
            return TelegramSendSounds.Silent;

        if (appMessagesRing)
            return TelegramSendSounds.Rings;

        return OwnerPush_Policy.Decide(PhonePushModes.Filtered, entry.RawText, ownerIsWaitingForAReply, entry.Subject) == OwnerPushDecisions.SendNow
            ? TelegramSendSounds.Rings
            : TelegramSendSounds.Silent;
    }
}
