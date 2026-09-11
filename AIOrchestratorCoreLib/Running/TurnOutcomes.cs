using AIOrchestratorCoreLib.Running.TurnResult;

namespace AIOrchestratorCoreLib.Running;

/// <summary>The three words a turn can end with, and the one rule that picks between them.</summary>
public static class TurnOutcomes
{
    public const string SUCCESS = "success";
    public const string ERROR = "error";
    public const string TIMEOUT = "timeout";

    public static bool Is_Success(ITurnResult result)
    {
        return !result.TimedOut && result.ExitCode == 0 && !result.IsError;
    }

    public static string Describe(ITurnResult result)
    {
        if (result.TimedOut)
            return TIMEOUT;

        return Is_Success(result) ? SUCCESS : ERROR;
    }
}
