namespace AIOrchestratorCoreLib.WindowFocus;

/// <summary>
/// Does this window title name THIS session — and only this one.
///
/// IT WAS A PLAIN SUBSTRING TEST, and that is a real collision the moment a repo has ten
/// orchestrations. `SOLO · da-vinci-fintech-suite-1` is a substring of
/// `SOLO · da-vinci-fintech-suite-10`, so orchestration 1 found orchestration 10's window: Show
/// focused the wrong terminal, Organize tiled it in the wrong place — the owner's report that
/// organize "doesn't really work" (2026-08-20) — and the shutdown terminator, which closes windows
/// by the same fragment, could close a session that was still working.
///
/// A title is `LABEL · orch-id`, optionally followed by ` · display name`. So the fragment must end
/// at a BOUNDARY: either the title is exactly the fragment, or what follows the fragment is the
/// separator. `-10` fails that test because what follows `…-1` is `0`, not a separator.
/// </summary>
public static class SessionWindowTitle_Matcher
{
    /// <summary>The separator SessionWindowTitle_Builder puts between a title's parts.</summary>
    public const string SEPARATOR = " · ";

    public static bool Matches(string windowTitle, string titleFragment)
    {
        if (string.IsNullOrEmpty(titleFragment))
            return false;

        var at = windowTitle.IndexOf(titleFragment, StringComparison.OrdinalIgnoreCase);

        if (at < 0)
            return false;

        var after = at + titleFragment.Length;

        // Ends the title, or is followed by the separator. Anything else means the fragment stopped
        // in the middle of a longer id.
        return after == windowTitle.Length
            || windowTitle.AsSpan(after).StartsWith(SEPARATOR, StringComparison.Ordinal);
    }
}
