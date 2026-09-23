namespace AIOrchestratorCoreLib.Bridge.LimitRescue;

/// <summary>
/// What a /resume sweep did, slot by slot: which sessions were stopped for the watchdog to restart,
/// and which were left alone and why. Slots are named <c>general</c>, <c>&lt;orch&gt;/supervisor</c>
/// or <c>&lt;orch&gt;/&lt;member&gt;</c>.
///
/// <para>
/// THE OWNER'S COUNT IS RESTARTS AGAINST SESSIONS ALREADY AWAKE (owner, 2026-09-23). /resume used to
/// answer "woke 7 session(s)" for seven channel appends that no watcher was armed to hear — every
/// monitor had expired at the limit — so the number described nothing that happened. Named in the
/// log, counted on the phone: the log is where the owner goes to find WHICH one.
/// </para>
/// </summary>
public sealed class LimitRescueSweep(IReadOnlyList<string> restarted, IReadOnlyList<(string Slot, LimitRescueSkips Reason)> leftAlone)
{
    public IReadOnlyList<string> Restarted { get; } = restarted;

    public IReadOnlyList<(string Slot, LimitRescueSkips Reason)> LeftAlone { get; } = leftAlone;

    public int Count_LeftAlone(LimitRescueSkips reason)
    {
        return LeftAlone.Count(pair => pair.Reason == reason);
    }

    /// <summary>One line naming every session restarted and every session left alone, with the reason.</summary>
    public string Describe_ForLog()
    {
        var restarted = Restarted.Count == 0 ? "none" : string.Join(", ", Restarted);
        var leftAlone = LeftAlone.Count == 0 ? "none" : string.Join(", ", LeftAlone.Select(pair => $"{pair.Slot} ({Describe_Reason(pair.Reason)})"));

        return $"/resume — restarted {Restarted.Count} session(s) stuck on a usage limit so the watchdog brings them back into the GO AHEAD: {restarted}; left alone: {leftAlone}";
    }

    /// <summary>The owner-facing half: counts only, the zero clauses dropped (decision 15 — say what they can act on).</summary>
    public string Describe_ForOwner()
    {
        var parts = new List<string>
        {
            $"restarted {Restarted.Count} session{Plural(Restarted.Count)} stuck on the usage limit",
            $"{Count_LeftAlone(LimitRescueSkips.NotBlocked)} already awake",
        };

        var starting = Count_LeftAlone(LimitRescueSkips.StartedSinceRefusal);

        if (starting > 0)
            parts.Add($"{starting} already restarting");

        var notRunning = Count_LeftAlone(LimitRescueSkips.NotRunning);

        if (notRunning > 0)
            parts.Add($"{notRunning} not running (the watchdog starts {(notRunning == 1 ? "it" : "them")})");

        var bridgeDriven = Count_LeftAlone(LimitRescueSkips.BridgeDriven);

        if (bridgeDriven > 0)
            parts.Add($"{bridgeDriven} bridge-driven (the dispatcher re-runs {(bridgeDriven == 1 ? "its turn" : "their turns")})");

        return string.Join(" · ", parts);
    }

    static string Describe_Reason(LimitRescueSkips reason)
    {
        return reason switch
        {
            LimitRescueSkips.NotBlocked => "awake — not blocked on a usage limit",
            LimitRescueSkips.NotRunning => "not running — the watchdog starts it",
            LimitRescueSkips.StartedSinceRefusal => "restarted since its refusal — still starting up",
            _ => "bridge-driven — the dispatcher re-runs its turn",
        };
    }

    static string Plural(int count) => count == 1 ? "" : "s";
}

/// <summary>Why a /resume sweep left a session alone.</summary>
public enum LimitRescueSkips
{
    NotBlocked,
    NotRunning,
    BridgeDriven,
    StartedSinceRefusal,
}
