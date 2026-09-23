using System.Text.Json.Nodes;
using AIOrchestratorCoreLib.Configuration.SettingsCatalog;
using AIOrchestratorCoreLib.Configuration.SettingsCatalog.SettingDefinition;
using Xunit;

using Catalog = global::AIOrchestratorCoreLib.Configuration.SettingsCatalog.SettingsCatalog;

namespace AIOrchestratorCoreLib.Tests.Configuration.SettingsCatalog;

/// <summary>
/// THE PHASE-2 GATE (spec §10): with <c>classic</c> the machine resolves to what master did before
/// the merge, with <c>quiet</c> to what the fork does. Plan 02 wires no behaviour, so what a probe
/// can measure here is the RESOLVED VALUE of every catalogue entry under each preset — which is
/// exactly the surface plan 03's seams will read. A seam that later disagrees with one of these
/// lines has changed the owner's phone without anyone deciding to.
///
/// <para>
/// EXTENDED TO THE PHONE BY PLAN 03 (Task 12, 2026-09-23), not replaced: <see cref="PresetPhoneProbeTests"/>
/// drives the real engine under each preset and asserts what reaches the owner's phone — the sends, edits
/// and reactions in order, and which of them rang. These rows stay the value half of the gate; a row here
/// that moves without its phone probe moving says the engine stopped reading the key.
/// </para>
/// <para>
/// SPELLED OUT RATHER THAN COMPUTED. Deriving the expected values from the preset files would make
/// this test assert that a file equals itself; spec §12 names <c>quiet</c> reproducing the fork's
/// phone exactly as a top risk, and a risk is not mitigated by a tautology.
/// </para>
/// <para>
/// THE FOUR MODEL ROWS RESOLVE IDENTICALLY UNDER BOTH PRESETS, AND THAT IS THE CORRECTED TRUTH, not
/// a copy-paste (2026-09-12, task 8). The plan's own draft of this file carried
/// <c>[InlineData("models.supervisor", "\"claude-fable-5-1\"")]</c> under <c>classic</c>; it was
/// written before task-6 fix round 1 removed the four <c>models.*</c> rows from
/// <c>kit/presets/classic.json</c>, precisely because a preset restating an older model on every
/// judging role made the catalogue's registered Opus unreachable on any machine naming no preset —
/// the common case. Both presets are now silent about models, so both resolve to the shipped
/// default, and a row here that read <c>claude-fable-5-1</c> again would mean the preset had
/// quietly taken the default back.
/// </para>
/// <para>
/// A MISSING DEFINITION THROWS RATHER THAN READING AS A PASS (CLAUDE.md decision 20): a probe that
/// cannot find the entry it measures must refuse to run, never certify the absence of the thing it
/// is testing. <see cref="Definition"/> is the only way into the catalogue from this file, and a
/// null expectation below therefore means exactly one thing — the entry resolved to JSON null —
/// never "the path was misspelled and nothing was measured".
/// </para>
/// </summary>
public class PresetProbeTests
{
    /// <summary>
    /// A WHOLE MACHINE, NOT A HAND-BUILT TREE: the preset comes from the same
    /// <see cref="Presets_Loader.Resolve_ForConfig"/> the loader calls, so the <c>classic</c> arm
    /// also exercises spec §11.3's "absent <c>preset</c> means classic" rather than naming the word.
    /// The config tree carries only the <c>preset</c> key — no catalogue path is spelled
    /// <c>preset</c>, so it can answer for nothing below.
    /// </summary>
    static string? Resolve(string presetName, string path)
    {
        var configTree = presetName == Presets_Loader.CLASSIC
            ? null
            : (JsonObject)JsonNode.Parse($$"""{"preset":"{{presetName}}"}""")!;

        var preset = Presets_Loader.Resolve_ForConfig(configTree).Tree;

        return Settings_Resolver.Resolve(Definition(path), preset, configTree, session: null).Value?.ToJsonString();
    }

    static ISettingDefinition Definition(string path)
    {
        return Catalog.Find_OrNull(path)
            ?? throw new InvalidOperationException(
                $"'{path}' is not a catalogue path, so this probe measured nothing at all for it. "
                + "Correct the row or register the entry — a probe that cannot find its subject must "
                + "fail loudly rather than certify its absence (CLAUDE.md decision 20).");
    }

    [Theory]
    // Manu's phone, as master shipped it.
    [InlineData("models.supervisor", "\"opus\"")]
    [InlineData("models.implementer", "\"opus\"")]
    [InlineData("models.solo", "\"opus\"")]
    [InlineData("models.general", "\"sonnet\"")]
    [InlineData("effort.supervisor", "\"xhigh\"")]
    [InlineData("effort.solo", "\"xhigh\"")]
    [InlineData("effort.implementer", null)]
    [InlineData("phone.push", "\"filtered\"")]
    [InlineData("phone.status.periodic", "true")]
    [InlineData("phone.appMessagesRing", "true")]
    [InlineData("phone.receipts", "\"ticks\"")]

    // OFF, NOT MASTER'S "on": the owner's answer to D5 (2026-09-14) is "off for both presets", so
    // classic stopped stating the key and reads the catalogue's shipped default like quiet does.
    [InlineData("phone.replyKeyboard", "\"off\"")]

