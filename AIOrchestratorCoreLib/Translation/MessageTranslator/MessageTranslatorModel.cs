using System.Diagnostics;
using System.Text;
using AIOrchestratorCoreLib.Logging.OrchestrationLog;

namespace AIOrchestratorCoreLib.Translation.MessageTranslator;

/// <summary>
/// Shells out to the Claude Code CLI headless (`claude -p --model haiku`) — no API key needed, it
/// rides the owner's plan like every session does. Instruction AND content travel via stdin so no
/// text ever needs command-line escaping. Any failure, timeout, or empty output falls back to the
/// original text — a broken translator must never block Telegram traffic.
/// </summary>
internal sealed class MessageTranslatorModel(IOrchestrationLog log) : IMessageTranslator
{
    /// <summary>Inbound IT→EN: haiku suffices (already-English text just passes through).</summary>
    const string TO_ENGLISH_MODEL = "haiku";

    /// <summary>Outbound EN→IT is owner-facing and quality-critical — haiku repeatedly returned English or mixed prose.</summary>
    const string TO_ITALIAN_MODEL = "sonnet";

    const int TRANSLATION_TIMEOUT_MILLISECONDS = 45_000;

    const string TO_ENGLISH_INSTRUCTION =
        "You are a translation filter. Translate the message below to English. "
        + "If it is already entirely in English, return it UNCHANGED. "
        + "Preserve formatting, line breaks, bullets and emoji exactly. "
        + "Preserve any line starting with 'IMAGE:' exactly as written. "
        + "Reply with ONLY the translated text - no preamble, no quotes, no commentary.";

    const string TO_ITALIAN_INSTRUCTION =
        "You are a translation filter. Translate the message below to Italian. "
        + "Translate ALL the prose - never leave whole sentences in English. "
        + "But keep the technical TERMS in English exactly as written, the way Italian professionals use them: "
        + "financial/trading terms (Long, Short, spread, hedge ratio, backtest, drawdown), "
        + "programming terms (commit, merge, branch, worktree, build, test, bug, refactor, deploy, review, grep), "
        + "hardware terms, product names, code identifiers, file paths and error messages. "
        + "Preserve formatting, line breaks, bullets and emoji exactly. "
        + "Reply with ONLY the translated text - no preamble, no quotes, no commentary.";

    readonly IOrchestrationLog _log = log;

    public async Task<string> Translate_ToEnglish_Async(string text, CancellationToken cancellationToken)
    {
        return await Translate_Async(TO_ENGLISH_INSTRUCTION, text, TO_ENGLISH_MODEL, cancellationToken);
    }

    public async Task<string> Translate_ToItalian_Async(string text, CancellationToken cancellationToken)
    {
        var translated = await Translate_Async(TO_ITALIAN_INSTRUCTION, text, TO_ITALIAN_MODEL, cancellationToken);

        // Unchanged output on this direction means the model refused/failed silently — make it
        // VISIBLE in the log so untranslated owner texts can be diagnosed instead of shrugged at.
        if (translated == text && text.Trim().Length > 0)
            _log.Log_Warning("", $"EN→IT translation returned the text unchanged ({text.Length} chars)");

        return translated;
    }

    async Task<string> Translate_Async(string instruction, string text, string model, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(text))
            return text;

        try
        {
            using var process = Process.Start(Build_StartInfo(model))
                ?? throw new Exception("Process.Start returned null for the claude CLI");

            using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutSource.CancelAfter(TRANSLATION_TIMEOUT_MILLISECONDS);

            await process.StandardInput.WriteAsync($"{instruction}\n\nMESSAGE:\n{text}");
            process.StandardInput.Close();

            var outputTask = process.StandardOutput.ReadToEndAsync(timeoutSource.Token);
            var errorDrainTask = process.StandardError.ReadToEndAsync(timeoutSource.Token);

            try
            {
                await process.WaitForExitAsync(timeoutSource.Token);
            }
            catch (OperationCanceledException)
            {
                Kill_BestEffort(process);
                _log.Log_Warning("", "Translation timed out — using the original text");
                return text;
            }

            var output = (await outputTask).Trim();
            await errorDrainTask;

            if (process.ExitCode != 0 || output.Length == 0)
            {
                _log.Log_Warning("", $"Translation call failed (exit code {process.ExitCode}, output length {output.Length}) — using the original text");
                return text;
            }

            return output;
        }
        catch (Exception ex)
        {
            _log.Log_Warning("", $"Translation failed — using the original text: {ex.Message}");
            return text;
        }
    }

    /// <summary>cmd /c resolves claude through PATH (including .cmd shims) — the WPF app has no shell of its own.</summary>
    static ProcessStartInfo Build_StartInfo(string model)
    {
        return new ProcessStartInfo
        {
            FileName = "cmd.exe",
            Arguments = $"/c claude -p --model {model}",
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardInputEncoding = new UTF8Encoding(false),
            StandardOutputEncoding = new UTF8Encoding(false),
            StandardErrorEncoding = new UTF8Encoding(false),
        };
    }

    static void Kill_BestEffort(Process process)
    {
        try
        {
            process.Kill(entireProcessTree: true);
        }
        catch
        {
            // Already exited between the timeout and the kill — nothing to do.
        }
    }
}
