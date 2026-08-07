using System.Windows.Media;

namespace AIOrchestrator.Views;

/// <summary>Display model for one orchestration card. Rebuilt on every refresh; bindings are one-way.</summary>
public class OrchestrationCardView
{
    /// <summary>The REAL orchestration id — what every action handler must send. Never the display name.</summary>
    public string OrchId { get; init; } = string.Empty;

    /// <summary>What the header shows: the goal name when set, else the orch id.</summary>
    public string Title { get; init; } = string.Empty;

    public string RepoName { get; init; } = string.Empty;
    public string SummaryText { get; init; } = string.Empty;
    public string ClosedLabel { get; init; } = string.Empty;
    public bool IsClosed { get; init; }
    public double CardOpacity { get; init; } = 1.0;
    public IReadOnlyList<MemberRowView> Members { get; init; } = [];

    /// <summary>Collapsed on the general supervisor's card: it has no implementers and cannot be closed.</summary>
    public System.Windows.Visibility OrchestrationButtonsVisibility { get; init; } = System.Windows.Visibility.Visible;

    /// <summary>PLAN.md ledger progress — collapsed when the supervisor keeps no plan file.</summary>
    public System.Windows.Visibility ProgressVisibility { get; init; } = System.Windows.Visibility.Collapsed;

    public string ProgressText { get; init; } = string.Empty;

    /// <summary>Star-sized pair driving the slim progress bar: done fraction vs the rest.</summary>
    public System.Windows.GridLength ProgressDoneStar { get; init; } = new(0, System.Windows.GridUnitType.Star);

    public System.Windows.GridLength ProgressLeftStar { get; init; } = new(1, System.Windows.GridUnitType.Star);
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

    /// <summary>Collapsed on closed members/supervisors — there is no terminal left to show.</summary>
    public System.Windows.Visibility ShowButtonVisibility { get; init; } = System.Windows.Visibility.Visible;

    /// <summary>Dimmed for a closed member inside a still-open card (closed cards dim as a whole).</summary>
    public double RowOpacity { get; init; } = 1.0;
}
