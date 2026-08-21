namespace AIOrchestratorCoreLib.Telegram;

/// <summary>
/// Whether this topic is waiting on the OWNER, and whether the waiting has stopped the work.
///
/// The owner asked to see it from the topic LIST, without opening anything (2026-08-19): *"from the
/// topic name I should be able to immediately understand if some topic needs me for a response,
/// whether blocking or not"*.
///
/// `MemberStates.BlockedOnOwner` is a member declaring it cannot continue, and that is what draws
/// ⛔.
///
/// ❓ WAS ORIGINALLY WIRED TO THE WRONG DECIDER and is now `OwnerQuestionPending_Decider`. It was
/// built on `OwnerOwesReply_Decider`, which answers "whose move is it on the owner channel" — true
/// after every report a session writes, including its answer to the owner's own message. So the
/// glyph was lit almost permanently, and the owner asked the obvious question on 2026-08-21: *"If
/// there are no questions, why did they put the question mark in the topic name?"* `Wanted` now
/// means an actual unanswered question, tested by the same reader that decides what reaches their
/// phone. `OwnerOwesReply_Decider` is unchanged and still correct for its own callers — the stall
/// alert and the app's session rows, which really do want "whose move is it".
/// </summary>
public enum OwnerReplyStates
{
    /// <summary>Nobody is waiting on them.</summary>
    None,

    /// <summary>Waiting on them, and the endeavour is still moving meanwhile.</summary>
    Wanted,

    /// <summary>Waiting on them AND stopped: a member has declared BLOCKED ON OWNER.</summary>
    Blocking,
}
