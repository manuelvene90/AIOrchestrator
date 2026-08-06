namespace AIOrchestratorCoreLib.Spawning.SessionSpawner;

public static class SessionSpawner_Factory
{
    public static ISessionSpawner Create()
    {
        return new SessionSpawnerModel();
    }
}
