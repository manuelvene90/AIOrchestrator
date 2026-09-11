namespace AIOrchestratorCoreLib.Configuration.RepoEntry;

/// <summary>One repository the orchestrator can start a supervision session on.</summary>
public interface IRepoEntry
{
    string Name { get; }
    string Path { get; }

    /// <summary>
    /// The Telegram <c>icon_color</c> this repository's topics are created with, or null when it has
    /// never had one (every repo entry written before brief F1, and every one added since by hand).
    ///
    /// <para>
    /// PERSISTED rather than derived. See <see cref="Telegram.TopicColor_Rotation"/>: the rotation
    /// depends on what has already been handed out, and the repo list is reordered at runtime — so
    /// a colour computed from a position would change under the owner every time they dragged a row.
    /// </para>
    /// </summary>
    int? TopicColor { get; }
}
