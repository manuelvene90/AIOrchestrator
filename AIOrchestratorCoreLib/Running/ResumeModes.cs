namespace AIOrchestratorCoreLib.Running;

/// <summary>
/// What a session remembers across its own restarts, whichever runner drives it.
/// <see cref="Transcript"/>: a bridge-driven session <c>--resume</c>s its print session, so every
/// turn after the first keeps its context (an implementer mid-task); a terminal supervisor or solo
/// <c>--resume</c>s its own conversation when the probe names a transcript that still exists
/// (owner request 2026-09-10 — never <c>--continue</c>, which guesses the most recent conversation
/// in a repo directory several sessions share). <see cref="Fresh"/>: start empty, with only what
/// the session re-reads from disk — CLAUDE.md decision 8 made configuration: the general supervisor
/// is stateless across launches by owner directive, and a resumed conversation once re-ran a
/// failed request on boot.
///
/// <para>
/// IMPLEMENTERS AND REVIEWERS IN A TERMINAL ALWAYS START FRESH regardless of this setting: the
/// channel is their durable state by design (CLAUDE.md decision 8), so the launcher never hands
/// them a resume id and this mode only reaches them through a bridge-driven runner.
/// </para>
/// </summary>
public enum ResumeModes
{
    Transcript,
    Fresh,
}

public static class ResumeMode_Names
{
    public const string TRANSCRIPT = "transcript";
    public const string FRESH = "fresh";

    public static string Get_Word(ResumeModes mode)
    {
        return mode switch
        {
            ResumeModes.Transcript => TRANSCRIPT,
            ResumeModes.Fresh => FRESH,
            _ => throw new Exception($"Unhandled ResumeModes: {mode}"),
        };
    }

    public static ResumeModes? Parse_OrNull(string? word)
    {
        return word?.Trim().ToLowerInvariant() switch
        {
            TRANSCRIPT => ResumeModes.Transcript,
            FRESH => ResumeModes.Fresh,
            _ => null,
        };
    }
}
