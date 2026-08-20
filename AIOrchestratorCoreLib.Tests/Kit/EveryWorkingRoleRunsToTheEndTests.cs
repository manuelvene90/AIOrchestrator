using Xunit;

namespace AIOrchestratorCoreLib.Tests.Kit;

/// <summary>
/// THE DEFAULT IS NEVER TO STOP (owner directive, 2026-08-20).
///
/// They had to say it twice in one evening: *"you're stuck, you're not developing anything"*, and
/// then *"You need to do the entire endeavor all at once without ever stopping"*. The failure is
/// specific and it is invisible from the inside — a session finishes a phase, reports, and treats
/// the report as a turn boundary. Reporting FEELS like a stopping point, so nothing in the session
/// notices that it stopped; only the owner does, by watching silence.
///
/// Pinned across every role that WORKS, because all three did it. The general supervisor is left out
/// on purpose: it is a concierge whose turns really do end when it has answered.
/// </summary>
public class EveryWorkingRoleRunsToTheEndTests
{
    static readonly string[] WORKING_ROLES = ["solo.md", "supervisor.md", "implementer.md"];

    [Fact]
    public void EveryWorkingRoleIsToldToCarryOnPastItsOwnReport()
    {
        foreach (var fileName in WORKING_ROLES)
        {
            var text = Read_RoleCommand(fileName);

            Assert.True(
                text.Contains("RUN TO THE END"),
                $"{fileName} never tells the session to run to the end — it will report and stop, and the owner will have to notice");

            // The load-bearing half: a report is not a boundary. Without this the rule reads as
            // "work hard", which every session already believes it is doing.
            Assert.Contains("report AND CARRY ON", text);
        }
    }

    /// <summary>
    /// The three exits are NAMED. A rule that says "never stop" without saying when you may would be
    /// read as "never ask", and a session that cannot ask when it is genuinely blocked is worse than
    /// one that stops too often.
    /// </summary>
    [Fact]
    public void TheThreeReasonsToStopAreStated()
    {
        foreach (var fileName in WORKING_ROLES)
        {
            var text = Read_RoleCommand(fileName);

            Assert.Contains("BLOCKS the whole endeavour", text);
            Assert.Contains("step-by-step", text);
            Assert.Contains("told you to stop", text);
        }
    }

    static string Read_RoleCommand(string fileName)
    {
        var folder = AppContext.BaseDirectory;

        for (var depth = 0; depth < 8; depth++)
        {
            var candidate = Path.Combine(folder, "kit", "commands", fileName);

            if (File.Exists(candidate))
                return File.ReadAllText(candidate);

            var parent = Directory.GetParent(folder);

            if (parent == null)
                break;

            folder = parent.FullName;
        }

        Assert.Fail($"kit/commands/{fileName} was not found walking up from {AppContext.BaseDirectory}");

        return "";
    }
}
