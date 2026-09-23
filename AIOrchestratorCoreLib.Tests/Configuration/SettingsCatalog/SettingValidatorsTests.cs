using System.Text.Json.Nodes;
using AIOrchestratorCoreLib.Configuration.SettingsCatalog;
using AIOrchestratorCoreLib.Telegram;
using Xunit;
using Catalog = global::AIOrchestratorCoreLib.Configuration.SettingsCatalog.SettingsCatalog;

namespace AIOrchestratorCoreLib.Tests.Configuration.SettingsCatalog;

/// <summary>
/// THE TWO LIST VALIDATORS. A refusal here is what turns a hand-edited typo into "that one key falls
/// to the layer below" at the resolver — without it the typo was accepted as written and reached a
/// builder. `web.listen`'s validator has its own file, <c>ListenAddressValidatorTests</c>, now that it
/// is a real check rather than a registered name.
/// </summary>
public class SettingValidatorsTests
{
    [Fact]
    public void PulseFields_RefusesAWordThatIsNotAField()
    {
        var refusal = SettingValidators.Validate_OrNull(SettingValidators.PULSE_FIELDS, List("supervisor", "suprvisor"));

        Assert.NotNull(refusal);
        Assert.Contains("suprvisor", refusal, StringComparison.Ordinal);
    }

    [Fact]
    public void PulseFields_RefusesARepeat()
    {
        var refusal = SettingValidators.Validate_OrNull(SettingValidators.PULSE_FIELDS, List("updated", "updated"));

        Assert.NotNull(refusal);
        Assert.Contains("updated", refusal, StringComparison.Ordinal);
    }

    /// <summary>And the empty list — a pulse with no fields is legal, the owner's to choose.</summary>
    [Fact]
    public void PulseFields_AcceptsTheShippedDefault()
    {
        Assert.Null(SettingValidators.Validate_OrNull(SettingValidators.PULSE_FIELDS, Catalog.Find_OrNull("pulse.fields")!.Default_OrNull));
        Assert.Null(SettingValidators.Validate_OrNull(SettingValidators.PULSE_FIELDS, List([.. PulseField_Names.ALL])));
        Assert.Null(SettingValidators.Validate_OrNull(SettingValidators.PULSE_FIELDS, List()));
    }

    /// <summary>
    /// `progress` IS A FIELD (owner, 2026-09-23) — spelled as the literal an owner types, not through the
    /// constant, so a rename of the constant that forgot `ALL` cannot pass this by agreeing with itself.
    /// Classic's own list leads with it, so a refusal here would cost every untouched machine its PULSE.
    /// </summary>
    [Fact]
    public void PulseFields_AcceptsProgress_AndClassicsListLeadingWithIt()
    {
        Assert.Null(SettingValidators.Validate_OrNull(SettingValidators.PULSE_FIELDS, List("progress")));
        Assert.Null(SettingValidators.Validate_OrNull(
            SettingValidators.PULSE_FIELDS, List("progress", "supervisor", "members", "modelEffort", "updated")));
    }

    /// <summary>
    /// "tail sup" is a verb WITH ITS TARGET — a tap carries no text, so the target rides inside the
    /// verb — and <c>BotCommandMenu.ALL</c> holds only the bare "tail". Exact membership would refuse
    /// the catalogue's own default; only the first token is a verb.
    /// </summary>
    [Fact]
    public void BotCommands_ChecksOnlyTheFirstToken()
    {
        Assert.Null(SettingValidators.Validate_OrNull(SettingValidators.BOT_COMMANDS, List("tail sup")));

        var refusal = SettingValidators.Validate_OrNull(SettingValidators.BOT_COMMANDS, List("tale sup"));

        Assert.NotNull(refusal);
        Assert.Contains("tale", refusal, StringComparison.Ordinal);
    }

    [Fact]
    public void BotCommands_AcceptsBothShippedDefaults()
    {
        Assert.Null(SettingValidators.Validate_OrNull(SettingValidators.BOT_COMMANDS, List([.. TopicCommandButtons.Commands])));
        Assert.Null(SettingValidators.Validate_OrNull(SettingValidators.BOT_COMMANDS, List([.. TopicCommandButtons.GeneralCommands])));
    }

    /// <summary>Classic's <c>general.buttons</c> — General with no bar at all.</summary>
    [Fact]
    public void BotCommands_AcceptsTheEmptyList()
    {
        Assert.Null(SettingValidators.Validate_OrNull(SettingValidators.BOT_COMMANDS, List()));
    }

    /// <summary>The repeat is of the VERB: two targets of one verb are still one verb twice on a bar.</summary>
    [Fact]
    public void BotCommands_RefusesARepeatedVerb()
    {
        var refusal = SettingValidators.Validate_OrNull(SettingValidators.BOT_COMMANDS, List("tail sup", "tail 1"));

        Assert.NotNull(refusal);
        Assert.Contains("tail", refusal, StringComparison.Ordinal);
    }

    static JsonArray List(params string[] words)
    {
        var array = new JsonArray();

        foreach (var word in words)
            array.Add(JsonValue.Create(word));

        return array;
    }
}
