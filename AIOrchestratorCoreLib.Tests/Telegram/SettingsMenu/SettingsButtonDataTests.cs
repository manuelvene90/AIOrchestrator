using System.Text;
using AIOrchestratorCoreLib.Configuration.SettingsCatalog;
using AIOrchestratorCoreLib.Configuration.SettingsPresentation;
using AIOrchestratorCoreLib.Telegram.SettingsMenu;
using Xunit;

namespace AIOrchestratorCoreLib.Tests.Telegram.SettingsMenu;

/// <summary>
/// A STATELESS PAYLOAD, LIKE THE DIALS AND UNLIKE THE OPTIONS (CLAUDE.md decision 24). ModelEffortButton_Data
/// carries its whole answer so it survives a restart; the opt- registry does not and answers "expired".
/// A settings menu is a control the owner may come back to, so it must be the first kind.
/// </summary>
public class SettingsButtonDataTests
{
    static int Index_Of(string path)
    {
        for (var index = 0; index < SettingsCatalog.ALL.Count; index++)
        {
            if (SettingsCatalog.ALL[index].Path == path)
                return index;
        }

        throw new InvalidOperationException($"'{path}' is not a catalogue row — the test measures nothing");
    }

    [Fact]
    public void APayload_RoundTrips()
    {
        var index = Index_Of("phone.receipts");
        var data = SettingsButton_Data.Build(SettingsMenuViews.Setting, category: null, index, page: 3, SettingsMenuEdits.Set, "reactions");

        Assert.StartsWith(SettingsButton_Data.PREFIX, data);

        var parsed = SettingsButton_Data.Parse_OrNull(data);

        Assert.NotNull(parsed);
        Assert.Equal(SettingsMenuViews.Setting, parsed.Value.View);
        Assert.Equal(nameof(SettingCategories.Receipts), parsed.Value.CategoryWord);
        Assert.Equal(index, parsed.Value.Index);
        Assert.Equal(SettingsButton_Data.Compute_Check("phone.receipts"), parsed.Value.Check);
        Assert.Equal(3, parsed.Value.Page);
        Assert.Equal(SettingsMenuEdits.Set, parsed.Value.Edit);
        Assert.Equal("reactions", parsed.Value.Word);

        var resolved = SettingsButton_Data.Resolve_OrNull(parsed.Value);

        Assert.NotNull(resolved);
        Assert.Equal(SettingCategories.Receipts, resolved.Value.Category);
        Assert.Same(SettingsCatalog.ALL[index], resolved.Value.Definition_OrNull);
    }

    [Fact]
    public void EveryViewAndEditThatCanBeTapped_RoundTrips()
    {
        var index = Index_Of("pulse.buttons");

        foreach (var (view, category, settingIndex, edit, word) in new (SettingsMenuViews, SettingCategories?, int?, SettingsMenuEdits?, string?)[]
        {
            (SettingsMenuViews.Categories, null, null, null, null),
            (SettingsMenuViews.Category, SettingCategories.Pulse, null, null, null),
            (SettingsMenuViews.Setting, null, index, null, null),
            (SettingsMenuViews.Values, SettingCategories.Pulse, index, SettingsMenuEdits.Add, "tail sup"),
            (SettingsMenuViews.Values, null, index, SettingsMenuEdits.Remove, "progress"),
            (SettingsMenuViews.Confirm, null, index, SettingsMenuEdits.Reset, null),
            (SettingsMenuViews.Setting, null, index, SettingsMenuEdits.Reply, null),
            (SettingsMenuViews.Setting, null, index, SettingsMenuEdits.ApplyHeldReply, null),
        })
        {
            var data = SettingsButton_Data.Build(view, category, settingIndex, page: 1, edit, word);
            var resolved = SettingsButton_Data.Resolve_OrNull(SettingsButton_Data.Parse_OrNull(data) ?? throw new InvalidOperationException($"'{data}' did not parse"));

            Assert.NotNull(resolved);
            Assert.Equal(view, resolved.Value.View);
            Assert.Equal(settingIndex, resolved.Value.Index);
            Assert.Equal(edit, resolved.Value.Edit);
            Assert.Equal(word, resolved.Value.Word);
            Assert.Equal(1, resolved.Value.Page);
        }
    }

