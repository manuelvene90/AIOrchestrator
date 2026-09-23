using System.Text.Json.Nodes;
using AIOrchestratorCoreLib.Configuration.SettingsCatalog;
using AIOrchestratorCoreLib.Configuration.SettingsCatalog.SettingDefinition;

namespace AIOrchestratorCoreLib.Configuration.SettingsPresentation.SettingReading;

/// <summary>
/// The only way to build an <see cref="ISettingReading"/>. It takes what the reader decided — the value,
/// its origin, how it reads, the offers — and applies the two rules that are functions of the catalogue
/// row alone, so they cannot be handed in wrong: <see cref="ISettingReading.IsEditable"/> is
/// <c>Renderer != ReadOnly</c> (the writer's rule, ruling P4) and <see cref="ISettingReading.RestartLabel"/>
/// is <see cref="RestartKind_Labels"/> of the row's restart kind.
///
/// <para>
/// THE VALUE IS DETACHED HERE, once, from whatever tree the resolver found it in. The embedded presets are
/// parsed once per process and shared: a reading holding a node of that tree would let one caller's edit
/// change the preset for every caller after it.
/// </para>
/// </summary>
public static class SettingReading_Factory
{
    public static ISettingReading Create(
        ISettingDefinition definition,
        JsonNode? value_OrNull,
        SettingOrigins origin,
        string displayValue,
        string originLabel,
        string? sessionNote_OrNull,
        IReadOnlyList<string> offeredValues)
    {
        if (string.IsNullOrWhiteSpace(displayValue))
            throw new ArgumentException($"The reading of '{definition.Path}' has a blank display value — every value reads as words, 'not set' included.");

        if (string.IsNullOrWhiteSpace(originLabel))
            throw new ArgumentException($"The reading of '{definition.Path}' has a blank origin label.");

        return new SettingReadingModel(
            definition,
            value_OrNull?.DeepClone(),
            origin,
            displayValue,
            originLabel,
            RestartKind_Labels.Describe(definition.Restart),
            isEditable: definition.Renderer != SettingRenderers.ReadOnly,
            sessionNote_OrNull,
            offeredValues);
    }
}
