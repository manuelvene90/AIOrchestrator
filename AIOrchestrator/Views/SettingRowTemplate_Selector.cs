using System.Windows;
using System.Windows.Controls;

namespace AIOrchestrator.Views;

/// <summary>
/// PICKS A SETTINGS ROW'S CONTROL TEMPLATE BY LOOKING IT UP, NOT BY DECIDING (plan 04 Task 9). The templates
/// in <c>SettingsWindow.xaml</c> are keyed by the <c>SettingEditorKinds</c> value itself, and the kind was
/// chosen by CoreLib's <c>SettingEditor_Factory</c> — so there is no <c>switch</c> here to drift from it, and a
/// kind with no template draws nothing rather than the wrong control. The WPF project may hold no decision
/// (plan 04's global constraint: it is untestable by this suite).
/// </summary>
public sealed class SettingRowTemplate_Selector : DataTemplateSelector
{
    public override DataTemplate? SelectTemplate(object item, DependencyObject container)
    {
        if (item is SettingRowView row && container is FrameworkElement element)
            return element.TryFindResource(row.EditorKind) as DataTemplate;

        return base.SelectTemplate(item, container);
    }
}
