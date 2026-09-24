using System.IO;
using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using AIOrchestrator.Views;
using AIOrchestratorCoreLib.Configuration;
using AIOrchestratorCoreLib.Configuration.OrchestratorConfigProvider;
using AIOrchestratorCoreLib.Configuration.SettingsPresentation;
using AIOrchestratorCoreLib.Configuration.SettingsPresentation.SettingEditor;
using AIOrchestratorCoreLib.Configuration.SettingsPresentation.SettingReading;
using AIOrchestratorCoreLib.Configuration.SettingsWriting;
using AIOrchestratorCoreLib.Logging.OrchestrationLog;
using AIOrchestratorCoreLib.SupervisionPaths;

namespace AIOrchestrator;

/// <summary>
/// THE SETTINGS WINDOW, GENERIC OVER THE CATALOGUE (plan 04 Task 9). One tab per catalogue category plus
/// Connection; every row is a CoreLib reading drawn through a CoreLib editor, and every edit is written the
/// moment it is made through <see cref="Settings_Writer"/> (D5 — immediate-apply, no Cancel), then the row is
/// re-read from disk so its value and origin label show what the file now says.
///
/// <para>
/// PLUMBING ONLY. This project is untestable by the suite, so this file forwards: a click becomes the editor's
/// value, the value goes to the writer, the writer's answer goes to <see cref="SettingWriteNote_Formatter"/>, and
/// the fresh reading goes back to the row. It never rebuilds a config object to change a setting — the old
/// 13-argument <c>OrchestratorConfig_Factory.Create</c> + <c>Save</c> is gone, because that shape rewrote every
/// key it owned at its resolved value and turned presets into "set here" (P11, spec §6.2).
/// </para>
/// <para>
/// THE TOKEN IS THE ONE HAND-WRITTEN FIELD (D11): it is deliberately not a catalogue row, it lives in
/// secrets.json, and it is written by <see cref="OrchestratorConfig_Loader.Save_BotToken"/> alone — never the
/// full Save — behind its own explicit button, so half a pasted token is never written keystroke by keystroke.
/// </para>
/// </summary>
public partial class SettingsWindow : Window
{
    readonly ISupervisionPaths _paths;
    readonly IOrchestrationLog? _log;
    readonly List<SettingRowView> _rows = [];

    public SettingsWindow(ISupervisionPaths paths, IOrchestratorConfigProvider configProvider, IOrchestrationLog? log)
    {
        _paths = paths;
        _log = log;

        InitializeComponent();
        Views.DarkTitleBar_Enabler.Apply(this);

        var (readings, presetName) = SettingsSnapshot_Reader.Read_All_FromDisk(paths, session: null, log);

        PresetText.Text = SettingsRow_Builder.Describe_PresetHeader(presetName);
        BotTokenTextBox.Text = configProvider.Get_Current().TelegramBotToken ?? string.Empty;
        ConnectionRowsList.ItemsSource = Create_Rows(SettingsRow_Builder.Select_ConnectionRows(readings));

        var sectionTemplate = (DataTemplate)FindResource("SectionTemplate");

        foreach (var (_, title, rows) in SettingsRow_Builder.Build_Sections(readings))
            SettingsTabs.Items.Add(new TabItem { Header = title, Content = Create_Rows(rows), ContentTemplate = sectionTemplate });
    }

    IReadOnlyList<SettingRowView> Create_Rows(IReadOnlyList<ISettingReading> readings)
    {
        var rows = readings.Select(reading => new SettingRowView(SettingEditor_Factory.Create_ForReading(reading))).ToArray();

        _rows.AddRange(rows);

        return rows;
    }

    // ── Row edits: the editor builds the value, the writer judges and writes it ──

    void Toggle_Click(object sender, RoutedEventArgs e)
    {
        var row = Row_Of(sender);

        Commit(row, row.Editor.Build_FromToggle(((CheckBox)sender).IsChecked == true));
    }

    void Choice_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        // Only a pick the owner made: drawing the row (or redrawing it after an edit) selects the current
        // offer, which is not an edit and must not be written back.
        if (e.AddedItems.Count != 1 || e.AddedItems[0] is not string caption)
            return;

        var row = Row_Of(sender);

        if (caption == row.SelectedOffer)
            return;

