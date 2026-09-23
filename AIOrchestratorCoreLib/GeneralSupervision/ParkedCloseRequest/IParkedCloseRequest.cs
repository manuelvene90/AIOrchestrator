namespace AIOrchestratorCoreLib.GeneralSupervision.ParkedCloseRequest;

/// <summary>
/// What kind of decision is waiting for the owner's tap. The parked folder holds all of them, and the
/// difference decides what is asked, what is executed, and what the requester is told.
///
/// **THE NAME IS NOW WRONG AND THAT IS ACKNOWLEDGED DEBT, NOT A CHOICE.** Since 2026-08-13 this also
/// carries a PROMOTION, which is not a close. What these types model is "a request parked for the
/// owner's tap", so the family should be `ParkedConfirmation*`.
///
/// The rename is DEFERRED deliberately: `imp-4` is deleting member closes from these same files, and
/// a rename colliding with a deletion, in the machine that closes orchestrations, is the worst place
/// to spend merge risk. It follows once that branch lands. Written here so the next reader knows the
/// name is owed rather than intended.
/// </summary>
public enum ParkedCloseKinds
{
    /// <summary>Ends every session in the orchestration and deletes its Telegram topic.</summary>
    Orchestration,

    /// <summary>Retires ONE member — its terminal is killed, the orchestration keeps running.</summary>
    Implementer,

    /// <summary>
    /// A basic orchestration becomes a full crew: the solo ends, a supervisor takes over its channel,
    /// imp-1 spawns empty. It fits this lifecycle unchanged because it DOES close something — the solo
    /// — and because it is the same shape of decision: expensive, effectively one-way, and the owner's
    /// to make.
    /// </summary>
    Promotion,

    /// <summary>
    /// A solo asks for a SIBLING solo — a second orchestration in its own topic and worktree, linked by an
    /// endeavour (spec 2026-09-23 §4.3). It closes nothing; it is here because it is the same shape of
    /// decision as a promotion: a second session running indefinitely is the owner's to start (O1).
    ///
    /// LAST, so no persisted ordinal moves: engine state stores the kind by NAME today, but an enum that
    /// grows in the middle is one serializer change away from re-reading every parked Promotion as this.
    /// </summary>
    Sibling,
}

/// <summary>
/// A close request parked in <c>awaiting-owner/</c>, read back as one shape whichever kind it is.
///
/// It exists because the confirmation lifecycle was written for a single request type and read every
/// parked file through <c>Read_CloseOrchestrationRequest_OrNull</c>. A parked close-implementer
/// returns null from that reader, so without this the guard would have filed a perfectly valid
/// request as "unreadable" and told its requester to drop a fresh one.
/// </summary>
public interface IParkedCloseRequest
{
    ParkedCloseKinds Kind { get; }

    string OrchId { get; }

    /// <summary>The member being retired, and null for an orchestration close.</summary>
    string? MemberId { get; }

    /// <summary>
    /// Who asked, as recorded by the request itself.
    ///
    /// A close-orchestration request REQUIRES this field — an orchestration was once closed with
    /// nothing on disk able to say who asked, and a default would re-create that hole under a new
    /// name. The close-implementer schema carries no such field, so this describes the file rather
    /// than naming a session: the owner is not shown a name that nothing verified.
    /// </summary>
    string Requester { get; }

    /// <summary>WHY, in one line, relayed to the owner. Never silent.</summary>
    string Reason { get; }

    string ParkedFilePath { get; }

    /// <summary>
    /// The whole spawn-sibling request for <see cref="ParkedCloseKinds.Sibling"/>, and null for the other
    /// three kinds. The prompt and the birth need its name, job, worktree and handover — fields no other
    /// kind has — so it travels whole rather than being flattened into slots the other kinds would leave
    /// empty or, worse, reuse.
    /// </summary>
    SpawnSiblingRequest.ISpawnSiblingRequest? Sibling { get; }
}
