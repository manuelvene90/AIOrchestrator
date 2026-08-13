namespace AIOrchestratorCoreLib.Status.IdleMember;

sealed class IdleMemberModel : IIdleMember
{
    readonly string _memberId;
    readonly string _idleFor;

    internal IdleMemberModel(string memberId, string idleFor)
    {
        _memberId = memberId;
        _idleFor = idleFor;
    }

    public string MemberId => _memberId;

    public string IdleFor => _idleFor;
}