    /// <summary>A value is the last field, so a word carrying the separator rides inside it untouched.</summary>
    [Fact]
    public void AWordCarryingTheSeparator_RoundTrips()
    {
        var index = Index_Of("owner.name");
        var data = SettingsButton_Data.Build(SettingsMenuViews.Setting, null, index, 0, SettingsMenuEdits.Set, "a:b");

        Assert.Equal("a:b", SettingsButton_Data.Parse_OrNull(data)!.Value.Word);
    }

    /// <summary>
    /// THE CHECK IS FNV-1a, NOT string.GetHashCode (ruling P7). GetHashCode is randomised per PROCESS, so a
    /// menu message sent before an app restart would read as stale after it, every time. Pinned against the
    /// algorithm's own published test vectors, not against this implementation's output.
    /// </summary>
    [Fact]
    public void TheCheck_IsStableFnv1a_FourHexDigits()
    {
        Assert.Equal("9dc5", SettingsButton_Data.Compute_Check(string.Empty)); // 0x811c9dc5
        Assert.Equal("292c", SettingsButton_Data.Compute_Check("a"));          // 0xe40c292c
        Assert.Equal("f968", SettingsButton_Data.Compute_Check("foobar"));     // 0xbf9cf968 — the low 16 bits
    }

    [Fact]
    public void ThePrefix_CollidesWithNoOtherFamily()
    {
        string[] families = ["cmd:", "hold:", "go:", "close-yes-", "close-no-", "model:", "effort:", "opt-"];

        foreach (var family in families)
        {
            Assert.False(family.StartsWith(SettingsButton_Data.PREFIX, StringComparison.Ordinal), $"'{family}' starts with '{SettingsButton_Data.PREFIX}'");
            Assert.False(SettingsButton_Data.PREFIX.StartsWith(family, StringComparison.Ordinal), $"'{SettingsButton_Data.PREFIX}' starts with '{family}'");
        }
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("opt-17")]
    [InlineData("cmd:show:4242")]
    [InlineData("cmd:settings:4242")]
    [InlineData("hold:4242")]
    [InlineData("go:4242")]
    [InlineData("close-yes-abc")]
    [InlineData("model:crm-3:sup:fable")]
    [InlineData("effort:crm-3:imp:xhigh")]
    [InlineData("settings")]
    [InlineData("SET:h::::0")]
    public void SomeoneElsesPayload_ParsesToNull_SoItFallsThroughToTheNextHandler(string? callbackData)
    {
        Assert.Null(SettingsButton_Data.Parse_OrNull(callbackData));
        Assert.False(SettingsButton_Data.Is_Ours(callbackData));
    }

    /// <summary>
    /// OURS BUT UNREADABLE is still ours: it parses to null (nothing to act on) and <c>Is_Ours</c> says so, so
    /// the handler answers the stale line instead of letting a "set:" tap fall through unanswered.
    /// </summary>
    [Theory]
    [InlineData("set:")]
    [InlineData("set:s")]
    [InlineData("set:x::::0")]
    [InlineData("set:s:Kernel:1:abcd")]
    [InlineData("set:s:Kernel:1:abcd:-1")]
    [InlineData("set:s:Kernel:one:abcd:0")]
    [InlineData("set:s:Kernel:1:abcd:0:")]
    [InlineData("set:s:Kernel:1:abcd:0:*word")]
    [InlineData("set:s:Kernel:1:abcd:0:=")]
    [InlineData("set:s:Kernel:1:abcd:0:~extra")]
    public void AMalformedPayloadOfOurs_ParsesToNull_ButIsStillOurs(string callbackData)
    {
        Assert.Null(SettingsButton_Data.Parse_OrNull(callbackData));
        Assert.True(SettingsButton_Data.Is_Ours(callbackData));
    }

