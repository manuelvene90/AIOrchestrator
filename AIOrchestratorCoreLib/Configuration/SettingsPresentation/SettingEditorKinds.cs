namespace AIOrchestratorCoreLib.Configuration.SettingsPresentation;

/// <summary>
/// THE CONTROL AN EDITOR DRAWS FOR ONE READING — the WPF window's template key (plan 04 Task 9). It is
/// <c>SettingRenderers</c> with ONE split, and the split is why this exists rather than the renderer alone:
/// an <c>OrderedList</c> is a <see cref="WordPicker"/> when the reading offers words (<c>pulse.fields</c>, the
/// two button lists — D15, a typo is unreachable) and a <see cref="FreeTextList"/> when it offers none
/// (<c>highRiskPatterns</c>, ruling P5: add, remove, reorder, words typed). Which one a row gets is a decision,
/// and the WPF project may hold none (the global constraint: a <c>switch</c> there is in the wrong project), so
/// it is taken by <c>SettingEditor_Factory</c>, where a test pins it.
///
/// <para>
/// <see cref="Secret"/> IS THE MASKED ROW (<c>SettingsSnapshot_Reader.MASKED_SECRET_PATH</c>, <c>web.token</c>).
/// Its box is always empty because its value never leaves the reader (P2), so as a plain Text row an Apply on
/// that empty box wrote "" and CLEARED a set token with the words "Saved." — and a second Apply after setting
/// one erased it again (review of 8be367a, 2026-09-23). This window is the one place the page sends the owner
/// to set that token, and clearing it reopens token-less editing of the fenced rows (P32). A Secret row sets on
/// a non-blank box only; it is cleared by its Reset and by nothing else.
/// </para>
/// </summary>
public enum SettingEditorKinds
{
    Toggle,
    Choice,
    Number,
    Text,
    Secret,
    WordPicker,
    FreeTextList,
    ReadOnly,
}
