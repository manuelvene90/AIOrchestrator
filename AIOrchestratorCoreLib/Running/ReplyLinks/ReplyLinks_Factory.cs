namespace AIOrchestratorCoreLib.Running.ReplyLinks;

public static class ReplyLinks_Factory
{
    public static IReplyLinks Create_InMemory()
    {
        return new ReplyLinksModel();
    }
}
