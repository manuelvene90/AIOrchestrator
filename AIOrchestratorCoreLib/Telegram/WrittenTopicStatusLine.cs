namespace AIOrchestratorCoreLib.Telegram;

/// <summary>
/// WHAT A TOPIC'S STATUS LINE LAST SAID ON THE PHONE: its text, the whole rendering — text and bar
/// labels, <see cref="TopicStatusLine_RenderKey"/> — that text made with its bar, and what the owner saw
/// when the line was last at the bottom.
///
/// <para>
/// THE FIRST TWO, IN ONE VALUE, because they answer two different questions and one of them was being
/// asked of the wrong one. The planner decides on the TEXT (is there anything new to say, is the
/// difference only the heartbeat) and on the RENDERING (did the bar change under the same text). From
/// the fork's `2143db8` until plan 03 Task 17 (2026-09-23) the engine remembered only the render key and
/// handed it to the planner as the text: the two never matched, so PULSE was edited on every tick and
/// reposted after every exchange with nothing new in it (plan 03 report §5.3).
/// </para>
/// <para>
/// THE THIRD, <see cref="SeenAtBottomKey"/> (ruling R27, 2026-09-24): "buried AND changed" means changed
/// since the owner last saw the line at the bottom. An edit made while the line is buried is not seen,
/// so it must not move this memory — otherwise an answer that both buries PULSE and changes it is edited
/// in place during the quiet window and then left buried for good. The planner decides the value
/// (<c>TopicStatusPlan.SeenAtBottomKey</c>); the engine only stores it.
/// </para>
/// <para>
/// ONE VALUE RATHER THAN SEVERAL MAPS, so they cannot drift apart: `Forget_StatusLineMessage` removes one
/// entry and every half goes with it, and a success stores all of them at once or none.
/// </para>
/// </summary>
public sealed class WrittenTopicStatusLine(string text, string renderKey, string? seenAtBottomKey)
{
    public string Text { get; } = text;

    public string RenderKey { get; } = renderKey;

    /// <summary>Null only when nothing is known yet; the planner then compares with the last write.</summary>
    public string? SeenAtBottomKey { get; } = seenAtBottomKey;
}
