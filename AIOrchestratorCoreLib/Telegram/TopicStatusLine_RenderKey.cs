using System.Globalization;
using System.Text;

namespace AIOrchestratorCoreLib.Telegram;

/// <summary>
/// WHAT THE STATUS LINE CURRENTLY LOOKS LIKE, as one comparable string — brief D.
///
/// <para>
/// The line is repainted only when something about it has changed, and "something" used to mean
/// its TEXT. That was true for as long as the buttons under it were fixed furniture. Brief D put a
/// toggle there whose label carries the held count, so a hold that catches three messages changes
/// the rendering and not one character of the text: the comparison says "nothing moved", no edit
/// is sent, and the count the owner is meant to read never leaves this process.
/// </para>
/// <para>
/// The second half is worse, and is why this is a KEY rather than a special case for the toggle: a
/// bar that ends up wrong for ANY reason would never be repainted either, because a quiet
/// orchestration's text does not move for hours. Comparing the whole rendering makes the repaint
/// follow the thing the owner actually sees.
/// </para>
/// <para>
/// LABELS ONLY, not callback data: the data carries a thread id that is stable for the life of the
/// topic, so including it would add nothing, and a key that changed for reasons the owner cannot
/// see would cost an edit per tick.
/// </para>
/// <para>
/// LENGTH-PREFIXED rather than delimited, so no separator has to be a character a label is assumed
/// never to contain — the assumption that quietly stops holding the day someone puts a bullet in a
/// button.
/// </para>
/// </summary>
public static class TopicStatusLine_RenderKey
{
    public static string Build(string text, IReadOnlyList<IReadOnlyList<(string Data, string Label)>> buttonRows)
    {
        var key = new StringBuilder(text.Length + 32);

        key.Append(text.Length.ToString(CultureInfo.InvariantCulture)).Append(':').Append(text);

        foreach (var row in buttonRows)
        {
            foreach (var button in row)
                key.Append('|').Append(button.Label.Length.ToString(CultureInfo.InvariantCulture)).Append(':').Append(button.Label);

            key.Append("|/");
        }

        return key.ToString();
    }
}
