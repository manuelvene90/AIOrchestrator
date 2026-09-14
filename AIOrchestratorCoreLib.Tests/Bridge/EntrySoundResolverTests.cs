using AIOrchestratorCoreLib.Bridge;
using AIOrchestratorCoreLib.Channels;
using AIOrchestratorCoreLib.Channels.ChannelEntry;
using AIOrchestratorCoreLib.Telegram;
using Xunit;

namespace AIOrchestratorCoreLib.Tests.Bridge;

/// <summary>
/// WHETHER ONE MIRRORED ENTRY RINGS — <c>phone.appMessagesRing</c> (plan 03 Task 3, D7 answered (b)).
///
/// <para>
/// <c>true</c> is the phone this tree already had: whatever the supervisor (or the solo standing in for
/// one) says to the owner rings. <c>false</c> is D7 (b): that session's NARRATION arrives silently, and
/// only what <c>phone.push = filtered</c> would have sent at once still rings — a question, a
/// <c>BLOCKED ON OWNER</c>, a file for the owner, the boot greeting and THE answer. The predicate is
/// Task 2's, asked, never restated: if this class and <see cref="OwnerPush_Policy.Decide"/> ever
/// disagree about what a question is, one of them has grown a second copy of the rule (decision 12).
/// </para>
/// </summary>
public class EntrySoundResolverTests
{
    const string SUBJECT = "a report";

    [Theory]
    [InlineData(ChannelAuthors.Supervisor)]
    [InlineData(ChannelAuthors.Solo)]
    public void WhenAppMessagesRing_NarrationFromTheOwnersSession_Rings(ChannelAuthors author)
    {
        var entry = Entry(author, SUBJECT, "Two fixes landed; running the suite on the merged tree now.");

        Assert.Equal(TelegramSendSounds.Rings, EntrySound_Resolver.Resolve(entry, ownerIsWaitingForAReply: false, appMessagesRing: true));
    }

    [Theory]
    [InlineData(ChannelAuthors.Supervisor)]
    [InlineData(ChannelAuthors.Solo)]
    public void WhenAppMessagesAreSilent_NarrationFromTheOwnersSession_IsSilent(ChannelAuthors author)
    {
        var entry = Entry(author, SUBJECT, "Two fixes landed; running the suite on the merged tree now.");

        Assert.Equal(TelegramSendSounds.Silent, EntrySound_Resolver.Resolve(entry, ownerIsWaitingForAReply: false, appMessagesRing: false));
    }

    /// <summary>What D7 (b) keeps ringing under <c>false</c> — each row one member of the filtered arm's "send now".</summary>
    [Theory]
    [InlineData("a question", "Should the retry cap be three or five?")]
    [InlineData("a question", "QUESTION: which retry cap\nOPTION: three\nOPTION: five")]
    [InlineData("blocked", "BLOCKED ON OWNER: the deploy key is missing from the vault.")]
    [InlineData("a file", "The report you asked for.\nATTACH: C:\\repo\\report.csv")]
    [InlineData("a picture", "The mockup.\nIMAGE: C:\\repo\\mockup.png")]
    [InlineData("supervisor online — Repo", "")]
    public void WhenAppMessagesAreSilent_WhatFilteredWouldSendAtOnce_StillRings(string subject, string body)
    {
        var entry = Entry(ChannelAuthors.Supervisor, subject, body);

        Assert.Equal(TelegramSendSounds.Rings, EntrySound_Resolver.Resolve(entry, ownerIsWaitingForAReply: false, appMessagesRing: false));
    }

    [Fact]
    public void WhenAppMessagesAreSilent_TheAnswerTheOwnerIsWaitingFor_Rings()
    {
        var entry = Entry(ChannelAuthors.Supervisor, SUBJECT, "The rebuild is done.");

        Assert.Equal(TelegramSendSounds.Rings, EntrySound_Resolver.Resolve(entry, ownerIsWaitingForAReply: true, appMessagesRing: false));
    }

    /// <summary>
    /// A "WAITING ON …" SUBJECT IS A STATUS LINE, NEVER THE ANSWER — decision 25's rule, which the filtered
    /// arm already applies to the credit. Rung while the owner waits, it would ring for the line written
    /// seconds before the real answer.
    /// </summary>
    [Fact]
    public void WhenAppMessagesAreSilent_ATurnEndDeclaration_IsSilent_EvenWhileTheOwnerWaits()
    {
        var entry = Entry(ChannelAuthors.Supervisor, "WAITING ON the re-review - fix landed", "Task 6 fix round landed, review running.");

        Assert.Equal(TelegramSendSounds.Silent, EntrySound_Resolver.Resolve(entry, ownerIsWaitingForAReply: true, appMessagesRing: false));
    }

    /// <summary>The app, a member or a reviewer never rang, and neither setting makes them.</summary>
    [Theory]
    [InlineData(ChannelAuthors.App, true)]
    [InlineData(ChannelAuthors.App, false)]
    [InlineData(ChannelAuthors.Implementer, true)]
    [InlineData(ChannelAuthors.Reviewer, false)]
    public void AnAuthorWhoDoesNotSpeakToTheOwner_IsSilent_WhateverTheSetting(ChannelAuthors author, bool appMessagesRing)
    {
        var entry = Entry(author, SUBJECT, "Should the retry cap be three or five?\nBLOCKED ON OWNER: the deploy key.");

        Assert.Equal(TelegramSendSounds.Silent, EntrySound_Resolver.Resolve(entry, ownerIsWaitingForAReply: true, appMessagesRing));
    }

    static IChannelEntry Entry(ChannelAuthors author, string subject, string body)
    {
        var rawText = $"## [7] FROM {ChannelAuthor_Words.Get_Word(author)} — 2026-09-14 10:00 — {subject}\n{body}";

        return ChannelEntry_Factory.Create(7, author, "2026-09-14 10:00", subject, body, rawText);
    }
}
