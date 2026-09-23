namespace AIOrchestratorCoreLib.Configuration.SettingsWriting;

/// <summary>
/// WHAT <see cref="Settings_Writer"/> DID WITH ONE EDIT, and the only six answers a renderer has to draw.
/// <c>Applied</c> and <c>Reset</c> are the two effects; the three refusals each name WHOSE rule refused —
/// the catalogue has no such row, the row is ReadOnly, or the row's own definition turned the value down —
/// because a renderer answers each differently (a stale menu line, a pointer to where the row IS changed,
/// the definition's own message beside the box).
///
/// <para>
/// <c>WriteFailed</c> IS THE FILE'S REFUSAL, NOT THE EDIT'S (plan 04 Task 2b, ruling P33, 2026-09-23): the
/// edit was acceptable, but config.json is present and could not be read, or was read and does not parse, so
/// the writer wrote NOTHING rather than replace a file it could not understand with one holding only the
/// edited keys — the owner's repos, chat ids and hand-edited keys erased by a phone tap. The file is byte for
/// byte as it was, and the message says which of the two it was and what to do (fix it by hand; try again).
/// It is named for what the owner sees — their write did not happen — and a renderer draws it as it draws a
/// write that threw. A write that FAILED part-way (the rename refused, the disk full) is still not an
/// outcome: it throws, for <c>Atomic_FileWriter</c>'s reason — a write that did not happen must never be
/// reported as one.
/// </para>
/// </summary>
public enum SettingsWriteOutcomes
{
    Applied,
    Reset,
    RefusedUnknownPath,
    RefusedReadOnly,
    RefusedInvalid,
    WriteFailed,
}
