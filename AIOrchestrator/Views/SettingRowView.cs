using System.ComponentModel;
using AIOrchestratorCoreLib.Configuration.SettingsPresentation;
using AIOrchestratorCoreLib.Configuration.SettingsPresentation.SettingEditor;

namespace AIOrchestrator.Views;

/// <summary>
/// ONE SETTINGS ROW AS THE WINDOW BINDS IT — a bindable copy of CoreLib's <see cref="ISettingEditor"/> and
/// nothing more (plan 04 Task 9). Every value here is read off the editor or its reading; nothing is decided:
/// which control, what the box holds, which words may move and which value an edit writes all come from
/// <c>SettingEditor_Factory</c>, where <c>SettingEditorTests</c> can see them. This class exists only because
/// WPF binds to properties and change notifications, and value tuples carry fields.
/// </summary>
public sealed class SettingRowView : INotifyPropertyChanged
{
    ISettingEditor _editor;

    public SettingRowView(ISettingEditor editor)
    {
        _editor = editor;
        EditText = editor.EditText;
        Items = Create_Items(editor);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public ISettingEditor Editor => _editor;

    public string Path => _editor.Reading.Definition.Path;

    public string Label => _editor.Reading.Definition.Label;

    public string Description => _editor.Reading.Definition.Description;

    public string DisplayValue => _editor.Reading.DisplayValue;

    public string OriginLabel => _editor.Reading.OriginLabel;

    public string RestartLabel => _editor.Reading.RestartLabel;

    public string? SessionNote => _editor.Reading.SessionNote_OrNull;

    public SettingEditorKinds EditorKind => _editor.Kind;

    public string? RangeHint => _editor.RangeHint_OrNull;

    public bool IsOn => _editor.IsOn;

    public IReadOnlyList<string> Offers => _editor.Reading.OfferedValues;

    public string? SelectedOffer => _editor.CurrentOffer_OrNull;

    public IReadOnlyList<string> RemainingOffers => _editor.RemainingOffers;

    public bool CanReset => _editor.CanReset;

    public IReadOnlyList<SettingListItemView> Items { get; private set; }

    /// <summary>A Number or Text box's text, two-way: what Apply hands to the editor's parser.</summary>
    public string EditText { get; set; }

    /// <summary>A free-text list's "add" box, two-way.</summary>
    public string NewWord { get; set; } = string.Empty;

    /// <summary>A picker's chosen word, two-way.</summary>
    public string? PickedOffer { get; set; }

    public string? Note { get; private set; }

    public bool NoteIsRefusal { get; private set; }

    /// <summary>Redraws the row from a fresh reading's editor, and discards whatever was typed but not applied.</summary>
    public void Show(ISettingEditor editor)
    {
        _editor = editor;
        EditText = editor.EditText;
        NewWord = string.Empty;
        PickedOffer = null;
        Items = Create_Items(editor);
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(string.Empty));
    }

    public void Show_Note((string Text, bool IsRefusal) note)
    {
        Note = note.Text;
        NoteIsRefusal = note.IsRefusal;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Note)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(NoteIsRefusal)));
    }

    IReadOnlyList<SettingListItemView> Create_Items(ISettingEditor editor)
    {
        return editor.Items
            .Select((item, index) => new SettingListItemView(this, index, item.Caption, item.CanMoveUp, item.CanMoveDown))
            .ToArray();
    }
}

/// <summary>One element of a list row, bindable: the editor's caption and move flags, and its index for the edit.</summary>
public sealed class SettingListItemView(SettingRowView row, int index, string caption, bool canMoveUp, bool canMoveDown)
{
    public SettingRowView Row { get; } = row;

    public int Index { get; } = index;

    public string Caption { get; } = caption;

    public bool CanMoveUp { get; } = canMoveUp;

    public bool CanMoveDown { get; } = canMoveDown;
}