    /// <summary>
    /// THE LONGEST PAYLOAD THE CATALOGUE CAN PRODUCE FITS IN 64 BYTES, and this is computed from the
    /// catalogue rather than from a guess: the widest id, the widest page number, and the longest enum word
    /// of any Enum row. Telegram refuses an over-long callback_data at SEND time, on the phone, where nothing
    /// in this suite can see it.
    /// </summary>
    [Fact]
    public void TheLongestPayloadTheCatalogueCanProduce_FitsUnderTheCap()
    {
        var readings = SettingsSnapshot_Reader.Read_All(configTree: null, Presets_Loader.Load_Embedded(Presets_Loader.CLASSIC), Presets_Loader.CLASSIC, session: null);

        // Every word any row can carry: the Enum rows' words and every offer a renderer draws (on/off, "not set",
        // the picker lists' words) — the longest of them, whatever row it belongs to, is the worst case.
        var longestWord = SettingsCatalog.ALL.SelectMany(definition => definition.EnumValues)
            .Concat(readings.SelectMany(reading => reading.OfferedValues))
            .OrderByDescending(word => Encoding.UTF8.GetByteCount(word))
            .First();

        // The widest page: a category's own pages, or a Values view's pages over the longest offer list.
        var widestPage = Math.Max(
            SettingsRow_Builder.Build_Sections(readings).Max(section => section.Rows.Count),
            readings.Max(reading => reading.OfferedValues.Count));

        var longest = string.Empty;

        for (var index = 0; index < SettingsCatalog.ALL.Count; index++)
        {
            var data = SettingsButton_Data.Build(SettingsMenuViews.Confirm, null, index, widestPage, SettingsMenuEdits.Remove, longestWord);

            if (Encoding.UTF8.GetByteCount(data) > Encoding.UTF8.GetByteCount(longest))
                longest = data;
        }

        Assert.Contains(":" + SettingsButton_Data.Compute_Check(SettingsCatalog.ALL[SettingsButton_Data.Parse_OrNull(longest)!.Value.Index!.Value].Path) + ":", longest);
        Assert.True(Encoding.UTF8.GetByteCount(longest) <= SettingsButton_Data.TELEGRAM_CALLBACK_DATA_BYTE_LIMIT,
            $"'{longest}' is {Encoding.UTF8.GetByteCount(longest)} bytes");
    }

    [Fact]
    public void Build_Throws_RatherThanReturnAnOversizedPayload()
    {
        var index = Index_Of("owner.name");
        var word = new string('w', SettingsButton_Data.TELEGRAM_CALLBACK_DATA_BYTE_LIMIT);

        var exception = Assert.Throws<ArgumentException>(() => SettingsButton_Data.Build(SettingsMenuViews.Setting, null, index, 0, SettingsMenuEdits.Set, word));

        Assert.Contains(SettingsButton_Data.TELEGRAM_CALLBACK_DATA_BYTE_LIMIT.ToString(), exception.Message);
    }

    [Fact]
    public void Build_RefusesAnOrchestrationViewPayload_BecauseThatViewHasNoTaps()
    {
        Assert.Throws<ArgumentException>(() => SettingsButton_Data.Build(SettingsMenuViews.Orchestration, null, null, 0, null, null));
    }

    [Fact]
    public void Build_RefusesACategoryThatDisagreesWithTheIndex()
    {
        var index = Index_Of("phone.receipts");

        Assert.Throws<ArgumentException>(() => SettingsButton_Data.Build(SettingsMenuViews.Setting, SettingCategories.Kernel, index, 0, null, null));
    }

    /// <summary>
    /// A STALE ID FROM AN OLDER BUILD IS REFUSED, NOT ACTED ON (D8). The id is an index into
    /// SettingsCatalog.ALL, so a catalogue edit renumbers it and a menu message left open across an upgrade
    /// would otherwise toggle a different setting than the one whose label the owner read.
    /// </summary>
    [Fact]
    public void AnIdOutsideTheCatalogue_ParsesButIsAnsweredAsStale_AndChangesNothing()
    {
        var data = $"{SettingsButton_Data.PREFIX}s:Kernel:{SettingsCatalog.ALL.Count}:abcd:0:=on";

        var parsed = SettingsButton_Data.Parse_OrNull(data);

        Assert.NotNull(parsed);
        Assert.Null(SettingsButton_Data.Resolve_OrNull(parsed.Value));
        Assert.False(string.IsNullOrWhiteSpace(SettingsButton_Data.STALE_ANSWER));
        Assert.Contains("/settings", SettingsButton_Data.STALE_ANSWER);
    }

    [Fact]
    public void AnIdWhoseCategoryDisagreesWithThePayload_IsAnsweredAsStale()
    {
        var index = Index_Of("phone.receipts");
        var check = SettingsButton_Data.Compute_Check("phone.receipts");

        var parsed = SettingsButton_Data.Parse_OrNull($"{SettingsButton_Data.PREFIX}s:Kernel:{index}:{check}:0:=ticks");

        Assert.NotNull(parsed);
        Assert.Null(SettingsButton_Data.Resolve_OrNull(parsed.Value));
    }

