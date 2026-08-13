namespace AIOrchestratorCoreLib.Status.IdleMember;

public static class IdleMember_Factory
{
    public static IIdleMember Create(string memberId, string idleFor)
    {
        return new IdleMemberModel(memberId, idleFor);
    }
}
