using AIOrchestratorCoreLib.Logging.OrchestrationLog;
using AIOrchestratorCoreLib.Sessions.OrchestrationSessionStore;
using AIOrchestratorCoreLib.SupervisionPaths;
using AIOrchestratorCoreLib.Telegram.TelegramSendBudget;
using AIOrchestratorCoreLib.Time.Clock;

namespace AIOrchestratorCoreLib.Bridge.SettingsMenu;

public static class SettingsMenu_Factory
{
    /// <summary>
    /// The menu over the state the engine restored. A restored live menu gets its edit-gap exemption back HERE
    /// (D7): the exemption is not persisted — the budget holds none across a restart — and without this the
    /// first taps after a restart would meet the thirty-second gap on the very message the owner is using.
    /// <paramref name="sendBudget"/> is null in file-only mode and on the test seams that hand in no budget.
    /// </summary>
    public static ISettingsMenu Create(
        ISupervisionPaths paths,
        IOrchestrationSessionStore store,
        IOrchestrationLog log,
        IClock clock,
        ITelegramSendBudget? sendBudget,
        ISettingsMenuState state)
    {
        if (state.LiveMenuMessageId is { } liveMenuMessageId)
            sendBudget?.Exempt_FromEditGap(liveMenuMessageId);

        return new SettingsMenuModel(paths, store, log, clock, sendBudget, state);
    }
}