    // THE OWNER'S 6 s WITH NO DISCOUNT (2026-09-23, plan 03 task 13): time to press ⏸ Wait.
    [InlineData("phone.aggregationSeconds", "6")]
    [InlineData("phone.finishedMessageSeconds", "6")]
    [InlineData("pulse.holdToggle", "false")]

    // THE COUNT ON TOP, COMPACT (owner, 2026-09-23, plan 03 task 16): `progress` replaced `merged`.
    [InlineData("pulse.fields", """["progress","supervisor","members","modelEffort","updated"]""")]
    [InlineData("general.buttons", "[]")]

    // DELETE, NOT MASTER'S "close": the owner's answer to D2 (2026-09-14) is delete as the shipped
    // default with classic following it (ruling R4), so classic states nothing and reads the catalogue.
    [InlineData("topic.onClose", "\"delete\"")]
    [InlineData("topic.modeGlyphs", "\"name\"")]

    // NO PER-REPO TOPIC COLOUR (owner, 2026-09-23, plan 03 task 14): "colored without any context of why".
    [InlineData("topic.repoColours", "false")]
    [InlineData("runners.supervisor.runner", "\"terminal\"")]
    [InlineData("runners.implementer.runner", "\"terminal\"")]
    [InlineData("runners.implementer.resume", "\"transcript\"")]
    [InlineData("runners.general.resume", "\"fresh\"")]

    // NO CODE ON A HIGH-RISK QUESTION (owner, 2026-09-23, plan 03 task 15): "I don't want that."
    [InlineData("highRiskConfirmation", "false")]
    public void UnderClassic_TheMachineResolvesToMastersWay(string path, string? expected)
    {
        Assert.Equal(expected, Resolve(Presets_Loader.CLASSIC, path));
    }

    [Theory]
    // Nathan's phone, as the fork shipped it.
    [InlineData("models.supervisor", "\"opus\"")]
    [InlineData("models.implementer", "\"opus\"")]
    [InlineData("models.solo", "\"opus\"")]
    [InlineData("models.general", "\"sonnet\"")]
    [InlineData("effort.supervisor", null)]
    [InlineData("effort.solo", null)]
    [InlineData("effort.implementer", null)]
    [InlineData("phone.push", "\"everything\"")]
    [InlineData("phone.status.periodic", "false")]
    [InlineData("phone.appMessagesRing", "false")]
    [InlineData("phone.receipts", "\"reactions\"")]
    [InlineData("phone.replyKeyboard", "\"off\"")]
    [InlineData("phone.aggregationSeconds", "3")]
    [InlineData("phone.finishedMessageSeconds", "2")]
    [InlineData("pulse.holdToggle", "true")]
    [InlineData("topic.onClose", "\"delete\"")]
    [InlineData("topic.modeGlyphs", "\"pulseHeader\"")]
    [InlineData("topic.repoColours", "true")]
    [InlineData("runners.supervisor.runner", "\"stream\"")]
    [InlineData("runners.implementer.runner", "\"print\"")]
    [InlineData("runners.reviewer.runner", "\"print\"")]
    [InlineData("runners.solo.runner", "\"print\"")]
    [InlineData("runners.general.runner", "\"print\"")]
    [InlineData("runners.communicator.runner", "\"terminal\"")]
    [InlineData("runners.implementer.resume", "\"fresh\"")]
    [InlineData("runners.general.resume", "\"fresh\"")]
    [InlineData("highRiskConfirmation", "true")]
    public void UnderQuiet_TheMachineResolvesToTheForksWay(string path, string? expected)
    {
        Assert.Equal(expected, Resolve(Presets_Loader.QUIET, path));
    }

    /// <summary>
    /// EVERY CATALOGUE ENTRY RESOLVES UNDER BOTH PRESETS, without throwing and to a value its own
    /// definition accepts. The two [Theory] blocks above pin the values that differ; this one pins
    /// that nothing in the registry is unresolvable — the shape spec §12 calls "most likely to
    /// drift", caught before three renderers each meet it separately.
    ///
    /// <para>
    /// AN EMPTY CATALOGUE WOULD PASS THIS LOOP VACUOUSLY, which is the same nothing-is-ALLOW failure
    /// CLAUDE.md decision 20 names, so the count is asserted before the walk.
    /// </para>
    /// </summary>
    [Theory]
    [InlineData(Presets_Loader.CLASSIC)]
    [InlineData(Presets_Loader.QUIET)]
    public void EveryCatalogueEntry_ResolvesUnderBothPresets(string presetName)
    {
        Assert.NotEmpty(Catalog.ALL);

        var preset = Presets_Loader.Load_Embedded(presetName);

        foreach (var definition in Catalog.ALL)
        {
            if (definition.Kind == SettingKinds.Composite)
                continue;

            var (value, _) = Settings_Resolver.Resolve(definition, preset, configTree: null, session: null);
            var refusal = definition.Validate_OrNull(value);

            // Named rather than a bare Assert.Null: a gate that says only "expected null, got a
            // string" leaves the reader to find WHICH of forty-odd entries broke.
            Assert.True(refusal == null, $"under '{presetName}', '{definition.Path}' resolved to a value its own definition refuses: {refusal}");
        }
    }
}