        Commit(row, row.Editor.Build_FromText(caption));
    }

    void ApplyText_Click(object sender, RoutedEventArgs e)
    {
        var row = Row_Of(sender);

        Commit(row, row.Editor.Build_FromText(row.EditText));
    }

    void Box_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter)
            return;

        var row = Row_Of(sender);

        Commit(row, row.Editor.Build_FromText(row.EditText));
    }

    void SetSecret_Click(object sender, RoutedEventArgs e)
    {
        Commit_Secret(Row_Of(sender));
    }

    void Secret_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
            Commit_Secret(Row_Of(sender));
    }

    /// <summary>
    /// The editor answers null for a blank box — NO EDIT, never "clear" (the box is always drawn empty, because the
    /// value is masked): a set token is cleared by the row's Reset alone (review of 8be367a).
    /// </summary>
    void Commit_Secret(SettingRowView row)
    {
        var secret = row.Editor.Build_Secret_OrNull(row.EditText);

        if (secret != null)
            Commit(row, secret);
    }

    void MoveItemUp_Click(object sender, RoutedEventArgs e)
    {
        var item = Item_Of(sender);

        Commit(item.Row, item.Row.Editor.Build_Moved(item.Index, -1));
    }

    void MoveItemDown_Click(object sender, RoutedEventArgs e)
    {
        var item = Item_Of(sender);

        Commit(item.Row, item.Row.Editor.Build_Moved(item.Index, 1));
    }

    void RemoveItem_Click(object sender, RoutedEventArgs e)
    {
        var item = Item_Of(sender);

        Commit(item.Row, item.Row.Editor.Build_Removed(item.Index));
    }

    void AddOffer_Click(object sender, RoutedEventArgs e)
    {
        var row = Row_Of(sender);

        if (row.PickedOffer != null)
            Commit_Addition(row, row.PickedOffer);
    }

    void AddWord_Click(object sender, RoutedEventArgs e)
    {
        var row = Row_Of(sender);

        Commit_Addition(row, row.NewWord);
    }

    void NewWord_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter)
            return;

        var row = Row_Of(sender);

        Commit_Addition(row, row.NewWord);
    }

    void Reset_Click(object sender, RoutedEventArgs e)
    {
        var row = Row_Of(sender);

        Write_AndRedraw(row.Path, () => Settings_Writer.Reset(_paths, row.Path, _log));
    }

    /// <summary>The editor answers null when the text holds no word to add — nothing to write, so nothing is.</summary>
    void Commit_Addition(SettingRowView row, string text)
    {
        var added = row.Editor.Build_Added_OrNull(text);

        if (added != null)
            Commit(row, added);
    }

    /// <summary>
    /// A value the row already reads writes nothing and says so: an untouched box holds the RESOLVED value, and
    /// writing it back would turn a preset or shipped value into "set here" (review of 8be367a, P11's spirit).
    /// </summary>
    void Commit(SettingRowView row, JsonNode? value)
    {
        if (row.Editor.Is_Unchanged(value))
        {
            row.Show_Note((SettingWriteNote_Formatter.UNCHANGED, false));
            return;
        }

        Write_AndRedraw(row.Path, () => Settings_Writer.Apply(_paths, row.Path, value, _log));
    }

    /// <summary>
    /// One write, then every row showing that path redrawn from disk (the two ids appear on Connection AND
    /// Kernel), with the note beside it. A write that threw wrote nothing (Task 2's rule) and says so; a
    /// refusal — the definition's or the file's (P33) — is the writer's own message (decision 21).
    /// </summary>
    void Write_AndRedraw(string path, Func<(SettingsWriteOutcomes Outcome, string? Message_OrNull)> write)
    {
        (string Text, bool IsRefusal) note;

        try
        {
            var (outcome, message) = write();
            note = SettingWriteNote_Formatter.Describe(outcome, message);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            note = (SettingWriteNote_Formatter.Describe_WriteThrew(ex.Message), true);
        }

        var reading = SettingsSnapshot_Reader.Read_One_FromDisk_OrNull(path, _paths, session: null, _log);

        foreach (var row in _rows.Where(candidate => candidate.Path == path))
        {
            if (reading != null)
                row.Show(SettingEditor_Factory.Create_ForReading(reading));

            row.Show_Note(note);
        }
    }

    // ── Connection: the token's own Save (D11, P11) ──

    void SaveTokenButton_Click(object sender, RoutedEventArgs e)
    {
        // secrets.json ONLY, through the token's own door; an unreadable or corrupt secrets.json is refused
        // with an IOException naming the file (Task 2c, P35/P36), and nothing was written.
        // The box then shows the token AS WRITTEN (the loader trims it), not as pasted.
        try
        {
            BotTokenTextBox.Text = OrchestratorConfig_Loader.Save_BotToken(_paths, BotTokenTextBox.Text) ?? string.Empty;
            TokenNoteText.DataContext = new SettingNoteView((SettingsRow_Builder.BOT_TOKEN_SAVED_NOTE, false));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            TokenNoteText.DataContext = new SettingNoteView((SettingWriteNote_Formatter.Describe_WriteThrew(ex.Message), true));
        }
    }

    void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }

    static SettingRowView Row_Of(object sender)
    {
        return (SettingRowView)((FrameworkElement)sender).DataContext;
    }

    static SettingListItemView Item_Of(object sender)
    {
        return (SettingListItemView)((FrameworkElement)sender).DataContext;
    }
}
