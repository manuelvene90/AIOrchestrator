namespace AIOrchestratorCoreLib.SupervisionPaths;

public static class SupervisionPaths_Factory
{
    public static ISupervisionPaths Create(string root)
    {
        if (string.IsNullOrWhiteSpace(root))
            throw new ArgumentException($"Supervision root must be a non-empty path, got '{root}'");

        return new SupervisionPathsModel(root);
    }

    public static ISupervisionPaths Create_Default()
    {
        var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return Create(Path.Combine(userProfile, ".claude", "supervision"));
    }
}
