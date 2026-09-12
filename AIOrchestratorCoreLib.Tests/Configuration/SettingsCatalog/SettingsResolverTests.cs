using System.Text.Json.Nodes;
using AIOrchestratorCoreLib.Configuration.SettingsCatalog;
using AIOrchestratorCoreLib.Sessions.OrchestrationMember;
using AIOrchestratorCoreLib.Sessions.OrchestrationSession;
using Xunit;

using Catalog = global::AIOrchestratorCoreLib.Configuration.SettingsCatalog.SettingsCatalog;

namespace AIOrchestratorCoreLib.Tests.Configuration.SettingsCatalog;

/// <summary>
/// PRECEDENCE IS TESTED WITHOUT TOUCHING DISK, which is the whole reason the layers are parameters
/// (spec §6.2, following HostOptions_Factory.Create_FromArguments, whose environment reader is a
/// function for the same reason). One [Fact] per rung, so a broken rung names itself.
/// </summary>
public class SettingsResolverTests
{
    static JsonObject Tree(string json) => (JsonObject)JsonNode.Parse(json)!;

    [Fact]
    public void WithEveryLayerSilent_TheShippedDefaultWins_AndSaysSo()
    {
        var definition = Catalog.Find_OrNull("phone.receipts")!;

        var (value, origin) = Settings_Resolver.Resolve(definition, presetTree: null, configTree: null, session: null);

        Assert.Equal("ticks", value!.GetValue<string>());
        Assert.Equal(SettingOrigins.ShippedDefault, origin);
    }

    [Fact]
    public void ThePreset_BeatsTheShippedDefault()
    {
        var definition = Catalog.Find_OrNull("phone.receipts")!;

        var (value, origin) = Settings_Resolver.Resolve(definition, Tree("""{"phone.receipts":"reactions"}"""), configTree: null, session: null);

        Assert.Equal("reactions", value!.GetValue<string>());
        Assert.Equal(SettingOrigins.Preset, origin);
    }

    [Fact]
    public void TheConfigFile_BeatsThePreset()
    {
        var definition = Catalog.Find_OrNull("phone.receipts")!;

        var (value, origin) = Settings_Resolver.Resolve(
            definition,
            Tree("""{"phone.receipts":"reactions"}"""),
            Tree("""{"phone":{"receipts":"ticks"}}"""),
            session: null);

        Assert.Equal("ticks", value!.GetValue<string>());
        Assert.Equal(SettingOrigins.ConfigFile, origin);
    }

    /// <summary>
    /// THE SESSION IS THE TOP RUNG AND ONLY FOR ORCHESTRATION-SCOPE KEYS. A Machine-scope key read
    /// with a session present must not go looking in session.json — there is nothing there, and a
    /// resolver that looked would make every renderer's origin label unreliable.
    /// </summary>
    [Fact]
    public void TheSession_BeatsTheConfigFile_ForAnOrchestrationScopedKey()
    {
        var definition = Catalog.Find_OrNull("models.supervisor")!;
        var session = new TestSession { SupervisorModelOverride = "haiku" };

        var (value, origin) = Settings_Resolver.Resolve(definition, presetTree: null, Tree("""{"supervisorModel":"opus"}"""), session);

        Assert.Equal("haiku", value!.GetValue<string>());
        Assert.Equal(SettingOrigins.Session, origin);
    }

    [Fact]
    public void AMachineScopedKey_IgnoresTheSessionEntirely()
    {
        var definition = Catalog.Find_OrNull("phone.receipts")!;

        var (_, origin) = Settings_Resolver.Resolve(definition, presetTree: null, configTree: null, new TestSession { SupervisorModelOverride = "haiku" });

        Assert.Equal(SettingOrigins.ShippedDefault, origin);
    }

    /// <summary>
    /// ABSENT IS ABSENT AND BLANK IS ABSENT (spec §6.2). Proven on this codebase 2026-09-10:
    /// {"reviewerModel":""} handed the reviewer the empty string and the spawn carried NO --model
    /// flag at all — the CLI's own default, neither the owner's answer nor the app's, with no line
    /// anywhere saying so. A cleared field is the owner saying nothing.
    /// </summary>
    [Fact]
    public void ABlankValueInALayer_IsAbsent_AndTheLayerBelowAnswers()
    {
        var definition = Catalog.Find_OrNull("models.supervisor")!;

        var (value, origin) = Settings_Resolver.Resolve(
            definition,
            Tree("""{"models.supervisor":"claude-fable-5-1"}"""),
            Tree("""{"supervisorModel":"   "}"""),
            session: null);

        Assert.Equal("claude-fable-5-1", value!.GetValue<string>());
        Assert.Equal(SettingOrigins.Preset, origin);
    }