    /// <summary>
    /// A ROW INSERTED MID-CATEGORY RENUMBERS WHILE THE CATEGORY STILL MATCHES (ruling P7) — plan 03 T14/T15
    /// insert rows inside Kernel and Phone. The check over the row's Path is what catches it.
    /// </summary>
    [Fact]
    public void AnIdWhoseCheckDisagreesWithItsPath_IsAnsweredAsStale_EvenWhenTheCategoryMatches()
    {
        var index = Index_Of("phone.receipts");
        var wrongCheck = SettingsButton_Data.Compute_Check("phone.appMessagesRing");

        var parsed = SettingsButton_Data.Parse_OrNull($"{SettingsButton_Data.PREFIX}s:Receipts:{index}:{wrongCheck}:0:=ticks");

        Assert.NotNull(parsed);
        Assert.Null(SettingsButton_Data.Resolve_OrNull(parsed.Value));
    }

    [Theory]
    [InlineData("set:c:Nowhere:::0")]
    [InlineData("set:c:3:::0")]
    [InlineData("set:c:phone:::0")]
    [InlineData("set:c::::0")]
    public void ACategoryWordThisBuildDoesNotKnow_IsAnsweredAsStale(string callbackData)
    {
        var parsed = SettingsButton_Data.Parse_OrNull(callbackData);

        Assert.NotNull(parsed);
        Assert.Null(SettingsButton_Data.Resolve_OrNull(parsed.Value));
    }

    /// <summary>
    /// A CHANGE WITH NO SETTING TO CHANGE IS NOT A TAP THIS BUILD MADE (fix round 1 of 7763a6b). No button of
    /// the Categories or Category view carries an edit, so one that does would otherwise resolve, read as a
    /// write through <c>Writes_OnTap</c>, and hand the handler a null definition to write to.
    /// </summary>
    [Theory]
    [InlineData("set:h::::0:=on")]
    [InlineData("set:h::::0:~")]
    [InlineData("set:h::::0:?")]
    [InlineData("set:c:Kernel:::0:=on")]
    [InlineData("set:c:Pulse:::1:+progress")]
    [InlineData("set:c:Phone:::0:!")]
    public void AnEditRidingOnAViewThatNamesNoSetting_IsAnsweredAsStale(string callbackData)
    {
        var parsed = SettingsButton_Data.Parse_OrNull(callbackData);

        Assert.NotNull(parsed);
        Assert.NotNull(parsed.Value.Edit);
        Assert.Null(SettingsButton_Data.Resolve_OrNull(parsed.Value));
    }

    /// <summary>A CONFIRM VIEW NEVER WRITES (ruling P6): the first tap of a Kernel edit only asks.</summary>
    [Fact]
    public void Writes_OnTap_IsFalseForAConfirmView_AndForTheReplyStep_AndForNavigation()
    {
        Assert.False(SettingsButton_Data.Writes_OnTap(SettingsMenuViews.Confirm, SettingsMenuEdits.Set));
        Assert.False(SettingsButton_Data.Writes_OnTap(SettingsMenuViews.Confirm, SettingsMenuEdits.Reset));
        Assert.False(SettingsButton_Data.Writes_OnTap(SettingsMenuViews.Setting, SettingsMenuEdits.Reply));
        Assert.False(SettingsButton_Data.Writes_OnTap(SettingsMenuViews.Setting, null));

        Assert.True(SettingsButton_Data.Writes_OnTap(SettingsMenuViews.Setting, SettingsMenuEdits.Set));
        Assert.True(SettingsButton_Data.Writes_OnTap(SettingsMenuViews.Setting, SettingsMenuEdits.Reset));
        Assert.True(SettingsButton_Data.Writes_OnTap(SettingsMenuViews.Setting, SettingsMenuEdits.ApplyHeldReply));
        Assert.True(SettingsButton_Data.Writes_OnTap(SettingsMenuViews.Values, SettingsMenuEdits.Add));
        Assert.True(SettingsButton_Data.Writes_OnTap(SettingsMenuViews.Values, SettingsMenuEdits.Remove));
    }
}
