namespace AIOrchestratorCoreLib.Mirroring;

/// <summary>
/// WHO IS TEXTING, when the app itself is the speaker: the system and the machine, in one label.
///
/// <para>
/// OWNER REQUEST, 2026-09-17, and the message that prompted it was not one of ours. Coolify texted
/// the owner *"Server 'localhost' high disk usage detected! Disk usage: 81%"* — a real alert about a
/// real machine, and unreadable, because nothing in it says WHICH machine. The owner's own systems
/// text them from several hosts, so a notice that does not name its source makes them go and look
/// before they can act on it. This app's app-authored notices had the same shape: "⚙ App: kit check
/// FAILED" tells the reader nothing about which of their machines has stopped starting sessions.
/// </para>
/// <para>
/// ONLY THE APP'S OWN VOICE CARRIES IT (the owner's choice between three options). An agent's
/// message already arrives inside its orchestration's Telegram topic, so the topic name answers
/// "whose is this" and a prefix on every line would be the repetition the mirror exists to avoid
/// (decision 14). The app is the one speaker with no topic of its own — it writes into General and
/// into every orchestration alike.
/// </para>
/// <para>
/// THE MACHINE NAME IS THE MACHINE'S NAME, not a setting. No config key and no environment variable:
/// the fix for an ugly label is to rename the host, which is the same answer the owner got for the
/// Coolify alert that started this — the server was called 'localhost' in Coolify's own settings, and
/// renaming it there fixes the message without touching a line of anyone's code. A second name for a
/// machine is a second thing that can be wrong, and the CLAUDE.md prudence rule against
/// invisible-to-the-session environment variables points the same way.
/// </para>
/// </summary>
public static class AppSource_Label
{
    /// <summary>The system. Not the plugin id and not the process name — the thing the owner calls it.</summary>
    public const string SYSTEM = "AIOrch";

    /// <summary>
    /// "AIOrch · orch-vps". Read live rather than cached: a hostname change should not need a
    /// restart to show up, and this is called once per mirrored app entry, which is rare.
    /// </summary>
    public static string Describe()
    {
        var machine = Environment.MachineName;

        // A HOST WITH NO NAME STILL GETS A SOURCE. Environment.MachineName is documented as
        // non-empty, but it is ambient and this label's whole job is to be readable, so an empty one
        // degrades to the system name alone rather than to "AIOrch · ".
        return string.IsNullOrWhiteSpace(machine) ? SYSTEM : $"{SYSTEM} · {machine}";
    }
}
