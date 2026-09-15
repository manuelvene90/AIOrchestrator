namespace AIOrchestratorCoreLib.Bridge.HeldAppendMemo;

public static class HeldAppendMemo_Factory
{
    public static IHeldAppendMemo Create()
    {
        return new HeldAppendMemoModel();
    }
}
