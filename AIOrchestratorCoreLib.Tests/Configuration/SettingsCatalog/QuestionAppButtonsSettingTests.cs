using System.Text.Json.Nodes;
using AIOrchestratorCoreLib.Bridge;
using AIOrchestratorCoreLib.Configuration.PhoneSettings;
using AIOrchestratorCoreLib.Configuration.SettingsCatalog;
using AIOrchestratorCoreLib.Configuration.SettingsCatalog.SettingDefinition;
using AIOrchestratorCoreLib.Configuration.SettingsPresentation;
using Xunit;
using Catalog = global::AIOrchestratorCoreLib.Configuration.SettingsCatalog.SettingsCatalog;

namespace AIOrchestratorCoreLib.Tests.Configuration.SettingsCatalog;

/// <summary>
/// THE BUTTONS UNDER A QUESTION ARE A SETTING (owner, 2026-09-24, ai-orchestrator-29 entry [123]: *"my brother
/// has transformed my button under questions 'Explain in more details' into 'Let's talk' but I more often use
/// the explain in more details feature. Can we have a setting that lets us decide what buttons we want under
/// the questions? I'd add all 2 or just one of the two."*).
///
/// <para>
/// ONE ORDERED LIST, the same row kind as <c>pulse.fields</c>, so the three renderers draw it with no code of
/// their own (plan 04's gate walks it). Shipped value = today's behaviour, Let's talk alone (ruling R14).
/// </para>
/// </summary>
public class QuestionAppButtonsSettingTests
{
    const string PATH = "questions.appButtons";

    static ISettingDefinition Row()
    {
        return Catalog.Find_OrNull(PATH)
            ?? throw new Exception($"'{PATH}' is not in the catalogue — REFUSING to pass about a row this test never found.");
    }

    static JsonArray List(params string[] words) => new([.. words.Select(word => (JsonNode?)JsonValue.Create(word))]);

    [Fact]
    public void TheRow_IsAnOrderedList_OnThePhonePage_ShippingTodaysLetsTalkAlone()
    {
        var row = Row();

        Assert.Equal(PhoneSettings_Json.QUESTION_APP_BUTTONS_PATH, row.Path);
        Assert.Equal(SettingKinds.StringList, row.Kind);
        Assert.Equal(SettingRenderers.OrderedList, row.Renderer);
        Assert.Equal(SettingCategories.Phone, row.Category);
        Assert.Equal(SettingScopes.Machine, row.Scope);
        Assert.Equal(RestartKinds.None, row.Restart);
        Assert.Equal("[\"talk\"]", row.Default_OrNull!.ToJsonString());
    }

    [Theory]
    [InlineData("[]")]
    [InlineData("[\"talk\"]")]
    [InlineData("[\"explain\"]")]
    [InlineData("[\"explain\",\"talk\"]")]
    [InlineData("[\"talk\",\"explain\"]")]
    public void TheRow_AcceptsEitherButton_BothInEitherOrder_AndNone(string listJson)
    {
        Assert.Null(Row().Validate_OrNull(JsonNode.Parse(listJson)));
    }

    [Fact]
    public void TheRow_RefusesAWordThatIsNotAnAppButton_AndARepeat()
    {
        var unknown = Row().Validate_OrNull(List("explain", "chat"));
        var repeat = Row().Validate_OrNull(List("talk", "talk"));

        Assert.NotNull(unknown);
        Assert.Contains("'chat'", unknown, StringComparison.Ordinal);
        Assert.Contains("explain, talk", unknown, StringComparison.Ordinal);

        Assert.NotNull(repeat);
        Assert.Contains("more than once", repeat, StringComparison.Ordinal);

        // Case-sensitive, like the words the engine matches.
        Assert.NotNull(Row().Validate_OrNull(List("Talk")));
    }

    /// <summary>Read on a phone: it names both buttons as the owner sees them, what each does, and what an empty list means.</summary>
    [Fact]
    public void TheDescription_SaysWhatEachButtonDoes_AndWhatNoneMeans()
    {
        var description = Row().Description;

        Assert.Contains(OwnerPush_Policy.EXPLAIN_LABEL, description, StringComparison.Ordinal);
        Assert.Contains(OwnerPush_Policy.TALK_LABEL, description, StringComparison.Ordinal);
        Assert.Contains("'explain'", description, StringComparison.Ordinal);
        Assert.Contains("'talk'", description, StringComparison.Ordinal);
        Assert.Contains("empty", description, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("order", description, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>A picker, like pulse.fields: every renderer offers exactly the two words the row accepts (ruling P5).</summary>
    [Fact]
    public void TheReading_OffersBothButtons_AndNothingElse()
    {
        var readings = SettingsSnapshot_Reader.Read_All(
            configTree: null, Presets_Loader.Load_Embedded(Presets_Loader.CLASSIC), Presets_Loader.CLASSIC, session: null);

        var reading = readings.Single(candidate => candidate.Definition.Path == PATH);

        Assert.Equal([QuestionAppButton_Names.EXPLAIN, QuestionAppButton_Names.TALK], reading.OfferedValues);
        Assert.Equal(QuestionAppButton_Names.ALL, reading.OfferedValues);
    }

    [Fact]
    public void ThePhoneBlock_ResolvesTheList_AndATypoFallsToTheLayerBelow()
    {
        Assert.Equal(["talk"], PhoneSettings_Json.Parse(configRoot: null, presetTree: null).QuestionAppButtons);

        var stated = (JsonObject)JsonNode.Parse("{\"questions\":{\"appButtons\":[\"talk\",\"explain\"]}}")!;
        Assert.Equal(["talk", "explain"], PhoneSettings_Json.Parse(stated, presetTree: null).QuestionAppButtons);

        var none = (JsonObject)JsonNode.Parse("{\"questions\":{\"appButtons\":[]}}")!;
        Assert.Empty(PhoneSettings_Json.Parse(none, presetTree: null).QuestionAppButtons);

        // A misspelled element refuses the whole list at that layer — the owner gets the shipped bar, never a hole.
        var typo = (JsonObject)JsonNode.Parse("{\"questions\":{\"appButtons\":[\"explian\"]}}")!;
        Assert.Equal(["talk"], PhoneSettings_Json.Parse(typo, presetTree: null).QuestionAppButtons);
    }
}
