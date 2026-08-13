namespace AIOrchestratorCoreLib.Status.IdleMember;

/// <summary>
/// One member the retirement advisor has decided is worth mentioning: who it is, and how long it has
/// been declared idle.
///
/// The two live SEPARATELY here on purpose. They used to be one rendered string — "imp-2 (idle 1 h 56
/// min)" — which meant the identity and a running clock could not be told apart by anything
/// downstream, and the flag's dedup key was built from it. Keeping them apart is what lets the key be
/// the member set while the body still shows the duration.
/// </summary>
public interface IIdleMember
{
    string MemberId { get; }

    /// <summary>Already rendered ("1 h 56 min") — this type never formats and never holds a clock.</summary>
    string IdleFor { get; }
}
