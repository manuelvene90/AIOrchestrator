namespace AIOrchestratorCoreLib.Running.TurnLiveness;

/// <summary>
/// THE LOOP DETECTOR FOR ONE TURN, with what does not change between polls kept between polls. The
/// brake asks every few seconds for each running member turn; without this each ask walked every
/// <c>projects/</c> folder to find the transcript again and re-parsed its last 512 KB even when not a
/// byte had been added (simplify review, 2026-09-11). The path is kept once found — a session's
/// transcript does not move during a turn — and the verdict is reused while the file's length and
/// last write are what they were when it was reached.
/// </summary>
internal sealed class TranscriptLoopWatch(string claudeHome, string sessionId)
{
    readonly string _claudeHome = claudeHome;
    readonly string _sessionId = sessionId;

    string? _transcriptPath;
    (long Length, DateTime LastWriteUtc, DateTime SinceUtc)? _lastLook;
    string? _lastVerdict;

    public string? Describe_Loop_OrNull(DateTime sinceUtc)
    {
        _transcriptPath ??= TranscriptActivity_Reader.Find_MainTranscript_OrNull(_claudeHome, _sessionId);

        if (_transcriptPath == null)
            return null;

        FileInfo file;

        try
        {
            file = new FileInfo(_transcriptPath);

            if (!file.Exists)
                return null;
        }
        catch
        {
            // The same safe direction as the detector itself: a file that cannot be looked at says nothing.
            return null;
        }

        var look = (file.Length, file.LastWriteTimeUtc, sinceUtc);

        if (_lastLook != look)
        {
            _lastVerdict = TranscriptLoop_Detector.Describe_Loop_OrNull(_transcriptPath, sinceUtc);
            _lastLook = look;
        }

        return _lastVerdict;
    }
}
