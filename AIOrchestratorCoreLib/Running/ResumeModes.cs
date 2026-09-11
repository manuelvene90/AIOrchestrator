namespace AIOrchestratorCoreLib.Running;

/// <summary>
/// What a print-run session remembers between turns. <see cref="Transcript"/>: every turn after
/// the first is <c>--resume</c> of the same transcript, so the session keeps its context (an
/// implementer mid-task). <see cref="Fresh"/>: every turn is a brand-new session whose only memory
/// is what it re-reads from disk — CLAUDE.md decision 8 made configuration: the general supervisor
/// is stateless across launches by owner directive, and a resumed conversation once re-ran a
/// failed request on boot.
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
