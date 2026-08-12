namespace AIOrchestratorCoreLib.Sessions;

/// <summary>
/// Builds the seed header written into a fresh channel file. The seed is preamble (ignored by the
/// parser); the protocol itself lives in the /supervisor and /implementer role commands.
/// </summary>
public static class ChannelSeed_Builder
{
    /// <summary>
    /// Used for EVERY member channel, reviewers included — so the author word it names must match the
    /// member it is for. It said `supervisor|implementer` in a reviewer's channel, telling that
    /// reviewer to sign entries as an implementer.
    ///
    /// Nothing has gone wrong yet: reviewers write `FROM reviewer` because their role command says
    /// so, and zero occurrences turned up across 17 reviewer channels. But the author word is not
    /// cosmetic any more — window markers are read only from member-authored entries, and a reviewer
    /// is specifically barred from that state — so a file that instructs the wrong word is a defect
    /// waiting for a session that trusts the file it was handed over the command it was given.
    /// </summary>
    public static string Build_ImplementerChannelSeed(string orchId, string memberId)
    {
        var authorWord = MemberKind_Ids.Resolve_Kind(memberId) == MemberKinds.Reviewer ? "reviewer" : "implementer";

        return
            $"# SUPERVISION CHANNEL — supervisor ⇄ {memberId} — orchestration '{orchId}'\n" +
            "\n" +
            $"Append-only duplex channel. Entries start with `## [n] FROM supervisor|{authorWord} — date — subject`.\n" +
            "Protocol rules live in the /supervisor and /implementer role commands. Never edit past entries.\n" +
            "\n" +
            "---\n";
    }

    public static string Build_GeneralChannelSeed()
    {
        return
            "# GENERAL SUPERVISOR CHANNEL — owner ⇄ general supervisor\n" +
            "\n" +
            "Append-only duplex channel, mirrored to the supergroup's General topic.\n" +
            "Entries start with `## [n] FROM supervisor|owner|app — date — subject`.\n" +
            "The general supervisor starts orchestrations by dropping request files in .requests/;\n" +
            "the app confirms with FROM app entries here. Never edit past entries.\n" +
            "\n" +
            "---\n";
    }

    public static string Build_OwnerChannelSeed(string orchId)
    {
        return
            $"# OWNER CHANNEL — owner ⇄ supervisor — orchestration '{orchId}'\n" +
            "\n" +
            "Append-only duplex channel. Entries start with `## [n] FROM supervisor|owner — date — subject`.\n" +
            "Owner messages arrive here from Telegram (appended by the bridge) or are typed directly.\n" +
            "Supervisor entries here are mirrored to the owner's Telegram topic. Never edit past entries.\n" +
            "\n" +
            "---\n";
    }
}
