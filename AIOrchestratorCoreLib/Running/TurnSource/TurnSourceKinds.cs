namespace AIOrchestratorCoreLib.Running.TurnSource;

/// <summary>
/// WHAT KIND OF CHANNEL A TURN SOURCE IS — because "who is inbound here" stopped being a question about the
/// reader's role alone the day a solo got a second channel (sibling plan 2026-09-23, spec §5.4).
///
/// <para>
/// A solo's own entries in its OWNER channel must never start its turn: that is the record of its previous
/// turn, and waking on it is a loop with one member in it. A solo's entries in a SIBLING's outbox are the
/// opposite — another session speaking, and the only sibling-to-sibling signal there is (§5.2). The author
/// word is <c>solo</c> in both files, so the file's kind is what tells them apart.
/// </para>
/// </summary>
public enum TurnSourceKinds
{
    /// <summary>The conversation with the owner: an orchestration's owner channel, or the general supervisor's.</summary>
    Owner,

    /// <summary>A member's spoke — <c>imp-1</c>, <c>rev-2</c> — shared by that member and its supervisor.</summary>
    Spoke,

    /// <summary>
    /// Another solo's outbox (<c>sibling-outbox.md</c>): READ-ONLY for the reader, which answers a sibling in
    /// its own outbox, never in this file.
    /// </summary>
    Sibling,
}
