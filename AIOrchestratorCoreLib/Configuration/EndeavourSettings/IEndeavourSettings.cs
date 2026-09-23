namespace AIOrchestratorCoreLib.Configuration.EndeavourSettings;

/// <summary>
/// THE <c>endeavour</c> BLOCK — today one row, <c>endeavour.maxOpenSiblings</c>, the cap on how many
/// sibling solos one endeavour may have open at once (spec 2026-09-23 §4.2, owner decision O2).
///
/// <para>
/// DATA, NOT CODE, on the lesson <c>IEffortSettings</c> records: a cap compiled into the binary is a cap
/// the owner cannot move without a rebuilt app actually running (CLAUDE.md decision 23). Each extra
/// sibling costs a topic, a session, and another block in every sibling's digest, so the owner is the
/// one who decides how many that is worth.
/// </para>
/// </summary>
public interface IEndeavourSettings
{
    /// <summary>
    /// The most OPEN MEMBERS one endeavour may have, THE REQUESTER INCLUDED — so 3 means a solo and two
    /// siblings, and a <c>spawn-sibling</c> from an endeavour already at 3 is refused <c>at-cap</c>. Counting
    /// members rather than "extra siblings" is the spec's own wording ("already has N open members") and
    /// keeps the number the owner types the number of topics they will see. Always at least 1.
    /// </summary>
    int MaxOpenSiblings { get; }
}
