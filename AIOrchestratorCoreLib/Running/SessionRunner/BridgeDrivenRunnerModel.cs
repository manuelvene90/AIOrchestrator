using AIOrchestratorCoreLib.Channels;
using AIOrchestratorCoreLib.Logging.OrchestrationLog;
using AIOrchestratorCoreLib.Running.PrintSessionState;
using AIOrchestratorCoreLib.Running.SessionLaunch;
using AIOrchestratorCoreLib.Running.TurnCursor;
using AIOrchestratorCoreLib.Running.TurnSource;
using AIOrchestratorCoreLib.Sessions.OrchestrationSessionStore;
using AIOrchestratorCoreLib.SupervisionPaths;
using AIOrchestratorCoreLib.Usage;

namespace AIOrchestratorCoreLib.Running.SessionRunner;

/// <summary>
/// Starting a bridge-driven session starts NO PROCESS HERE. It writes the session's state file — a
/// fresh session id, the role, the cwd, a baselined cursor for each channel it is woken by — and from
/// then on the turn dispatcher owns it: for <see cref="SessionRunners.Print"/> every inbound entry
/// becomes one <c>claude -p</c> invocation, for <see cref="SessionRunners.Stream"/> one living process
/// is fed the entries on its stdin. A restart finds the file already there and keeps it: the session id
/// is the transcript, and keeping it IS the resume.
///
/// One model for both because the registration is genuinely identical — the difference between the
/// two runners is entirely in how a turn reaches the model, which is
/// <see cref="TurnExecutor.ITurnExecutor"/>'s business and not this one's.
///
/// <para>
/// REGISTRATION IS WHERE HISTORY STOPS AND TRAFFIC STARTS. The channels are baselined here rather than
/// on the first tick, and the difference is not cosmetic: between the two there is a window of one
/// mirror tick in which anything appended would be absorbed as history instead of woken on. Taking the
/// baseline at the instant the session comes into existence leaves no such window — everything written
/// after this line is traffic, by construction.
/// </para>
/// </summary>
internal sealed class BridgeDrivenRunnerModel(SessionRunners kind, ISupervisionPaths paths, IOrchestrationSessionStore store, IOrchestrationLog log) : ISessionRunner
{
    readonly ISupervisionPaths _paths = paths;
    readonly IOrchestrationSessionStore _store = store;
    readonly IOrchestrationLog _log = log;

    public SessionRunners Kind { get; } = kind;

    public void Start(ISessionLaunch launch)
    {
        var word = SessionRunner_Names.Get_Word(Kind);
        var stateFile = PrintSessionState_Store.Get_StateFile(_paths, launch.Role, launch.OrchId, launch.MemberId);
        var existing = PrintSessionState_Store.Read_OrNull(stateFile);
        var sources = TurnSources_Resolver.Resolve(_paths, _store, launch.Role, launch.OrchId, launch.MemberId);

        if (existing != null)
        {
            _log.Log_Info(launch.OrchId, $"{launch.Role} '{launch.MemberId}' is {word}-run — already registered (session {existing.SessionId}, {existing.ExecutedTurns.Count} turn(s) executed); its transcript resumes on the next inbound entry");

            // THE MODEL AND THE REPO ARE RE-READ, because config.json can change under a running session.
            // A terminal session picks up a new `implementerModel` at its next respawn; a bridge-driven one
            // used to keep whatever was captured the first time, for the life of the state file, silently —
            // so an owner who switched a role to a cheaper model watched it go on billing the old one with
            // nothing anywhere saying why. The cursors and the transcript are untouched: this is the launch
            // configuration, not the session's memory.
            if (existing.Model != launch.Model || existing.WorkingDirectory != launch.WorkingDirectory)
            {
                _log.Log_Info(launch.OrchId, $"'{launch.MemberId}' was registered with model '{existing.Model ?? "(default)"}' in '{existing.WorkingDirectory}' and is now configured model '{launch.Model ?? "(default)"}' in '{launch.WorkingDirectory}' — updated for its next turn");
                PrintSessionState_Store.Write(stateFile, PrintSessionState_Factory.CreateFrom_Existing_Relaunched(existing, launch.WorkingDirectory, launch.Model));
            }

            Describe_Sources(launch, sources);
            return;
        }

        List<ITurnCursor> cursors = [];
        List<string> absorbed = [];

        foreach (var source in sources)
        {
            var cursor = TurnCursor_Factory.Create_Baseline(source, launch.Role, ChannelEntry_Parser.Parse_All(UsageTotals_Reader.Read_Text_Safe(source.ChannelFilePath)));

            cursors.Add(cursor);

            if (cursor.Delivered.Count > 0)
                absorbed.Add($"{source.Key}: {cursor.Delivered.Count}");
        }

        var state = PrintSessionState_Factory.Create_New(
            Guid.NewGuid().ToString(),
            launch.Role,
            launch.OrchId,
            launch.MemberId,
            launch.WorkingDirectory,
            launch.Model,
            TurnSources_Resolver.Resolve_Own(_paths, launch.Role, launch.OrchId, launch.MemberId).ChannelFilePath,
            cursors);

        PrintSessionState_Store.Write(stateFile, state);

        _log.Log_Info(launch.OrchId, $"{launch.Role} '{launch.MemberId}' is {word}-run — registered (session {state.SessionId}); no window, no process until its first inbound entry");
        Describe_Sources(launch, sources);

        // SAID, BECAUSE IT IS A HOLE AND NOT A DUPLICATE. Registering on a channel that already holds
        // unanswered traffic absorbs it: those entries will never start a turn. It is the right default
        // — the alternative hands a session a day of backlog as its first message — but it is a loss,
        // and BridgeState_Store paid for the lesson that an unstated one reads as nothing having
        // happened. Ordinarily this list is empty: a spoke is created empty and an orchestration's owner
        // channel is empty when its supervisor is registered.
        if (absorbed.Count > 0)
            _log.Log_Warning(launch.OrchId, $"'{launch.MemberId}' was registered on channels that already held inbound entries ({string.Join("; ", absorbed)}) — they are treated as HISTORY and will NOT start a turn; the session can still read the files");
    }

    /// <summary>
    /// Said EVERY time the session is registered, not once per app life: which channels wake this
    /// session is the one thing about a bridge-driven role its owner has to know, and a line that
    /// appears only on the very first registration is a line nobody reads.
    /// </summary>
    void Describe_Sources(ISessionLaunch launch, IReadOnlyList<ITurnSource> sources)
    {
        _log.Log_Info(launch.OrchId, $"'{launch.MemberId}' is {SessionRunner_Names.Get_Word(Kind)}-run and is woken by {sources.Count} channel(s): {string.Join(", ", sources.Select(source => source.Key))}");
    }
}
