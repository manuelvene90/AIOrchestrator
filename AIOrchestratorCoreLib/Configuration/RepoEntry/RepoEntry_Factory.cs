namespace AIOrchestratorCoreLib.Configuration.RepoEntry;

public static class RepoEntry_Factory
{
    public static IRepoEntry Create(string name, string path)
    {
        return Create(name, path, topicColor: null);
    }

    /// <summary>
    /// <paramref name="topicColor"/> is validated rather than trusted: config.json is hand-edited
    /// and agent-edited, and Telegram refuses <c>createForumTopic</c> outright for a colour outside
    /// its six — which would cost the orchestration its topic, not just its dot. An unrecognised
    /// value is dropped to null, so the topic is created in Telegram's default colour instead.
    /// </summary>
    public static IRepoEntry Create(string name, string path, int? topicColor)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException($"Repo name must be non-empty (path was '{path}')");
        if (string.IsNullOrWhiteSpace(path))
            throw new ArgumentException($"Repo path must be non-empty (name was '{name}')");

        var permitted = topicColor != null && Telegram.TopicColor_Rotation.Is_Permitted(topicColor.Value)
            ? topicColor
            : null;

        return new RepoEntryModel(name, path, permitted);
    }
}
