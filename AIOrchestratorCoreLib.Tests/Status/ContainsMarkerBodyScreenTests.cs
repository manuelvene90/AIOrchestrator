using AIOrchestratorCoreLib.Channels;
using AIOrchestratorCoreLib.Channels.ChannelEntry;
using AIOrchestratorCoreLib.Status;
using Xunit;
using Xunit.Abstractions;

namespace AIOrchestratorCoreLib.Tests.Status;

/// <summary>
/// THE CHEAP SCREEN IN <see cref="MemberState_Resolver.Contains_Marker"/>, and the two things that
/// have to be true of it at once: it changes NO answer, and it does the work it was added to remove.
///
/// <para>
/// WHAT IT IS FOR. <c>Open_RerouteContracts</c> walks EVERY entry of every implementer channel on
/// every tick and asks this function about each supervisor entry; it stops early only when a
/// declaration is found, so an orchestration where no <c>REROUTE:</c> was ever written pays the walk
/// in full, for ever. The function's body rule split the whole body into lines before it could say
/// no — an array plus a string per line, per entry, per channel, every two seconds.
/// </para>
/// <para>
/// THE MEASUREMENT IS DIFFERENTIAL, NOT A THRESHOLD (CLAUDE.md decision 20: no assertion that can
/// pass for two reasons). Two corpora of the SAME shape and the same answer — <c>false</c> — differ
/// only in whether the marker word appears in the body at all. The one that carries it, mid-sentence
/// where it declares nothing, still reaches the line split; the one that does not is turned away by
/// the screen. Comparing the two rules out "the machine was fast today" and, more importantly, goes
/// red the moment the screen is deleted: without it the two corpora do identical work.
/// </para>
/// </summary>
public class ContainsMarkerBodyScreenTests(ITestOutputHelper output)
{
    const string MARKER = "REROUTE:";

    static IChannelEntry Build_Entry(string subject, string body)
    {
        return ChannelEntry_Factory.Create(7, ChannelAuthors.Supervisor, "2026-09-17", subject, body, $"## [7] FROM supervisor — 2026-09-17 — {subject}\n{body}");
    }

    /// <summary>
    /// EVERY WAY THE ONE MATCHER SAYS YES STILL GETS THROUGH. Each of these is a rule the matcher's
    /// own docstring says was paid for by a live failure, and each is a way a substring screen written
    /// carelessly would silently stop finding declarations: the wrong case (the screen must be
    /// OrdinalIgnoreCase, as the rule it guards is), markdown decoration, an emoji in front, an
    /// indent, and the subject branch — which the screen must not stand in front of at all, because a
    /// subject can declare a marker that the body never mentions.
    /// </summary>
    [Theory]
    [InlineData("", "REROUTE: rev-1 from abc1234")]
    [InlineData("", "reroute: rev-1 from abc1234")]
    [InlineData("", "**REROUTE:** rev-1 from abc1234")]
    [InlineData("", "🚩 REROUTE: rev-1 from abc1234")]
    [InlineData("", "   REROUTE: rev-1 from abc1234")]
    [InlineData("", "the verdict stands.\n\nREROUTE: rev-1 from abc1234")]
    [InlineData("F1 and F3 again — REROUTE: rev-1 from abc1234", "no body mention of the word at all")]
    public void ADeclarationTheMatcherAcceptsIsStillAccepted(string subject, string body)
    {
        Assert.True(
            MemberState_Resolver.Contains_Marker(Build_Entry(subject, body), MARKER),
            $"the screen turned away a declaration the matcher accepts — subject '{subject}', body '{body}'");
    }

    /// <summary>
    /// AND EVERY WAY IT SAYS NO IS STILL NO. The point of the screen is that it refuses only entries
    /// the matcher would refuse anyway, so the refusals have to be pinned beside the acceptances —
    /// otherwise a screen that answered <c>true</c> for everything would pass the case above.
    /// </summary>
    [Theory]
    [InlineData("", "no marker anywhere in this entry")]
    [InlineData("", "we should REROUTE: this one later")]
    [InlineData("", "> REROUTE: rev-1 from abc1234")]
    [InlineData("", "REROUTEING: is not the word")]
    public void WhatTheMatcherRefusesIsStillRefused(string subject, string body)
    {
        Assert.False(
            MemberState_Resolver.Contains_Marker(Build_Entry(subject, body), MARKER),
            $"the entry declares nothing but was accepted — subject '{subject}', body '{body}'");
    }

    /// <summary>
    /// THE WORK SAVED, MEASURED IN BYTES ALLOCATED, because that is what the line split cost and
    /// bytes are repeatable where a stopwatch on a shared machine is not.
    /// </summary>
    [Fact]
    public void AChannelWithoutTheWordCostsAFractionOfOneThatCarriesIt()
    {
        var withoutTheWord = Build_History("the fix landed on the branch and the suite is green");

        // SAME SHAPE, SAME ANSWER, and the word is there: mid-sentence, so it declares nothing and
        // Contains_Marker still answers false — having done the full line split to find that out.
        var carryingTheWord = Build_History("the fix landed and we can talk about REROUTE: policy later");

        Assert.All(withoutTheWord, entry => Assert.False(MemberState_Resolver.Contains_Marker(entry, MARKER)));
        Assert.All(carryingTheWord, entry => Assert.False(MemberState_Resolver.Contains_Marker(entry, MARKER)));

        Measure_Walk(withoutTheWord);
        Measure_Walk(carryingTheWord);

        var screened = Measure_Walk(withoutTheWord);
        var split = Measure_Walk(carryingTheWord);

        // PRINTED, so the figure in a report is read off a run and not remembered.
        output.WriteLine($"200 entries x 20 lines, one backward walk: {screened} bytes with the screen, {split} bytes when the body carries the word and the split runs.");

        Assert.True(
            screened * 8 < split,
            $"the screen is not saving the line split: a channel with no {MARKER} allocated {screened} bytes against {split} for one that carries the word — they should differ by an order of magnitude, and they differ by nothing when the screen is gone");
    }

    /// <summary>One backward walk of a channel, exactly as <c>Open_RerouteContracts</c> does it, in
    /// bytes allocated. No early exit is possible: none of these entries declares anything.</summary>
    static long Measure_Walk(IReadOnlyList<IChannelEntry> entries)
    {
        var before = GC.GetAllocatedBytesForCurrentThread();
        var found = false;

        for (var i = entries.Count - 1; i >= 0; i--)
        {
            if (MemberState_Resolver.Contains_Marker(entries[i], MARKER))
                found = true;
        }

        var after = GC.GetAllocatedBytesForCurrentThread();

        Assert.False(found, "the corpus was supposed to declare nothing — the walk short-circuited and the measurement is of a different thing");

        return after - before;
    }

    /// <summary>A channel of ordinary supervisor traffic: 200 entries of 20 lines, which is a modest
    /// day on a real one.</summary>
    static IReadOnlyList<IChannelEntry> Build_History(string line)
    {
        List<IChannelEntry> entries = [];

        for (var i = 0; i < 200; i++)
            entries.Add(Build_Entry("VERDICT — task 4", string.Join('\n', Enumerable.Repeat(line, 20))));

        return entries;
    }
}
