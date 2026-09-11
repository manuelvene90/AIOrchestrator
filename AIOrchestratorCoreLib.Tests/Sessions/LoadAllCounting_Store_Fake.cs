using AIOrchestratorCoreLib.Sessions;
using AIOrchestratorCoreLib.Sessions.OrchestrationSession;
using AIOrchestratorCoreLib.Sessions.OrchestrationSessionStore;

namespace AIOrchestratorCoreLib.Tests.Sessions;

/// <summary>
/// THE REAL STORE, WITH A TALLY ON <see cref="Load_All"/>. Everything else is passed straight through,
/// so the engine under test behaves exactly as it does in the app.
///
/// <para>
/// WHY NOT THE <c>TickIo_Counters</c> SEAM. That one counts SYSCALLS, and the store now remembers each
/// <c>session.json</c> it has parsed against the file's stamp — so twenty-four calls to
/// <c>Load_All</c> and one call to <c>Load_All</c> read the disk the same number of times and the
/// syscall count cannot tell them apart. The roster snapshot is about the CALLS: the folder
/// enumeration, the stat per orchestration and the list building that happen whether or not the parse
/// is remembered. Counting them needs a seam at the call, which is this.
/// </para>
/// </summary>
internal sealed class LoadAllCounting_Store_Fake(IOrchestrationSessionStore inner) : IOrchestrationSessionStore
{
    readonly IOrchestrationSessionStore _inner = inner;
    long _loadAllCalls;

    public long LoadAll_Calls => Interlocked.Read(ref _loadAllCalls);

    public void Reset_Count() => Interlocked.Exchange(ref _loadAllCalls, 0);

    public IReadOnlyList<IOrchestrationSession> Load_All()
    {
        Interlocked.Increment(ref _loadAllCalls);

        return _inner.Load_All();
    }

    public IOrchestrationSession Get_Session(string orchId) => _inner.Get_Session(orchId);

    public IOrchestrationSession? Get_Session_OrNull(string orchId) => _inner.Get_Session_OrNull(orchId);

    public IOrchestrationSession? Find_ByTelegramTopicId_OrNull(long topicId) => _inner.Find_ByTelegramTopicId_OrNull(topicId);

    public IOrchestrationSession Create_Orchestration(string orchId, string repoName, string repoPath) => _inner.Create_Orchestration(orchId, repoName, repoPath);

    public IOrchestrationSession Add_Implementer(string orchId) => _inner.Add_Implementer(orchId);

    public IOrchestrationSession Add_Member(string orchId, MemberKinds kind) => _inner.Add_Member(orchId, kind);

    public IOrchestrationSession Add_Member(string orchId, MemberKinds kind, string? model) => _inner.Add_Member(orchId, kind, model);

    public void Set_TelegramTopicId(string orchId, long topicId) => _inner.Set_TelegramTopicId(orchId, topicId);

    public void Set_StatusLineMessageId(string orchId, long messageId) => _inner.Set_StatusLineMessageId(orchId, messageId);

    public void Clear_StatusLineMessageId(string orchId) => _inner.Clear_StatusLineMessageId(orchId);

    public void Set_SupervisorPid(string orchId, int? pid) => _inner.Set_SupervisorPid(orchId, pid);

    public void Clear_Supervisor(string orchId) => _inner.Clear_Supervisor(orchId);

    public void Stamp_CommunicatorSpawned(string orchId) => _inner.Stamp_CommunicatorSpawned(orchId);

    public void Set_TelegramMode(string orchId, AIOrchestratorCoreLib.Telegram.TelegramDeliveryModes mode) => _inner.Set_TelegramMode(orchId, mode);

    public void Set_AwaitingTest(string orchId, bool awaitingTest) => _inner.Set_AwaitingTest(orchId, awaitingTest);

    public void Set_Done(string orchId, bool done) => _inner.Set_Done(orchId, done);

    public void Set_OwnerPresence(string orchId, AIOrchestratorCoreLib.Telegram.OwnerPresenceModes presence) => _inner.Set_OwnerPresence(orchId, presence);

    public void Set_MemberPid(string orchId, string memberId, int? pid) => _inner.Set_MemberPid(orchId, memberId, pid);

    public void Set_DisplayName(string orchId, string displayName) => _inner.Set_DisplayName(orchId, displayName);

    public void Set_SupervisorModelOverride(string orchId, string? model) => _inner.Set_SupervisorModelOverride(orchId, model);

    public void Set_ImplementerModelOverride(string orchId, string? model) => _inner.Set_ImplementerModelOverride(orchId, model);

    public void Mark_TopicDeletePending(string orchId) => _inner.Mark_TopicDeletePending(orchId);

    public void Mark_TopicDeleted(string orchId) => _inner.Mark_TopicDeleted(orchId);

    public void Mark_TopicDeleteFailureReported(string orchId) => _inner.Mark_TopicDeleteFailureReported(orchId);

    public void Close_Member(string orchId, string memberId) => _inner.Close_Member(orchId, memberId);

    public void Close_Orchestration(string orchId) => _inner.Close_Orchestration(orchId);
}
