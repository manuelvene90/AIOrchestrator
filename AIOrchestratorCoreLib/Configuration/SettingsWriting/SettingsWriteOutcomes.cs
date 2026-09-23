namespace AIOrchestratorCoreLib.Configuration.SettingsWriting;

/// <summary>
/// WHAT <see cref="Settings_Writer"/> DID WITH ONE EDIT, and the only five answers a renderer has to draw.
/// <c>Applied</c> and <c>Reset</c> are the two effects; the three refusals each name WHOSE rule refused —
/// the catalogue has no such row, the row is ReadOnly, or the row's own definition turned the value down —
/// because a renderer answers each differently (a stale menu line, a pointer to where the row IS changed,
/// the definition's own message beside the box). A write that FAILED is not an outcome: it throws, for
/// <c>Atomic_FileWriter</c>'s reason — a write that did not happen must never be reported as one.
/// </summary>
public enum SettingsWriteOutcomes
{
    Applied,
    Reset,
    RefusedUnknownPath,
    RefusedReadOnly,
    RefusedInvalid,
}
