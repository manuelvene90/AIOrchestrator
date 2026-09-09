using System.Text.Json.Nodes;
using AIOrchestratorCoreLib.Telegram;
using Xunit;

namespace AIOrchestratorCoreLib.Tests.Telegram;

/// <summary>
/// The owner, 2026-09-09: pressing back on the phone made "the selection of main commands appear in
/// the place of the keyboard, which is annoying because further backward does nothing and I need to
/// make the keyboard disappear to have full chat view." That is what `is_persistent` asks Telegram
/// for — show the bar whenever the regular keyboard is hidden — and it overrode the little
/// keyboard icon that would otherwise collapse it. The bar is now collapsible: still installed,
/// still one tap to reopen, but a second back press hides it.
/// </summary>
public class ReplyKeyboardMarkupTests
{
    [Fact]
    public void TheRows_BecomeTelegramKeyboardButtons_InOrder()
    {
        var markup = ReplyKeyboard_Markup.Build([["/screen", "/show"], ["/merge"]]);

        var keyboard = Assert.IsType<JsonArray>(markup["keyboard"]);

        Assert.Equal(2, keyboard.Count);
        Assert.Equal("/screen", Text_At(keyboard, 0, 0));
        Assert.Equal("/show", Text_At(keyboard, 0, 1));
        Assert.Equal("/merge", Text_At(keyboard, 1, 0));
    }

    [Fact]
    public void TheBar_IsCollapsible_NotPersistent()
    {
        var markup = ReplyKeyboard_Markup.Build([["/screen"]]);

        Assert.False(markup["is_persistent"]?.GetValue<bool>() ?? false, "is_persistent pins the bar over the chat whenever the regular keyboard hides");
    }

    [Fact]
    public void TheBar_IsResizedToItsButtons_AndShownToEveryone()
    {
        var markup = ReplyKeyboard_Markup.Build([["/screen"]]);

        Assert.True(markup["resize_keyboard"]?.GetValue<bool>());
        Assert.False(markup["selective"]?.GetValue<bool>());
    }

    static string? Text_At(JsonArray keyboard, int row, int column)
    {
        return keyboard[row]?[column]?["text"]?.GetValue<string>();
    }
}
