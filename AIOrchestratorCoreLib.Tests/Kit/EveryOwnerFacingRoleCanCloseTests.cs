using Xunit;

namespace AIOrchestratorCoreLib.Tests.Kit;

/// <summary>
/// A ROLE THAT TALKS TO THE OWNER MUST KNOW HOW TO END ITS OWN ORCHESTRATION.
///
/// The mechanism was always there — any session can drop a close-orchestration request and the app
/// holds it for the owner's confirming tap. `supervisor.md` taught it; `solo.md` never did. So on
/// 2026-08-19 the owner told a solo "close this session", and it answered "close the orchestration
/// from the app when you're ready" — advice that is useless to someone on a phone, and the
/// orchestration simply stayed open.
///
/// A capability that exists and is untaught is indistinguishable, from the owner's side, from one
/// that does not exist.
/// </summary>
public class EveryOwnerFacingRoleCanCloseTests
{
    /// <summary>The roles that take instructions straight from the owner, so both can be told to close.</summary>
    static readonly string[] OWNER_FACING_ROLES = ["solo", "supervisor"];

    [Fact]
    public void BothOwnerFacingRolesAreTaughtToCloseTheirOwnOrchestration()
    {
        var files = Find_RoleProtocols();

        // The harness proves itself before asserting a presence: a scan that found nothing would be
        // the strongest possible pass and would mean nothing at all.
        Assert.NotEmpty(files);

        foreach (var expected in OWNER_FACING_ROLES)
        {
            var path = files.FirstOrDefault(entry => entry.Role == expected).Path;

            Assert.True(path != null, $"{expected} is not in kit/skills — this test is not reading what it claims to");

            var text = File.ReadAllText(path!);

            Assert.True(
                text.Contains("close-orchestration"),
                $"{expected} never teaches the close-orchestration request, so that role cannot end its own orchestration when asked");

            Assert.True(
                text.Contains("requester"),
                $"{expected} teaches the close request without the required `requester` field, which the app rejects");
        }
    }

    /// <summary>
    /// THE ANSWER THAT CAUSED THIS, refused explicitly. Knowing the request exists is not enough if
    /// the role still believes "tell them to use the app" is an acceptable reply.
    /// </summary>
    [Fact]
    public void SoloIsToldNotToSendTheOwnerToTheApp()
    {
        var solo = Find_RoleProtocols().FirstOrDefault(entry => entry.Role == "solo").Path;

        Assert.True(solo != null, "the solo protocol is not in kit/skills");

        Assert.Contains("Never answer a close request by telling them to do it from the app", File.ReadAllText(solo!));
    }

    /// <summary>
    /// Every role protocol, keyed by ROLE — which is the folder name now that the kit is a plugin
    /// (kit/skills/&lt;role&gt;/SKILL.md), where it used to be the file name (kit/commands/&lt;role&gt;.md).
    /// Empty when the kit cannot be found; the callers refuse on that before asserting anything.
    /// </summary>
    static IReadOnlyList<(string Role, string Path)> Find_RoleProtocols()
    {
        return KitRepoFiles.Find_AllRoleProtocols();
    }
}
