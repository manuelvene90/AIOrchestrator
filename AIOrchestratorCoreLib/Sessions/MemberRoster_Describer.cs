using AIOrchestratorCoreLib.Sessions.OrchestrationMember;

namespace AIOrchestratorCoreLib.Sessions;

/// <summary>
/// What is actually RUNNING in an orchestration, in one phrase, for the card the owner reads before
/// deciding whether to spend more.
///
/// IT COUNTED A SOLO AS AN IMPLEMENTER, because everything that was not a reviewer fell into that
/// bucket — so a basic orchestration's card read "1 implementer" for something with no implementer
/// and no supervisor at all. The Telegram side has always said it correctly: "One solo session
/// spawned — no supervisor, no implementers." Two owner-facing surfaces disagreeing about what a
/// basic orchestration IS is the same class as every other finding tonight, on the surface where the
/// owner decides about money.
///
/// The rule this file already lived by is the argument for fixing it: reviewers were broken out of
/// the implementer count precisely because a hidden count "lies about where the spend is going". A
/// solo hidden inside the implementer count is that same lie, and it survived because nothing in the
/// app project has any concept of a solo.
///
/// It lives in CoreLib rather than in the window so it can be tested — the WPF project has no suite,
/// and this is a counting rule rather than a layout.
/// </summary>
public static class MemberRoster_Describer
{
    public static string Describe_OpenMembers(IReadOnlyList<IOrchestrationMember> members)
    {
        var solos = 0;
        var implementers = 0;
        var reviewers = 0;

        foreach (var member in members)
        {
            if (member.ClosedUtc != null)
                continue;

            switch (MemberKind_Ids.Resolve_Kind(member.MemberId))
            {
                case MemberKinds.Solo:
                    solos++;
                    break;

                case MemberKinds.Reviewer:
                    reviewers++;
                    break;

                default:
                    implementers++;
                    break;
            }
        }

        // A SOLO IS THE WHOLE ORCHESTRATION, so it is said on its own rather than counted beside
        // roles that do not exist there. "1 solo session · 0 implementers" would be technically true
        // and would still invite the reader to look for the rest of a crew.
        if (solos > 0 && implementers == 0 && reviewers == 0)
            return solos == 1 ? "one solo session" : $"{solos} solo sessions";

        List<string> parts = [];

        if (solos > 0)
            parts.Add($"{solos} solo");

        parts.Add($"{implementers} implementer{(implementers == 1 ? "" : "s")}");

        if (reviewers > 0)
            parts.Add($"{reviewers} reviewer{(reviewers == 1 ? "" : "s")}");

        return string.Join(" · ", parts);
    }
}
