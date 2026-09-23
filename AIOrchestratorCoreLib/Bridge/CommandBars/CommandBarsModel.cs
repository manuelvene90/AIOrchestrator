using AIOrchestratorCoreLib.Configuration.PulseSettings;
using AIOrchestratorCoreLib.Logging.OrchestrationLog;
using AIOrchestratorCoreLib.Telegram;

namespace AIOrchestratorCoreLib.Bridge.CommandBars;

/// <inheritdoc cref="ICommandBars"/>
internal sealed class CommandBarsModel(IOrchestrationLog log) : ICommandBars
{
    /// <summary>
    /// Two buttons per row. Six commands stacked one-per-row — the shape every other keyboard here
    /// uses — would put a slab of buttons under the one message in the topic the owner reads all day.
    /// </summary>
    const int BUTTONS_PER_ROW = 2;

    /// <summary>
    /// THREAD ID ZERO IS DELIBERATE and is what the parser round-trips for General. General is not a
    /// topic, so there is no thread to name; the tap handler reads a zero as "use the tap's own thread",
    /// which in General is null, which every command already treats as General. A sentinel would be a
    /// second spelling of the same nothing.
    /// </summary>
    const long GENERAL_THREAD_ID = 0;

    /// <summary>
    /// The machine-wide log, not an orchestration's: all three keys are machine-scoped, so there is no one
    /// orchestration a refusal belongs to.
    /// </summary>
    const string MACHINE_LOG_SCOPE = "";

    readonly IOrchestrationLog _log = log;

    // ITS OWN LOCK: the status-line loop and the inbound loop both reach here — the bar from one, the
    // receipt's placement from the other.
    readonly Lock _gate = new();

    /// <summary>
    /// ONCE PER DISTINCT LINE, the rule <c>PrintTurnDispatcherModel.Report_ConfigRejections_Once</c> argues.
    /// Every bar is rebuilt on every status tick, so a refusal said per build would bury the log it is
    /// written into (decision 14); a flag set at the first build would swallow the refusal earned by an
    /// edit made while the app runs. D10's line never changes, so it is said once per process — one
    /// engine per host — exactly as the plan asks.
    /// </summary>
    readonly HashSet<string> _said = new(StringComparer.Ordinal);

    public IReadOnlyList<IReadOnlyList<(string Data, string Label)>> Build_TopicRows(
        IPulseSettings pulse, ReceiptStyles receipts, long messageThreadId, bool isHolding, int heldCount)
    {
        Say_Once_IfAny(TopicCommandButtons.Describe_VerbsWithoutTapRoute_OrNull(PulseSettings_Json.BUTTONS_PATH, pulse.Buttons));

        return Chunk_IntoRows(TopicCommandButtons.Build_ForTopic(
            pulse.Buttons, messageThreadId, isHolding, heldCount, Is_HoldToggleOnTheBar(pulse, receipts)));
    }

    public IReadOnlyList<IReadOnlyList<(string Data, string Label)>> Build_GeneralRows(IPulseSettings pulse)
    {
        Say_Once_IfAny(TopicCommandButtons.Describe_VerbsWithoutTapRoute_OrNull(PulseSettings_Json.GENERAL_BUTTONS_PATH, pulse.GeneralButtons));

        return Chunk_IntoRows(TopicCommandButtons.Build_ForGeneral(pulse.GeneralButtons, GENERAL_THREAD_ID));
    }

    public bool Is_HoldToggleOnTheBar(IPulseSettings pulse, ReceiptStyles receipts)
    {
        var (onTheBar, isFallback) = HoldTogglePlacement_Resolver.Resolve(pulse.HoldToggle, receipts);

        if (isFallback)
            Say_Once_IfAny(HoldTogglePlacement_Resolver.FALLBACK_WARNING);

        return onTheBar;
    }

    void Say_Once_IfAny(string? line)
    {
        if (line == null)
            return;

        lock (_gate)
        {
            if (!_said.Add(line))
                return;
        }

        // Outside the lock: a log sink is I/O, and nothing here needs it serialised beyond the set.
        _log.Log_Warning(MACHINE_LOG_SCOPE, line);
    }

    /// <summary>
    /// ONE chunker for both bars. The loop existed once per caller for as long as there was one caller; a
    /// second copy of it is how the two bars come to wrap differently for no reason anybody decided.
    /// </summary>
    static IReadOnlyList<IReadOnlyList<(string Data, string Label)>> Chunk_IntoRows(IReadOnlyList<(string Data, string Label)> buttons)
    {
        List<IReadOnlyList<(string Data, string Label)>> rows = [];

        for (var index = 0; index < buttons.Count; index += BUTTONS_PER_ROW)
            rows.Add([.. buttons.Skip(index).Take(BUTTONS_PER_ROW)]);

        return rows;
    }
}
