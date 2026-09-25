using System.Text.Json.Nodes;
using AIOrchestratorCoreLib.Configuration.SettingsCatalog;
using AIOrchestratorCoreLib.Configuration.SettingsCatalog.SettingDefinition;

namespace AIOrchestratorCoreLib.Configuration.SettingsPresentation.SettingReading;

/// <summary>
/// ONE SETTING AS A RENDERER SHOWS IT — value, origin and row, already decided (plan 04 Task 1). Three
/// renderers draw from this: the Telegram <c>/settings</c> menu, the loopback web page and the WPF window.
/// None of them computes anything about a setting itself, because each thing they would compute — how a
/// value reads, what "from preset classic" says, whether a row can be edited, what to offer as choices —
/// would then exist three times and drift three ways (CLAUDE.md decision 12). A renderer draws these
/// members; it does not derive them.
///
/// <para>
/// BUILT ONLY BY <c>SettingsSnapshot_Reader</c> through <see cref="SettingReading_Factory"/>, which is where
/// the two rules that are pure functions of the catalogue (<see cref="IsEditable"/>,
/// <see cref="RestartLabel"/>) are applied, so no caller can hand in a reading that disagrees with them.
/// </para>
/// </summary>
public interface ISettingReading
{
    /// <summary>The catalogue row, whole: label, description, kind, scope, category — everything else a renderer shows.</summary>
    ISettingDefinition Definition { get; }

    /// <summary>
    /// The resolved value as JSON, or null when it resolved to nothing — and ALSO null for a masked secret
    /// (<c>web.token</c>, ruling P2), whose value never leaves the reader. A FRESH COPY on every read, owned
    /// by the caller: it can be put into a response body or edited without touching the tree it came from.
    /// </summary>
    JsonNode? Value_OrNull { get; }

    /// <summary>Which layer answered — the question the owner is actually asking (spec §6.2).</summary>
    SettingOrigins Origin { get; }

    /// <summary>The value as one bounded line of text (<c>SettingValue_Formatter</c>), or the mask's word for a secret.</summary>
    string DisplayValue { get; }

    /// <summary>"shipped default", "from preset classic", "set here" or "set for this orchestration" (<c>SettingOrigin_Labels</c>).</summary>
    string OriginLabel { get; }

    /// <summary>When a change takes effect (<c>RestartKind_Labels</c>) — shown, never enforced (spec §6.1).</summary>
    string RestartLabel { get; }

    /// <summary>
    /// <c>Definition.Renderer != SettingRenderers.ReadOnly</c> and NOTHING ELSE — the writer's own rule, read
    /// off the catalogue, so its refusal and a renderer's greyed-out control can never disagree (ruling P4).
    /// </summary>
    bool IsEditable { get; }

    /// <summary>
    /// When the session answered (<see cref="SettingOrigins.Session"/>), one sentence naming what it shadows —
    /// the machine's own value and its origin — because editing the row writes config.json and changes nothing
    /// on screen for this orchestration. Null for every other origin.
    /// </summary>
    string? SessionNote_OrNull { get; }

    /// <summary>
    /// What a renderer may draw as choices, so it never asks the catalogue a second question: on/off for a
    /// Toggle, the enum words (plus <c>SettingValue_Formatter.NOT_SET</c> for a nullable one) for a Choice, the
    /// known words for a picker list, and EMPTY for free text — Text, Number, a free-text list
    /// (<c>highRiskPatterns</c>) and ReadOnly. Drawing, not validating: the definition still says yes (decision 21).
    /// </summary>
    IReadOnlyList<string> OfferedValues { get; }
}
