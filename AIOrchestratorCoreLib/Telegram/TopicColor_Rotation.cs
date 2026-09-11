namespace AIOrchestratorCoreLib.Telegram;

/// <summary>
/// A COLOUR PER REPOSITORY FOR ITS TOPICS — brief F1.
///
/// <para>
/// Telegram lets <c>createForumTopic</c> name one of six <c>icon_color</c> values and shows it as
/// the dot beside the topic in the list. The owner runs several repositories at once and reads that
/// list on a phone; today every topic is the same default colour, so which endeavour a topic
/// belongs to is legible only by reading its name.
/// </para>
/// <para>
/// A ROTATION, NOT A HASH, and the difference is the whole design. A hash of the repo name is
/// stable and needs nothing stored — and with three repositories it can perfectly well give two of
/// them the same colour, which is precisely the confusion this removes. Handing out the next unused
/// colour guarantees that up to six repositories are all distinct; the seventh reuses the first,
/// which is Telegram's limit and not ours.
/// </para>
/// <para>
/// WHICH IS WHY IT IS PERSISTED. The rotation depends on what has already been handed out, and the
/// repo list is REORDERED at runtime (<see cref="Configuration.ConfigRepos_Reorderer"/>), so a
/// colour derived from a repo's position in the list would change under the owner every time they
/// dragged a row. The assignment is written next to the repo in config.json and read back; the
/// rotation only ever decides what a repo that has never had one gets.
/// </para>
/// </summary>
public static class TopicColor_Rotation
{
    /// <summary>
    /// Telegram's six permitted <c>icon_color</c> values [documented]. Any other number is refused,
    /// so this list is a closed set rather than a palette we chose — the ORDER is ours, and it is
    /// the order the colours are handed out in.
    /// </summary>
    public static readonly IReadOnlyList<int> PALETTE =
    [
        0x6FB9F0, // blue
        0xFFD67E, // yellow
        0xCB86DB, // violet
        0x8EEE98, // green
        0xFF93B2, // rose
        0xFB6F5F, // red
    ];

    /// <summary>
    /// The colour for a repository that has never had one, given the colours already in use.
    ///
    /// <para>
    /// The first unused colour, so up to six repositories are all different. Past six — or after
    /// repositories have been removed and added enough to leave the set fragmented — it falls back
    /// to position modulo six, which is the "seventh reuses the first" the brief asks for.
    /// </para>
    /// </summary>
    public static int Pick_ForNewRepo(IReadOnlyCollection<int> coloursAlreadyInUse)
    {
        foreach (var colour in PALETTE)
        {
            if (!coloursAlreadyInUse.Contains(colour))
                return colour;
        }

        return PALETTE[coloursAlreadyInUse.Count % PALETTE.Count];
    }

    /// <summary>
    /// Whether a number read off config.json is one Telegram will actually accept. A hand-edited or
    /// stale value must not reach <c>createForumTopic</c>, because Telegram refuses the whole call
    /// for it — and the cost of a refused topic creation is an orchestration whose entries mirror
    /// into General instead (the misdelivery <c>Resolve_ThreadId_OrNull_Async</c> warns about at
    /// length). An unrecognised colour is dropped and the topic is created with Telegram's default.
    /// </summary>
    public static bool Is_Permitted(int colour)
    {
        return PALETTE.Contains(colour);
    }
}
