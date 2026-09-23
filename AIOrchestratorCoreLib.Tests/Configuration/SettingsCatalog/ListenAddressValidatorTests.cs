using System.Text.Json.Nodes;
using AIOrchestratorCoreLib.Configuration.SettingsCatalog;
using AIOrchestratorCoreLib.Web;
using Xunit;
using Catalog = global::AIOrchestratorCoreLib.Configuration.SettingsCatalog.SettingsCatalog;

namespace AIOrchestratorCoreLib.Tests.Configuration.SettingsCatalog;

/// <summary>
/// `web.listen`'s validator, closing the gap <c>SettingValidatorsTests</c> used to pin: `host:port`
/// (an IPv6 literal in brackets is accepted too), or the literal "off". Plan 04's listener (Task 7)
/// consumes <see cref="ListenAddress.Parse_OrNull"/> and <see cref="ListenAddress.Is_Off"/> directly —
/// both are pinned here alongside the owner-facing refusals <see cref="SettingValidators.Validate_OrNull"/>
/// produces, so a future change to the parse rules cannot drift from what the message says is wrong.
/// </summary>
public class ListenAddressValidatorTests
{
    [Fact]
    public void TheShippedDefault_IsAccepted()
    {
        Assert.Null(Validate("127.0.0.1:7391"));
        Assert.Equal(("127.0.0.1", 7391), ListenAddress.Parse_OrNull("127.0.0.1:7391"));
    }

    [Fact]
    public void TheWordOff_IsAccepted_AndParsesToNoEndpoint()
    {
        Assert.Null(Validate(ListenAddress.OFF));
        Assert.True(ListenAddress.Is_Off(ListenAddress.OFF));
        Assert.Null(ListenAddress.Parse_OrNull(ListenAddress.OFF));
    }

    [Fact]
    public void AHostWithNoPort_IsRefused_NamingWhatIsMissing()
    {
        var refusal = Validate("127.0.0.1");

        Assert.NotNull(refusal);
        Assert.Contains("port", refusal, StringComparison.OrdinalIgnoreCase);
        Assert.Null(ListenAddress.Parse_OrNull("127.0.0.1"));
    }

    [Fact]
    public void APortOutsideOneToSixtyFiveThousandFiveThirtyFive_IsRefused()
    {
        var tooLow = Validate("127.0.0.1:0");
        var tooHigh = Validate("127.0.0.1:65536");

        Assert.NotNull(tooLow);
        Assert.NotNull(tooHigh);
        Assert.Null(ListenAddress.Parse_OrNull("127.0.0.1:0"));
        Assert.Null(ListenAddress.Parse_OrNull("127.0.0.1:65536"));
    }

    [Fact]
    public void APortThatIsNotANumber_IsRefused_WithoutThrowing()
    {
        var refusal = Validate("127.0.0.1:abc");

        Assert.NotNull(refusal);
        Assert.Null(ListenAddress.Parse_OrNull("127.0.0.1:abc"));
    }

    /// <summary>"[::1]:7391" — decided and pinned here: an IPv6 literal must be bracketed, because an
    /// unbracketed one would make the last ':' ambiguous between an address colon and the port's.</summary>
    [Fact]
    public void AnIpV6Literal_InBrackets_IsAccepted()
    {
        Assert.Null(Validate("[::1]:7391"));
        Assert.Equal(("::1", 7391), ListenAddress.Parse_OrNull("[::1]:7391"));
    }

    [Fact]
    public void ANonStringValue_IsRefused_AsExpectedAString()
    {
        Assert.Equal("Expected a string", SettingValidators.Validate_OrNull(SettingValidators.LISTEN_ADDRESS, JsonValue.Create(7391)));
        Assert.Equal("Expected a string", SettingValidators.Validate_OrNull(SettingValidators.LISTEN_ADDRESS, new JsonArray()));
    }

    /// <summary>
    /// THE CATALOGUE'S SHIPPED DEFAULT MUST SATISFY ITS OWN VALIDATOR, and until this task it did so only
    /// because the validator accepted everything. SettingsCatalogTests.EveryShippedDefault_SatisfiesItsOwn
    /// Definition has been passing web.listen vacuously; after this task it passes for a reason.
    /// </summary>
    [Fact]
    public void TheCatalogueEntrysOwnDefault_PassesTheRealCheck()
    {
        Assert.Null(Validate(Catalog.Find_OrNull("web.listen")!.Default_OrNull));
    }

    static string? Validate(string value) => SettingValidators.Validate_OrNull(SettingValidators.LISTEN_ADDRESS, JsonValue.Create(value));

    static string? Validate(JsonNode? value) => SettingValidators.Validate_OrNull(SettingValidators.LISTEN_ADDRESS, value);
}
