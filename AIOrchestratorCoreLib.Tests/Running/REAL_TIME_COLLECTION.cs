using Xunit;

namespace AIOrchestratorCoreLib.Tests.Running;

/// <summary>
/// THE PRINT-RUNNER CLASSES THAT MEASURE REAL TIME, run alone.
///
/// <para>
/// <c>PrintRunnerTestHarness.Drive_Until</c> polls a wall-clock deadline while every tick spawns a
/// fake-CLI process, so what these classes observe depends on how much else the box is doing. The
/// convention note already says so and prescribes practice ("one suite at a time"); this is the half
/// that can be enforced in code — a loaded box is not only somebody else's suite, it is also this
/// one's other 3,400 tests running beside these.
/// </para>
/// <para>
/// WHAT IT LOOKS LIKE, MEASURED ON WINDOWS 2026-09-11/12 across ten runs of the print-runner filter:
/// never a wrong answer, always an EXTRA one — "Assert.Empty … Collection was not empty" with one
/// executed turn in it, "Assert.Single … contained 2 matching items" with two app entries. Between
/// two of Drive_Until's 100 ms polls a slow box lets a further turn complete, and the assertion after
/// the loop describes a system that has moved on. Each of the four classes below passes alone,
/// repeatedly, in a fraction of its 60-second budget.
/// </para>
/// <para>
/// NOT A BUDGET INCREASE, deliberately: raising the deadline only makes a genuine failure take
/// longer to arrive, and it would not touch these at all — the failures are overshoots, not timeouts.
/// And not an assertion loosened either, which is the other way this red could have been made to go
/// away: "exactly one turn ran" is the claim, and a test that accepted two would be measuring
/// nothing.
/// </para>
/// </summary>
[CollectionDefinition(NAME, DisableParallelization = true)]
public class REAL_TIME_COLLECTION
{
    public const string NAME = "real-time";
}
