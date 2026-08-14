using Xunit;

namespace AIOrchestratorCoreLib.Tests.Planning;

/// <summary>
/// THE LEDGER LEVER MUST REACH EVERY SESSION THAT OWNS A LEDGER.
///
/// The turn-end hook read `AIORCH_ROLE != "supervisor" -> exit 0`, so the single session of a BASIC
/// orchestration — which owns PLAN.md by its own role command, because there is no supervisor to own
/// it — was structurally exempt from the only lever that makes this artifact get maintained. It then
/// did what an unenforced protocol step gets done: the owner asked for six things across two hours
/// and the progress bar read 3/3 the entire time.
///
/// The owner's ruling is why this is a test and not an apology (2026-08-14): *"you are just a session
/// like any other. If you failed to upgrade the plan file any other future session also might fail.
/// Fix this permanently for any future orchestration or solo session."*
///
/// This is the same cross-language join `LedgerLegendTests` uses for the markers: the hook is bash
/// and cannot import anything, so reading the file is the only assertion available. It FAILS LOUDLY
/// when it cannot find the hook — a harness that cannot find what it tests must refuse to run rather
/// than certify the absence of the thing it is testing.
/// </summary>
public class LedgerEnforcementCoversTheSoloTests
{
    [Fact]
    public void TheHookGatesOnRoleAndAdmitsBothSupervisorAndSolo()
    {
        var text = Read_Hook();

        // The gate itself, as written. Asserted on the shape rather than on "contains solo", because
        // the word appears in the prose above it — and a comment mentioning solo while the gate still
        // rejects it is exactly the failure being fixed.
        Assert.Contains("supervisor|solo)", text);

        Assert.DoesNotContain("!= \"supervisor\"", text);
    }

    /// <summary>
    /// MEMBERS STAY OUT, and that is a decision rather than an omission: an implementer or a reviewer
    /// does not own the ledger and must not edit it, so blocking their turn would demand a write the
    /// protocol forbids. Pinned so that "make it cover everyone" cannot be applied one step too far.
    /// </summary>
    [Fact]
    public void MembersAreStillExempt()
    {
        var text = Read_Hook();

        Assert.DoesNotContain("implementer|", text);
        Assert.DoesNotContain("|reviewer)", text);
    }

    /// <summary>
    /// THE BLOCK MESSAGE MUST NOT ASSUME A VERDICT. It said "You posted a verdict to an implementer
    /// channel", which is impossible for a solo — it has no implementer channels — so the one session
    /// this change exists to reach would have been sent hunting for something that does not exist.
    /// The owner-request debt is the route that fires for a solo, and the text has to name it.
    /// </summary>
    [Fact]
    public void TheBlockMessageNamesBothDebtsAndTheRequestsTable()
    {
        var text = Read_Hook();

        Assert.Contains("the owner asked for something", text);
        Assert.Contains("OWNER REQUESTS", text);
        Assert.DoesNotContain("You posted a verdict to an implementer channel", text);
    }

    static string Read_Hook()
    {
        var folder = AppContext.BaseDirectory;

        for (var depth = 0; depth < 8; depth++)
        {
            var candidate = Path.Combine(folder, "kit", "hooks", "supervisor-ledger-check.sh");

            if (File.Exists(candidate))
                return File.ReadAllText(candidate);

            var parent = Directory.GetParent(folder);

            if (parent == null)
                break;

            folder = parent.FullName;
        }

        throw new Exception("could not locate kit/hooks/supervisor-ledger-check.sh — the harness is not reading the file it asserts about");
    }
}
