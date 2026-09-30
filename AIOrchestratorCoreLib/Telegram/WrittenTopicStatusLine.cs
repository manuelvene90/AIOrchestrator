namespace AIOrchestratorCoreLib.Telegram;

/// <summary>
/// WHAT A TOPIC'S STATUS LINE LAST SAID ON THE PHONE: its text, and the whole rendering — text and bar
/// labels, <see cref="TopicStatusLine_RenderKey"/> — that text made with its bar.
///
/// <para>
/// BOTH, IN ONE VALUE, because they answer two different questions and one of them was being asked of
/// the wrong one. The planner decides on the TEXT (is there anything new to say) and on the RENDERING
/// (did the bar change under the same text). From the fork's `2143db8` until plan 03 Task 17
/// (2026-09-23) the engine remembered only the render key and handed it to the planner as the text: the
/// two never matched, so PULSE was edited on every tick (plan 03 report §5.3).
/// </para>
/// <para>
/// A THIRD HALF, <c>SeenAtBottomKey</c>, lived here from ruling R27 (2026-09-24) to 2026-09-30: what the
/// owner last saw at the bottom, for the "buried AND changed" repost gate. The owner's rule of 2026-09-30
/// moves a buried line whether or not it changed, so the memory went with the gate.
/// </para>
/// <para>
/// ONE VALUE RATHER THAN SEVERAL MAPS, so they cannot drift apart: `Forget_StatusLineMessage` removes one
/// entry and every half goes with it, and a success stores all of them at once or none.
/// </para>
/// </summary>
public sealed class WrittenTopicStatusLine(string text, string renderKey)
{
    public string Text { get; } = text;

    public string RenderKey { get; } = renderKey;
}
