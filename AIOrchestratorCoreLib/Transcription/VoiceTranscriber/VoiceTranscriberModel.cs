using System.Diagnostics;
using System.Text;
using AIOrchestratorCoreLib.Logging.OrchestrationLog;

namespace AIOrchestratorCoreLib.Transcription.VoiceTranscriber;

/// <summary>
/// Runs the configured command with {input} replaced by the audio file path (quoted), via
/// cmd /c so PATH and .cmd shims resolve. Stdout (trimmed) is the transcript.
/// </summary>
internal sealed class VoiceTranscriberModel(IOrchestrationLog log) : IVoiceTranscriber
{
    const int TRANSCRIBE_TIMEOUT_MILLISECONDS = 120_000;

    readonly IOrchestrationLog _log = log;

    public async Task<string?> Transcribe_OrNull_Async(string audioFilePath, string commandTemplate, CancellationToken cancellationToken)
    {
        try
        {
            var command = commandTemplate.Replace("{input}", $"\"{audioFilePath}\"");

            var startInfo = new ProcessStartInfo
            {
                FileName = "cmd.exe",
                Arguments = $"/c {command}",
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardOutputEncoding = new UTF8Encoding(false),
                StandardErrorEncoding = new UTF8Encoding(false),
            };

            using var process = Process.Start(startInfo)
                ?? throw new Exception($"Process.Start returned null for transcribe command: {command}");

            using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutSource.CancelAfter(TRANSCRIBE_TIMEOUT_MILLISECONDS);

            var outputTask = process.StandardOutput.ReadToEndAsync(timeoutSource.Token);
            var errorDrainTask = process.StandardError.ReadToEndAsync(timeoutSource.Token);

            try
            {
                await process.WaitForExitAsync(timeoutSource.Token);
            }
            catch (OperationCanceledException)
            {
                Kill_BestEffort(process);
                _log.Log_Warning("", "Voice transcription timed out");
                return null;
            }

            var output = (await outputTask).Trim();
            await errorDrainTask;

            if (process.ExitCode != 0 || output.Length == 0)
            {
                _log.Log_Warning("", $"Voice transcription failed (exit code {process.ExitCode}, output length {output.Length})");
                return null;
            }

            return output;
        }
        catch (Exception ex)
        {
            _log.Log_Warning("", $"Voice transcription failed: {ex.Message}");
            return null;
        }
    }

    static void Kill_BestEffort(Process process)
    {
        try
        {
            process.Kill(entireProcessTree: true);
        }
        catch
        {
            // Already exited.
        }
    }
}
