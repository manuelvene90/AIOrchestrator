namespace AIOrchestratorCoreLib.Sessions;

/// <summary>
/// Builds the seed header written into a fresh channel file. The seed is preamble (ignored by the
/// parser); the protocol itself lives in the /supervisor and /implementer role commands.
/// </summary>
public static class ChannelSeed_Builder
{
    public static string Build_ImplementerChannelSeed(string orchId, string memberId)
    {
        return
            $"# SUPERVISION CHANNEL — supervisor ⇄ {memberId} — orchestration '{orchId}'\n" +
            "\n" +
            "Append-only duplex channel. Entries start with `## [n] FROM supervisor|implementer — date — subject`.\n" +
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
