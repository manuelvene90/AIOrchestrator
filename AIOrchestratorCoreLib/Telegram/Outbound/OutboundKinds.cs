namespace AIOrchestratorCoreLib.Telegram.Outbound;

/// <summary>
/// THE ONE DISTINCTION THE DESIGN TURNS ON.
///
/// <para>
/// A JOB must be delivered, and in order: it is a thing that HAPPENED, and the record of it is the
/// channel file, which is why losing one is not recoverable by repainting. A SLOT is the current
/// VALUE of something — a status line, a topic name, the dashboard — so a newer one makes an older
/// one WORTHLESS rather than late. Publishing a slot therefore overwrites any intent still waiting
/// under the same key, which is coalescing expressed as a data structure rather than as a rule
/// somebody has to remember.
/// </para>
/// </summary>
public enum OutboundKinds
{
    Job,
    Slot,
}
