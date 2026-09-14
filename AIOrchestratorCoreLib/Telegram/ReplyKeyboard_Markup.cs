using System.Text.Json.Nodes;

namespace AIOrchestratorCoreLib.Telegram;

/// <summary>
/// The `reply_markup` for the command bar above the owner's input box.
///
/// It used to ask for `is_persistent`, on the owner's earlier request for a PERMANENT bar. That
/// flag tells Telegram to show the bar whenever the regular keyboard is hidden — which is exactly
/// what pressing back on the phone does — and it disables the little keyboard icon that would
/// otherwise collapse it. The owner, 2026-09-09: "the selection of main commands appears in the
/// place of the keyboard, which is annoying because further backward does nothing and I need to
/// make the keyboard disappear to have full chat view." So the bar is COLLAPSIBLE now: still
/// installed at every launch, one tap on the icon to reopen it, and a second back press hides it.
/// Pinned by a test so the flag does not creep back in the name of permanence.
/// </summary>
public static class ReplyKeyboard_Markup
{
    public static JsonObject Build(IReadOnlyList<IReadOnlyList<string>> keyboardRows)
    {
        var rows = new JsonArray();

        foreach (var row in keyboardRows)
        {
            var buttons = new JsonArray();

            foreach (var label in row)
                buttons.Add(new JsonObject { ["text"] = label });

            rows.Add(buttons);
        }

        return new JsonObject
        {
            ["keyboard"] = rows,
            ["is_persistent"] = false,
            // Without this the bar renders at full standard-keyboard height — four buttons in a
            // half-screen slab, sitting on top of the conversation the owner is reading.
            ["resize_keyboard"] = true,
            ["selective"] = false,
        };
    }
}
