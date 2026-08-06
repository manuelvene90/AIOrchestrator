using System.Windows.Media;

namespace AIOrchestrator.Views;

/// <summary>Display model for one orchestration card. Rebuilt on every refresh; bindings are one-way.</summary>
public class OrchestrationCardView
{
    public string OrchId { get; init; } = string.Empty;
    public string RepoName { get; init; } = string.Empty;
    public string SummaryText { get; init; } = string.Empty;
    public string ClosedLabel { get; init; } = string.Empty;
    public bool IsClosed { get; init; }
    public double CardOpacity { get; init; } = 1.0;
    public IReadOnlyList<MemberRowView> Members { get; init; } = [];
}

/// <summary>One row inside a card: the supervisor or an implementer, with its state chip.</summary>
public class MemberRowView
{
    public string MemberLabel { get; init; } = string.Empty;
    public Brush RoleBrush { get; init; } = Brushes.Gray;
    public string StateText { get; init; } = string.Empty;
    public Brush StateBrush { get; init; } = Brushes.Gray;
    public string LastActivityText { get; init; } = string.Empty;

    /// <summary>Second line: current task · worktree · time on task · session cost.</summary>
    public string DetailText { get; init; } = string.Empty;

    /// <summary>Terminal window title fragment for the "Show session" button (sessions spawn one WT window each).</summary>
    public string FocusTitleFragment { get; init; } = string.Empty;
}
