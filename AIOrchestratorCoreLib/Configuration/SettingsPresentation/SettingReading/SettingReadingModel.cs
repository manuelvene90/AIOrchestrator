using System.Text.Json.Nodes;
using AIOrchestratorCoreLib.Configuration.SettingsCatalog;
using AIOrchestratorCoreLib.Configuration.SettingsCatalog.SettingDefinition;

namespace AIOrchestratorCoreLib.Configuration.SettingsPresentation.SettingReading;

/// <summary>
/// One reading as plain data. Built only through <see cref="SettingReading_Factory"/>, which detaches the
/// value and applies the catalogue's two rules; this model trusts what it is handed.
///
/// <para>
/// THE VALUE IS COPIED ON EVERY READ, not once. A <see cref="JsonNode"/> can have one parent: the first
/// renderer that put this value into a response body would otherwise leave it parented, and the second —
/// the web GET and the menu can serialise the same reading — would throw on attaching it. Settings values
/// are a word, a number or a short list, so a copy per read costs nothing worth saving.
/// </para>
/// </summary>
internal sealed class SettingReadingModel(
    ISettingDefinition definition,
    JsonNode? value_OrNull,
    SettingOrigins origin,
    string displayValue,
    string originLabel,
    string restartLabel,
    bool isEditable,
    string? sessionNote_OrNull,
    IReadOnlyList<string> offeredValues) : ISettingReading
{
    readonly JsonNode? _value_OrNull = value_OrNull;

    public ISettingDefinition Definition { get; } = definition;
    public JsonNode? Value_OrNull => _value_OrNull?.DeepClone();
    public SettingOrigins Origin { get; } = origin;
    public string DisplayValue { get; } = displayValue;
    public string OriginLabel { get; } = originLabel;
    public string RestartLabel { get; } = restartLabel;
    public bool IsEditable { get; } = isEditable;
    public string? SessionNote_OrNull { get; } = sessionNote_OrNull;
    public IReadOnlyList<string> OfferedValues { get; } = offeredValues;
}
