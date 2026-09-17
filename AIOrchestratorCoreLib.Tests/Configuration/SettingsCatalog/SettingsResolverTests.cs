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

    /// <summary>
    /// Three DISTINCT values (shipped default "opus", preset "sonnet", config "haiku") so both
    /// assertions carry weight — the earlier version used a config value equal to the shipped
    /// default, so a resolver that silently fell all the way through to the default would still pass
    /// the value assertion, leaving only the origin assertion load-bearing.
    /// </summary>
    [Fact]
    public void TheConfigFile_BeatsThePreset()
    {
        var definition = Catalog.Find_OrNull("models.supervisor")!;

        var (value, origin) = Settings_Resolver.Resolve(
            definition,
            Tree("""{"models.supervisor":"sonnet"}"""),
            Tree("""{"models":{"supervisor":"haiku"}}"""),
            session: null);

        Assert.Equal("haiku", value!.GetValue<string>());
        Assert.Equal(SettingOrigins.ConfigFile, origin);
    }

    /// <summary>
    /// THE SESSION IS THE TOP RUNG AND ONLY FOR ORCHESTRATION-SCOPE KEYS — this is the positive case,
    /// pinning that a session override actually wins.
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

    /// <summary>
    /// DOES NOT PIN THE SCOPE GUARD (review round 1, item 6): "phone.receipts" with no preset and no
    /// config tree is answered null by <see cref="Try_Session"/>'s own default arm in
    /// <see cref="SessionScoped_Reader"/> regardless of scope, so deleting the
    /// <c>definition.Scope == SettingScopes.Orchestration</c> guard in
    /// <see cref="Settings_Resolver.Resolve"/> would leave this test green too — it only covers "an
    /// unrecognised path with a session present still falls through to the shipped default", not
    /// "a Machine-scope key is refused a session lookup".
    /// </summary>
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
    /// said nothing" — `"effort": {"supervisor": null}` hand-written in config.json MEANS "emit no
    /// --effort flag", and it must beat a preset that sets xhigh (which `classic` does) rather than be
    /// read as the key being absent. Corrected 2026-09-12: this comment used to place the null in the
    /// QUIET preset, which carries no effort key at all — the layers it named were not the layers the
    /// case builds.
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
    /// PINS THE EXISTENCE MACHINERY ITSELF (spec §6.2 review round 1, item 4): a NULLABLE setting
    /// absent from the config tree and set in the preset must still resolve from the preset, with
    /// origin Preset. Without <c>NestedPath_Exists</c>/<c>ContainsKey</c> distinguishing "the key is
    /// absent" from "the key is present and JSON null", an absent nullable key in config.json would
    /// read back as the same <c>JsonNode? null</c> that <see cref="SettingsJson_Path.Read_OrNull"/>
    /// returns for an explicit null — silently stopping the fall-through to the preset's value.
    /// Verified by hand: deleting the <c>ContainsKey</c>/<c>NestedPath_Exists</c> check (so
    /// <c>Try_NestedPath</c> trusts <c>Read_OrNull</c> alone) turns this test red, because the config
    /// tree below has no <c>effort</c> key at all — <c>Read_OrNull</c> already answers null for it
    /// without any existence check, so the difference is purely in what the ABSENT case is allowed to
    /// mean.
    /// </summary>
    [Fact]
    public void ANullableSetting_AbsentFromConfig_AndSetInThePreset_ResolvesFromThePreset()
    {
        var definition = Catalog.Find_OrNull("effort.supervisor")!;

        var (value, origin) = Settings_Resolver.Resolve(
            definition,
            Tree("""{"effort.supervisor":"xhigh"}"""),
            Tree("""{"unrelated":true}"""),
            session: null);

        Assert.Equal("xhigh", value!.GetValue<string>());
        Assert.Equal(SettingOrigins.Preset, origin);
    }

    /// <summary>
    /// ALL SEVEN <see cref="SessionScoped_Reader"/> ARMS, EACH WITH ITS OWN DISTINGUISHABLE VALUE
    /// (review round 1, item 5): before this, only "models.supervisor" was exercised anywhere in this
    /// file, so a typo in any other arm — or a swap of the supervisor/implementer override properties
    /// — left every test green. The two model overrides and the two effort overrides deliberately
    /// never share a value, so a swap between the supervisor and implementer arm of either pair still
    /// fails.
    /// </summary>
    public static IEnumerable<object[]> SevenSessionScopedPaths()
    {
        yield return new object[] { "models.supervisor" };
        yield return new object[] { "models.implementer" };
        yield return new object[] { "effort.supervisor" };
        yield return new object[] { "effort.implementer" };
        yield return new object[] { "session.paused" };
        yield return new object[] { "session.telegramMode" };
        yield return new object[] { "session.ownerPresence" };
    }

    [Theory]
    [MemberData(nameof(SevenSessionScopedPaths))]
    public void EverySessionScopedArm_ReadsItsOwnDistinguishableValue(string path)
    {
        var definition = Catalog.Find_OrNull(path)!;
        var session = new TestSession
        {
            SupervisorModelOverride = "sup-model",
            ImplementerModelOverride = "imp-model",
            SupervisorEffortOverride = "low",
            ImplementerEffortOverride = "max",
            Paused = true,
            TelegramMode = global::AIOrchestratorCoreLib.Telegram.TelegramDeliveryModes.Deferred,
            OwnerPresence = global::AIOrchestratorCoreLib.Telegram.OwnerPresenceModes.Terminal,
        };

        var value = SessionScoped_Reader.Read_OrNull(definition, session);

        if (path == "session.paused")
        {
            Assert.True(value!.GetValue<bool>());
            return;
        }

        var expected = path switch
        {
            "models.supervisor" => "sup-model",
            "models.implementer" => "imp-model",
            "effort.supervisor" => "low",
            "effort.implementer" => "max",
            "session.telegramMode" => "Deferred",
            "session.ownerPresence" => "Terminal",
            _ => throw new InvalidOperationException($"Unhandled path '{path}' — add it to this switch."),
        };

        Assert.Equal(expected, value!.GetValue<string>());
    }

    /// <summary>
    /// RESOLVE_LONG READS ITS OWN SHIPPED DEFAULT (defect fixed 2026-09-17). It used to throw
    /// <see cref="InvalidOperationException"/> on exactly that layer for any <c>Kind = Int</c> row
    /// carrying a non-null default: <c>SettingDefinition_Factory.Create_Int</c> boxes the default with
    /// <c>JsonValue.Create(int)</c>, and a VALUE-backed JsonValue demands an exact type match from
    /// <c>GetValue&lt;T&gt;()</c>, unlike a node parsed from JSON TEXT, which converts freely. So the
    /// accessor could read a configured value and not the default it exists to fall back to — on an
    /// unconfigured machine, the ordinary case.
    ///
    /// <para>
    /// It survived because the only Kind=Int rows were nullable with a NULL default, where
    /// <c>value?.</c> short-circuits ahead of the cast, and because the accessor had no production
    /// caller at all until <c>reviewing.*</c>. Both shapes are asserted here, since the bug is
    /// precisely that the two layers are backed differently: the default comes from a value-backed
    /// node and the configured value from parsed text.
    /// </para>
    /// </summary>
    [Fact]
    public void ResolveLong_ReadsAnIntBackedShippedDefault_AndAParsedConfiguredValue()
    {
        var definition = Catalog.Find_OrNull("reviewing.reviewCapMinutes")!;

        Assert.Equal(90L, Settings_Resolver.Resolve_Long(definition, presetTree: null, configTree: null, session: null));

        var configured = Tree("""{"reviewing":{"reviewCapMinutes":45}}""");

        Assert.Equal(45L, Settings_Resolver.Resolve_Long(definition, presetTree: null, configTree: configured, session: null));
    }

    /// <summary>
    /// AND IT IS STILL LOUD ON A WRONG SHAPE. The tolerance added above is for the numeric BACKING of
    /// a node, never for its type: a setting whose value is a string is a fault to report, not a
    /// default to substitute quietly — the same reason <c>Require_Kind</c> throws rather than coercing.
    /// </summary>
    [Fact]
    public void ResolveLong_StillThrows_OnAValueThatIsNotANumber()
    {
        var definition = Catalog.Find_OrNull("reviewing.reviewCapMinutes")!;

        // Past the definition's own validator, which would refuse it in the config layer: the flat
        // whole-path key is read first, and this asserts the ACCESSOR's behaviour on a node it is
        // handed, not the validator's.
        var configured = Tree("""{"reviewing.reviewCapMinutes":"ninety"}""");

        var resolved = Settings_Resolver.Resolve(definition, presetTree: null, configTree: configured, session: null);

        // The validator refuses the string, so the resolver falls THROUGH to the shipped default —
        // which is the behaviour that matters, and it must be the number, not an exception.
        Assert.Equal(SettingOrigins.ShippedDefault, resolved.Origin);
        Assert.Equal(90L, Settings_Resolver.Resolve_Long(definition, presetTree: null, configTree: configured, session: null));
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
