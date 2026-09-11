using System.Text.Json.Nodes;
using AIOrchestratorCoreLib.Running.TurnResult;

namespace AIOrchestratorCoreLib.Running.StreamTurn;

/// <summary>
/// How a turn on a living process ended — and the distinction the fallback ladder is built on.
///
/// <para>
/// <see cref="Completed"/> is the CLI answering, whether well or with <c>is_error</c>: the turn
/// failed, the TRANSPORT worked. <see cref="Died"/> is the process gone mid-conversation, and
/// <see cref="Silent"/> is a process that is alive and saying nothing. Only the last two say
/// anything about the transport, so only they can push a session down the ladder — a session
/// falling back to print because the model kept answering "I cannot do that" would be the app
/// diagnosing itself from the wrong evidence.
/// </para>
/// </summary>
public sealed class StreamTurnOutcome
{
    public ITurnResult Result { get; }

    /// <summary>The process ended before the turn did.</summary>
    public bool ProcessDied { get; }

    /// <summary>Nothing arrived for longer than the heartbeat allows, though the process is alive.</summary>
    public bool WentSilent { get; }

    /// <summary>How long the silence had lasted when it was called — what the log says instead of "timeout".</summary>
    public TimeSpan Silence { get; }

    /// <summary>The <c>rate_limit_info</c> seen IN THIS TURN, or null: the event is emitted at the change.</summary>
    public JsonObject? RateLimitInfo { get; }

    public string Stderr { get; }

    /// <summary>True when the transport itself failed — the only thing a fallback may be decided on.</summary>
    public bool IsStructuralFailure => ProcessDied;

    StreamTurnOutcome(ITurnResult result, bool processDied, bool wentSilent, TimeSpan silence, JsonObject? rateLimitInfo, string stderr)
    {
        Result = result;
        ProcessDied = processDied;
        WentSilent = wentSilent;
        Silence = silence;
        RateLimitInfo = rateLimitInfo;
        Stderr = stderr;
    }

    public static StreamTurnOutcome Completed(ITurnResult result, JsonObject? rateLimitInfo)
    {
        return new StreamTurnOutcome(result, processDied: false, wentSilent: false, TimeSpan.Zero, rateLimitInfo, string.Empty);
    }

    public static StreamTurnOutcome Died(ITurnResult result, string stderr)
    {
        return new StreamTurnOutcome(result, processDied: true, wentSilent: false, TimeSpan.Zero, null, stderr);
    }

    /// <summary>
    /// <paramref name="mute"/> separates "hung with the turn timeout still to run" from "the turn
    /// simply took too long". Both are killed and retried; only the first is worth saying out loud,
    /// because a session that goes quiet at 2 minutes and one that thinks for 30 are different
    /// animals and the log is where the difference has to survive.
    /// </summary>
    public static StreamTurnOutcome Silent(ITurnResult result, bool mute, TimeSpan silence)
    {
        return new StreamTurnOutcome(result, processDied: false, wentSilent: mute, silence, null, string.Empty);
    }
}