    /// <summary>
    /// THE OLD SPELLING IS READ IN THE SAME LAYER, after the new one. Every config.json on both
    /// machines is written in the old spelling, and a re-homed path that stopped reading it would
    /// silently reset the owner's value to the shipped default.
    /// </summary>
    [Fact]
    public void TheLegacySpellingInTheConfigFile_IsReadWhenTheNewOneIsAbsent()
    {
        var definition = Catalog.Find_OrNull("phone.foldLongEntriesAbove")!;

        var (value, origin) = Settings_Resolver.Resolve(definition, presetTree: null, Tree("""{"telegram":{"foldLongEntriesAbove":1500}}"""), session: null);

        Assert.Equal(1500, value!.GetValue<int>());
        Assert.Equal(SettingOrigins.ConfigFile, origin);
    }

    [Fact]
    public void TheNewSpelling_BeatsTheLegacyOne_InTheSameLayer()
    {
        var definition = Catalog.Find_OrNull("phone.foldLongEntriesAbove")!;

        var (value, _) = Settings_Resolver.Resolve(
            definition,
            presetTree: null,
            Tree("""{"phone":{"foldLongEntriesAbove":700},"telegram":{"foldLongEntriesAbove":1500}}"""),
            session: null);

        Assert.Equal(700, value!.GetValue<int>());
    }

    /// <summary>
    /// A VALUE THE DEFINITION REFUSES FALLS THROUGH TO THE LAYER BELOW rather than being resolved.
    /// The same direction the loader's tolerant readers already take (2026-09-10): a typo costs that
    /// one setting its default, never the app — and a resolver that returned an out-of-range number
    /// would hand it to a spawn or a timer.
    /// </summary>
    [Fact]
    public void AnInvalidValueInALayer_IsSkipped_AndTheLayerBelowAnswers()
    {
        var definition = Catalog.Find_OrNull("phone.status.intervalMinutes")!;

        var (value, origin) = Settings_Resolver.Resolve(definition, presetTree: null, Tree("""{"phone":{"status":{"intervalMinutes":9000}}}"""), session: null);

        Assert.Equal(30, value!.GetValue<int>());
        Assert.Equal(SettingOrigins.ShippedDefault, origin);
    }

    /// <summary>
    /// NULL IS A REAL ANSWER FOR A NULLABLE ENUM, and it has to be distinguishable from "this layer
    /// said nothing" — `effort.supervisor: null` in the quiet preset MEANS "emit no --effort flag",
    /// which is not the same as the key being absent from a preset that sets xhigh.
    /// </summary>
    [Fact]
    public void AnExplicitJsonNull_ForANullableSetting_IsAnAnswer_NotAnAbsence()
    {
        var definition = Catalog.Find_OrNull("effort.supervisor")!;

        var (value, origin) = Settings_Resolver.Resolve(
            definition,
            Tree("""{"effort.supervisor":"xhigh"}"""),
            Tree("""{"effort":{"supervisor":null}}"""),
            session: null);

        Assert.Null(value);
        Assert.Equal(SettingOrigins.ConfigFile, origin);
    }

    /// <summary>
    /// A hand-rolled stub, not a mock: every member IOrchestrationSession declares throws
    /// NotSupportedException except the seven <see cref="SessionScoped_Reader"/> actually touches, so
    /// a resolver call that strays onto an unrelated member fails loudly rather than returning a
    /// quiet default.
    /// </summary>
    sealed class TestSession : IOrchestrationSession
    {
        public string? SupervisorModelOverride { get; set; }
        public string? ImplementerModelOverride { get; set; }
        public string? SupervisorEffortOverride { get; set; }
        public string? ImplementerEffortOverride { get; set; }
        public bool Paused { get; set; }
        public global::AIOrchestratorCoreLib.Telegram.TelegramDeliveryModes TelegramMode { get; set; } = global::AIOrchestratorCoreLib.Telegram.TelegramDeliveryModes.Normal;
        public global::AIOrchestratorCoreLib.Telegram.OwnerPresenceModes OwnerPresence { get; set; } = global::AIOrchestratorCoreLib.Telegram.OwnerPresenceModes.Remote;

        public string OrchId => throw new NotSupportedException();
        public string RepoName => throw new NotSupportedException();
        public string RepoPath => throw new NotSupportedException();
        public DateTime CreatedUtc => throw new NotSupportedException();
        public long? TelegramTopicId => throw new NotSupportedException();
        public long? StatusLineMessageId => throw new NotSupportedException();
        public int? SupervisorPid => throw new NotSupportedException();
        public DateTime? SupervisorSpawnedUtc => throw new NotSupportedException();
        public DateTime? CommunicatorSpawnedUtc => throw new NotSupportedException();
        public string? DisplayName => throw new NotSupportedException();
        public IReadOnlyList<IOrchestrationMember> Members => throw new NotSupportedException();
        public bool AwaitingTest => throw new NotSupportedException();
        public bool Done => throw new NotSupportedException();
        public DateTime? TelegramTopicDeletePendingUtc => throw new NotSupportedException();
        public DateTime? TelegramTopicDeletedUtc => throw new NotSupportedException();
        public bool TelegramTopicDeleteFailureReported => throw new NotSupportedException();
        public DateTime? ClosedUtc => throw new NotSupportedException();
    }
}
