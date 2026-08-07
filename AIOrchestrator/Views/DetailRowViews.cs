using System.Windows.Media;

namespace AIOrchestrator.Views;

/// <summary>One PLAN.md task line in the detail window's ledger, color-coded by its marker.</summary>
public class PlanLineView
{
    public string MarkerGlyph { get; init; } = string.Empty;
    public Brush MarkerBrush { get; init; } = Brushes.Gray;
    public string TaskText { get; init; } = string.Empty;
    public double LineOpacity { get; init; } = 1.0;
    public System.Windows.FontWeight TaskWeight { get; init; } = System.Windows.FontWeights.Normal;
}

/// <summary>One entry in the detail window's merged activity feed (owner channel + every spoke).</summary>
public class ActivityRowView
{
    public string TimeText { get; init; } = string.Empty;
    public string AuthorLabel { get; init; } = string.Empty;
    public Brush AuthorBrush { get; init; } = Brushes.Gray;

    /// <summary>Which channel it came from: "owner" or "imp-2".</summary>
    public string SourceLabel { get; init; } = string.Empty;

    public string Subject { get; init; } = string.Empty;
    public string BodyPreview { get; init; } = string.Empty;
}

/// <summary>A labelled figure in the detail window's header strip.</summary>
public class StatChipView
{
    public string Label { get; init; } = string.Empty;
    public string Value { get; init; } = string.Empty;
    public Brush ValueBrush { get; init; } = Brushes.White;
}
