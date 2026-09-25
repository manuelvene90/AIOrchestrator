namespace AIOrchestratorCoreLib.Bridge.Siblings;

/// <summary>
/// The archive labels of a refused <c>spawn-sibling</c> request — the word
/// <c>Archive_ResolvedRequest_BestEffort</c> files it under, and the thing a person grepping the
/// resolved folder searches for. One constant per row of spec 2026-09-23 §4.2's table, plus two that
/// are not rows there: <see cref="WORKTREE_NOT_ABSOLUTE"/> (the Task 6 carry) and
/// <see cref="LINKED_ORCHESTRATION"/> (a promotion refused for a linked orchestration, §7.6, Task 14).
/// </summary>
public static class SiblingRefusals
{
    public const string UNSPAWNABLE = "unspawnable";
    public const string NOT_A_SOLO = "not-a-solo";
    public const string HANDOVER_ALREADY_USED = "handover-already-used";
    public const string NO_HANDOVER_ENTRY = "no-handover-entry";
    public const string AT_CAP = "at-cap";

    /// <summary>
    /// A relative <c>worktree</c>. Not a row of the spec's table; added by the Task 6 carry (2026-09-23):
    /// resolved against the app's current directory a relative path names nobody's worktree, and a
    /// stored one would be the cwd of every respawn — so it is refused with its own reason rather than
    /// being answered "missing" or, by luck, "exists".
    /// </summary>
    public const string WORKTREE_NOT_ABSOLUTE = "worktree-not-absolute";
    public const string WORKTREE_MISSING = "worktree-missing";
    public const string WORKTREE_NOT_OF_REPO = "worktree-not-of-repo";
    public const string WORKTREE_SHARED = "worktree-shared";
    public const string NAME_TAKEN = "name-taken";

    /// <summary>Used by Task 14: a linked orchestration may not be promoted to a crew in v1 (§7.6).</summary>
    public const string LINKED_ORCHESTRATION = "linked-orchestration";
}
