namespace AIOrchestratorCoreLib.Telegram;

/// <summary>
/// Whether this topic is waiting on the OWNER, and whether the waiting has stopped the work.
///
/// The owner asked to see it from the topic LIST, without opening anything (2026-08-19): *"from the
/// topic name I should be able to immediately understand if some topic needs me for a response,
/// whether blocking or not"*.
///
/// Both facts already existed and neither was visible: `OwnerOwesReply_Decider` answers whose move
/// it is on the owner channel, and `MemberStates.BlockedOnOwner` is a member declaring it cannot
/// continue. This enum is the pairing of the two, not a new signal — nothing here decides anything
/// the app was not already deciding somewhere less visible.
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
