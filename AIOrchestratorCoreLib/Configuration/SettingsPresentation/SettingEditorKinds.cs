namespace AIOrchestratorCoreLib.Configuration.SettingsPresentation;

/// <summary>
/// THE CONTROL AN EDITOR DRAWS FOR ONE READING — the WPF window's template key (plan 04 Task 9). It is
/// <c>SettingRenderers</c> with ONE split, and the split is why this exists rather than the renderer alone:
/// an <c>OrderedList</c> is a <see cref="WordPicker"/> when the reading offers words (<c>pulse.fields</c>, the
/// two button lists — D15, a typo is unreachable) and a <see cref="FreeTextList"/> when it offers none
/// (<c>highRiskPatterns</c>, ruling P5: add, remove, reorder, words typed). Which one a row gets is a decision,
/// and the WPF project may hold none (the global constraint: a <c>switch</c> there is in the wrong project), so
/// it is taken by <c>SettingEditor_Factory</c>, where a test pins it.
/// </summary>
public enum SettingEditorKinds
{
    Toggle,
    Choice,
    Number,
    Text,
    WordPicker,
    FreeTextList,
    ReadOnly,
}
