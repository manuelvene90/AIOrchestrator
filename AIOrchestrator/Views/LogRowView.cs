using System.Windows.Media;

namespace AIOrchestrator.Views;

/// <summary>One row of the in-app activity log (dark-themed, per-level colored text).</summary>
public class LogRowView
{
    public string TimeText { get; init; } = string.Empty;
    public string Message { get; init; } = string.Empty;
    public Brush MessageBrush { get; init; } = Brushes.Gray;
}
