using AIOrchestratorCoreLib.Bridge.SettingsMenu;
using AIOrchestratorCoreLib.Configuration.SettingsPresentation;
using AIOrchestratorCoreLib.Sessions.OrchestrationSessionStore;
using AIOrchestratorCoreLib.SupervisionPaths;
using AIOrchestratorCoreLib.Tests.TestSupport;
using Xunit;

namespace AIOrchestratorCoreLib.Tests.Bridge.SettingsMenu;

/// <summary>
/// WHAT THE /settings MENU HANDS THE ENGINE TO PERSIST (plan 04 Task 5, fix round 1 of d63cd76). A step is
/// persisted so a restart leaves no invisible trap (ruling P23) — but a LAPSED step is no trap at all, only
/// stale text on disk, and a HELD SECRET is the owner's web.token sitting in the engine-state file until the next
/// General message happened to clear it.
/// </summary>
public class SettingsMenuTests : IDisposable
{
    readonly string _tempRoot = Path.Combine(Path.GetTempPath(), $"aiorch-settings-menu-unit-{Guid.NewGuid():N}");
    readonly FixedClock_Fake _clock = new(new DateTime(2026, 9, 23, 12, 0, 0, DateTimeKind.Utc));

    public void Dispose()
    {
        TempTree.Delete_BestEffort(_tempRoot);
        GC.SuppressFinalize(this);
    }

    [Fact]
    public void ALapsedStep_AndAHeldSecret_AreNotPersisted_ButALiveStepIs()
    {
        const long TOPIC_ID = 4242;

        var now = _clock.UtcNow;
        var live = SettingsReplyStep_Factory.Create_Waiting(null, "phone.status.intervalMinutes", now.AddMinutes(3));
        var lapsed = SettingsReplyStep_Factory.Create_Restored(TOPIC_ID, "buttonExpiryMinutes", "90", now.AddSeconds(-1));
        var heldSecret = SettingsReplyStep_Factory.Create_Restored(TOPIC_ID + 1, SettingsSnapshot_Reader.MASKED_SECRET_PATH, "tok-SENTINEL", now.AddMinutes(3));
        var heldNumber = SettingsReplyStep_Factory.Create_Restored(TOPIC_ID + 2, "buttonExpiryMinutes", "90", now.AddMinutes(3));

        var menu = Build_Menu(SettingsMenuState_Factory.Create_Restored(null, [live, lapsed, heldSecret, heldNumber]));

        var persisted = menu.Read_PersistableSteps();

        Assert.Equal(2, persisted.Count);
        Assert.Contains(live, persisted);
        Assert.Contains(heldNumber, persisted);
    }

    /// <summary>A waiting secret step holds nothing secret yet — it is persisted like any other live step, so a restart does not drop the prompt.</summary>
    [Fact]
    public void AWaitingSecretStep_IsPersisted()
    {
        var waiting = SettingsReplyStep_Factory.Create_Waiting(null, SettingsSnapshot_Reader.MASKED_SECRET_PATH, _clock.UtcNow.AddMinutes(3));

        var menu = Build_Menu(SettingsMenuState_Factory.Create_Restored(null, [waiting]));

        Assert.Same(waiting, Assert.Single(menu.Read_PersistableSteps()));
    }

    ISettingsMenu Build_Menu(ISettingsMenuState state)
    {
        var paths = SupervisionPaths_Factory.Create(_tempRoot);

        return SettingsMenu_Factory.Create(paths, OrchestrationSessionStore_Factory.Create(paths), new RecordingLog_Fake(), _clock, null, state);
    }
}
