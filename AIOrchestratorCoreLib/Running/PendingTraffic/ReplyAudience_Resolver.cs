using AIOrchestratorCoreLib.Channels;
using AIOrchestratorCoreLib.Running.TurnSource;

namespace AIOrchestratorCoreLib.Running.PendingTraffic;

/// <summary>
/// WHO A BRIDGE-DRIVEN TURN'S REPLY IS FOR, decided by what woke the turn (sibling plan 2026-09-23, ruling S4
/// on the Task 13 review's I1; owner decision O3: "sibling-to-sibling traffic is never texted", spec §2.2).
///
/// <para>
/// A solo's reply is always filed in its own owner channel. That is deliberate: a sibling's outbox is never
/// a reply target (<see cref="TurnSources_Resolver.Select_ReplyTargets"/>), and filing into the solo's OWN
/// outbox would have two print solos acknowledge each other for ever. But the owner channel is MIRRORED, so a
/// turn only siblings woke put sibling chatter on the owner's phone — and, while the owner was waiting on
/// something else, it went out as the answer and spent their credit.
/// </para>
/// <para>
/// SO THE REPLY OF A SIBLING-ONLY TURN IS <see cref="AppEntryAudiences.Agent"/>: filed with the
/// <see cref="AppEntryAudience_Tag"/>, the one mark the tree already has for "written, not texted", which the
/// mirror and the answered-count both honour. Anything the owner is part of — their own entry in the pending
/// set, or a boot turn with nothing pending, whose greeting is how the owner learns the session is up — is
/// <see cref="AppEntryAudiences.Owner"/>, exactly as before: the owner is waiting, and the reply is theirs.
/// </para>
/// </summary>
public static class ReplyAudience_Resolver
{
    public static AppEntryAudiences Resolve(IReadOnlyList<PendingEntry> pending)
    {
        return pending.Count > 0 && pending.All(item => item.Source.Kind == TurnSourceKinds.Sibling)
            ? AppEntryAudiences.Agent
            : AppEntryAudiences.Owner;
    }
}
