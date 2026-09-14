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
    const string ENGINE_RELATIVE_PATH = "AIOrchestratorCoreLib/Bridge/BridgeEngine/BridgeEngineModel.cs";

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

    /// <summary>
    /// 75abab7 on the fork measured live that deleting the carrier message deletes the bar with it —
    /// and master's own bridge deletes the carrier immediately after sending it, so on that evidence
    /// master's bar never actually worked. The spec's ruling (§7.6) makes the keyboard a per-user
    /// setting in a LATER plan, defaulting off, with the carrier kept once it is wired. Until then,
    /// <see cref="ReplyKeyboard_Markup"/> stays compiled and pinned but UNCALLED — this fails the
    /// moment any production code installs it.
    /// </summary>
    [Fact]
    public void NothingInstallsTheKeyboardYet_BecauseTheCarrierDeleteRemovesTheBar()
    {
        var engine = Read_EngineSource();

        Assert.DoesNotContain("Install_CommandKeyboard", engine);
    }

    static string? Text_At(JsonArray keyboard, int row, int column)
    {
        return keyboard[row]?[column]?["text"]?.GetValue<string>();
    }

    /// <summary>
    /// THE GUARD ON THE GUARD. Returns the source or FAILS — a harness that cannot find what it tests
    /// must refuse to run rather than certify the absence of the thing it never read.
    /// </summary>
    static string Read_EngineSource()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory != null)
        {
            var candidate = Path.Combine(directory.FullName, ENGINE_RELATIVE_PATH.Replace('/', Path.DirectorySeparatorChar));

            if (File.Exists(candidate))
                return File.ReadAllText(candidate);

            directory = directory.Parent;
        }

        throw new Exception(
            $"Could not locate '{ENGINE_RELATIVE_PATH}' walking up from '{AppContext.BaseDirectory}'. "
            + "This harness reads the engine's SOURCE, so a missing file means it measured nothing — "
            + "failing rather than reporting the keyboard as never installed.");
    }
}
