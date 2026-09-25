using System.Text.RegularExpressions;
using AIOrchestratorCoreLib.Configuration.SettingsCatalog;
using AIOrchestratorCoreLib.Configuration.SettingsPresentation;
using AIOrchestratorCoreLib.Web;
using Xunit;

using Catalog = global::AIOrchestratorCoreLib.Configuration.SettingsCatalog.SettingsCatalog;

namespace AIOrchestratorCoreLib.Tests.Web;

/// <summary>
/// THE PAGE CONTAINS NO SETTING. Spec §8.3's promise is that "a new catalogue entry appears in all three
/// renderers with no UI code", and for this renderer that promise is exactly one testable property: the
/// HTML must mention no catalogue path, no setting label and no category name, because it renders
/// everything from GET /settings. This is the only thing about this file a test can honestly say, and it
/// happens to be the important thing.
///
/// <para>
/// NOTHING HERE IS A LITERAL LIST OF SETTINGS: every forbidden word is read off <see cref="Catalog.ALL"/> and
/// <see cref="SettingsRow_Builder.Describe_Title"/>, so a row added on a sibling branch is guarded the day it
/// lands, and a page that grew a hard-coded mention of it goes red here rather than drifting in a browser.
/// </para>
/// </summary>
public class SettingsPageAssetTests
{
    static readonly string PAGE = SettingsPage_Reader.Read_Html();

    [Fact]
    public void ThePage_IsEmbedded_AndIsNotEmpty()
    {
        Assert.False(string.IsNullOrWhiteSpace(PAGE));
        Assert.Contains("<!doctype html>", PAGE, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("<script>", PAGE, StringComparison.Ordinal);
        Assert.Contains("<style>", PAGE, StringComparison.Ordinal);
    }

    /// <summary>
    /// Paths are matched ORDINALLY (a path is an identifier, and <c>repos</c> inside any longer word would
    /// still be the page knowing a setting's spelling); labels and category names case-insensitively, the
    /// category names as whole words — "Kit" and "Owner" are ordinary English, and a page that never says
    /// "owner" is the price of knowing it does not say it about a category.
    /// </summary>
    [Fact]
    public void ThePage_NamesNoCataloguePath_AndNoSettingLabel()
    {
        List<string> mentions = [];

        foreach (var definition in Catalog.ALL)
        {
            if (PAGE.Contains(definition.Path, StringComparison.Ordinal))
                mentions.Add($"path '{definition.Path}'");

            if (definition.LegacyPath_OrNull != null && PAGE.Contains(definition.LegacyPath_OrNull, StringComparison.Ordinal))
                mentions.Add($"legacy path '{definition.LegacyPath_OrNull}'");

            if (PAGE.Contains(definition.Label, StringComparison.OrdinalIgnoreCase))
                mentions.Add($"label '{definition.Label}'");
        }

        foreach (var category in Enum.GetValues<SettingCategories>())
        {
            foreach (var word in new[] { category.ToString(), SettingsRow_Builder.Describe_Title(category) }.Distinct())
            {
                if (Regex.IsMatch(PAGE, $@"\b{Regex.Escape(word)}\b", RegexOptions.IgnoreCase))
                    mentions.Add($"category '{word}'");
            }
        }

        Assert.True(mentions.Count == 0, "The page names settings it should read from GET /settings: " + string.Join(", ", mentions));
    }

    /// <summary>
    /// The routes and the token header are read off the handler's own constants, so a renamed route breaks this
    /// test rather than a page nobody opened.
    /// </summary>
    [Fact]
    public void ThePage_CallsGetAndPutSettings()
    {
        Assert.Contains($"'{SettingsRequest_Handler.SETTINGS_PATH}'", PAGE, StringComparison.Ordinal);
        Assert.Contains("'PUT'", PAGE, StringComparison.Ordinal);
        Assert.Contains("'DELETE'", PAGE, StringComparison.Ordinal);
        Assert.Contains($"'{SettingsRequest_Handler.RESET_QUERY_KEY}'", PAGE, StringComparison.Ordinal);

        // The header NAME comes from the GET's "editing.tokenHeader", never typed into the script.
        Assert.DoesNotContain(SettingsRequest_Handler.TOKEN_HEADER, PAGE, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("tokenHeader", PAGE, StringComparison.Ordinal);
    }

    /// <summary>
    /// Served from loopback to a browser that may have no internet — the headless VPS through an SSH tunnel is
    /// the case this page exists for — so one CDN reference would make it fail exactly where it is needed.
    /// </summary>
    [Fact]
    public void ThePage_LoadsNothingFromTheInternet()
    {
        Assert.DoesNotMatch(new Regex(@"https?://", RegexOptions.IgnoreCase), PAGE);
        Assert.DoesNotMatch(new Regex(@"(src|href)\s*=\s*[""']?//", RegexOptions.IgnoreCase), PAGE);
        Assert.DoesNotMatch(new Regex(@"<script[^>]*\bsrc\s*=", RegexOptions.IgnoreCase), PAGE);
        Assert.DoesNotMatch(new Regex(@"<link\b", RegexOptions.IgnoreCase), PAGE);
        Assert.DoesNotMatch(new Regex(@"@import|url\s*\(", RegexOptions.IgnoreCase), PAGE);
    }
}
