namespace AIOrchestrator.Views;

/// <summary>
/// A NOTE BESIDE A CONTROL THAT IS NOT A CATALOGUE ROW — the Connection tab's token Save — bindable under the SAME
/// member names as <see cref="SettingRowView"/>'s note, so the one <c>NoteStyle</c> colours a refusal red in both
/// places. The text and whether it is a refusal come from CoreLib's <c>SettingWriteNote_Formatter</c>.
/// </summary>
public sealed class SettingNoteView((string Text, bool IsRefusal) note)
{
    public string Note { get; } = note.Text;

    public bool NoteIsRefusal { get; } = note.IsRefusal;
}
