using AIOrchestratorCoreLib.Mirroring;
using AIOrchestratorCoreLib.Sessions;
using AIOrchestratorCoreLib.Status;
using Xunit;

namespace AIOrchestratorCoreLib.Tests.Status;

/// <summary>
/// A BASIC ORCHESTRATION HAS NO SUPERVISOR, AND THE OWNER MUST NOT BE TOLD OTHERWISE.
///
/// Owner, 2026-08-14: *"the app didn't realize this is a 'solo' session and writes things like
/// ✓✓ · 🔴 Sup: turn ended without a reply — nudged, an answer is coming. It should be done in a way
/// that it knows it's solo, otherwise I get confused."*
///
/// Two surfaces said it, for two different reasons, and both are pinned here:
///   - the narration label was the LITERAL "🔴 Sup" at six sites;
///   - the periodic status printed an unconditional supervisor row, whose "idle — waiting" came from
///     probing a usage file that a basic orchestration never writes. Not merely a wrong name: the
///     ABSENCE of a session was being reported as that session being idle.
/// </summary>
public class ABasicOrchestrationIsNotNarratedAsASupervisorTests
{
    /// <summary>
    /// All three voices, asserted positively. A negative alone ("does not say Sup") is satisfied by
    /// the empty string and by every other label in the language.
    /// </summary>
    [Fact]
    public void EachVoiceHasItsOwnLabel()
    {
        Assert.Equal("🟠 Solo", SpeakerLabel_Formatter.Describe(isGeneral: false, isBasic: true));
        Assert.Equal("🔴 Sup", SpeakerLabel_Formatter.Describe(isGeneral: false, isBasic: false));
        Assert.Equal("🟡 Gen-Sup", SpeakerLabel_Formatter.Describe(isGeneral: true, isBasic: false));
    }

    /// <summary>
    /// The one the owner complained about, stated as the property rather than as the string: nothing
    /// the app says ABOUT a basic orchestration may name a supervisor.
    /// </summary>
    [Fact]
    public void ASoloIsNeverCalledASupervisor()
    {
        var label = SpeakerLabel_Formatter.Describe(isGeneral: false, isBasic: true);

        Assert.DoesNotContain("Sup", label, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Solo", label);
    }

    /// <summary>
    /// GENERAL WINS OVER BASIC. The General topic is neither an orchestration nor a solo, and it has
    /// no supervisor spawn stamp of its own — so a caller that answers `isBasic: true` for it (the
    /// honest answer to "was a supervisor spawned for this?") must still get the concierge's voice.
    /// </summary>
    [Fact]
    public void TheGeneralTopicKeepsItsOwnVoiceWhicheverWayBasicIsAnswered()
    {
        Assert.Equal(
            SpeakerLabel_Formatter.GENERAL,
            SpeakerLabel_Formatter.Describe(isGeneral: true, isBasic: true));

        Assert.Equal(
            SpeakerLabel_Formatter.GENERAL,
            SpeakerLabel_Formatter.Describe(isGeneral: true, isBasic: false));
    }

    /// <summary>
    /// The status roster: no supervisor row for a basic orchestration, and the member rows survive.
    /// Both halves matter — dropping the whole roster would satisfy "no supervisor row" too.
    /// </summary>
    [Fact]
    public void ABasicOrchestrationsStatusHasNoSupervisorRow()
    {
        var text = StatusRoster_Builder.Build(
            "orch-1: 3/3 done (100%)",
            isBasic: true,
            "idle — waiting",
            ["- solo-1: working now"]);

        Assert.DoesNotContain("supervisor", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("- solo-1: working now", text);
        Assert.Contains("orch-1: 3/3 done (100%)", text);
    }

    /// <summary>
    /// And a CREW still gets it, which is the half that stops the fix from being "print less". The
    /// supervisor row is the first row after the header there, because the owner reads top-down.
    /// </summary>
    [Fact]
    public void ACrewStillGetsItsSupervisorRowFirst()
    {
        var text = StatusRoster_Builder.Build(
            "orch-2: 1/4 done (25%)",
            isBasic: false,
            "working now — editing Foo.cs",
            ["- imp-1: working now", "- rev-1: closed"]);

        var lines = text.Split('\n');

        Assert.Equal("orch-2: 1/4 done (25%)", lines[0]);
        Assert.Equal("- supervisor: working now — editing Foo.cs", lines[1]);
        Assert.Equal("- imp-1: working now", lines[2]);
        Assert.Equal("- rev-1: closed", lines[3]);
    }

    /// <summary>
    /// THE SOURCE OF `isBasic`, pinned so the two surfaces cannot start answering it differently.
    /// It is the supervisor SPAWN STAMP, not a member-id scan — `OrchestrationShape` documents why
    /// the roster reads a promoted orchestration as basic for ever, and this is the input both the
    /// label and the roster take.
    /// </summary>
    [Fact]
    public void BasicIsDecidedByTheSupervisorSpawnStamp()
    {
        Assert.True(OrchestrationShape.Is_BasicOrchestration(null));
        Assert.False(OrchestrationShape.Is_BasicOrchestration(new DateTime(2026, 8, 14, 20, 0, 0, DateTimeKind.Utc)));
    }
}
