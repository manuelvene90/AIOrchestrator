using AIOrchestratorCoreLib.Telegram;
using Xunit;

namespace AIOrchestratorCoreLib.Tests.Telegram;

/// <summary>
/// The owner, 2026-09-09: "even better if I just write the command and then I get prompted with the
/// possible options so I don't have to worry about spelling mistakes." A bare /model or /effort is
/// answered with buttons; in a crew every row offers the same value for both roles, supervisor on
/// the left, so one tap settles both the value and who it is for.
/// </summary>
public class ModelEffortPromptBuilderTests
{
    [Fact]
    public void ABasicOrchestration_OffersTheModels_TwoPerRow_ForTheOneSessionItHas()
    {
        var prompt = ModelEffortPrompt_Builder.Build_ModelPrompt("crm-3", hasSupervisor: false);

        Assert.Equal(2, prompt.Rows.Count);
        Assert.Equal(["Fable 5.1", "Opus 5"], prompt.Rows[0].Select(button => button.Label).ToList());
        Assert.Equal(["Sonnet 5", "Haiku 4.5"], prompt.Rows[1].Select(button => button.Label).ToList());

        foreach (var button in prompt.Rows.SelectMany(row => row))
        {
            var parsed = ModelEffortButton_Data.Parse_OrNull(button.Data);

            Assert.NotNull(parsed);
            Assert.Equal(ModelEffortKinds.Model, parsed.Value.Kind);
            Assert.Equal("crm-3", parsed.Value.OrchId);
            Assert.Equal(ModelEffortButton_Data.IMPLEMENTER_ROLE, parsed.Value.Role);
        }

        Assert.Equal("fable", ModelEffortButton_Data.Parse_OrNull(prompt.Rows[0][0].Data)?.Value);
        Assert.Equal("haiku", ModelEffortButton_Data.Parse_OrNull(prompt.Rows[1][1].Data)?.Value);
    }

    [Fact]
    public void ACrew_OffersEachModelForBothRoles_SupervisorOnTheLeft()
    {
        var prompt = ModelEffortPrompt_Builder.Build_ModelPrompt("option-lab-2", hasSupervisor: true);

        Assert.Equal(4, prompt.Rows.Count);

        foreach (var row in prompt.Rows)
        {
            Assert.Equal(2, row.Count);

            var left = ModelEffortButton_Data.Parse_OrNull(row[0].Data);
            var right = ModelEffortButton_Data.Parse_OrNull(row[1].Data);

            Assert.Equal(ModelEffortButton_Data.SUPERVISOR_ROLE, left?.Role);
            Assert.Equal(ModelEffortButton_Data.IMPLEMENTER_ROLE, right?.Role);
            Assert.Equal(left?.Value, right?.Value);
        }

        Assert.Equal("sup Fable 5.1", prompt.Rows[0][0].Label);
        Assert.Equal("imp Fable 5.1", prompt.Rows[0][1].Label);
        Assert.Contains("supervisor", prompt.Text);
    }

    [Fact]
    public void ABasicOrchestration_OffersTheFiveEfforts_ThreeThenTwo()
    {
        var prompt = ModelEffortPrompt_Builder.Build_EffortPrompt("crm-3", hasSupervisor: false);

        Assert.Equal(2, prompt.Rows.Count);
        Assert.Equal(["low", "medium", "high"], prompt.Rows[0].Select(button => button.Label).ToList());
        Assert.Equal(["xhigh", "max"], prompt.Rows[1].Select(button => button.Label).ToList());
        Assert.Equal((ModelEffortKinds.Effort, "crm-3", "imp", "max"), ModelEffortButton_Data.Parse_OrNull(prompt.Rows[1][1].Data));
    }

    [Fact]
    public void ACrew_OffersEachEffortForBothRoles_OnePerRow()
    {
        var prompt = ModelEffortPrompt_Builder.Build_EffortPrompt("option-lab-2", hasSupervisor: true);

        Assert.Equal(5, prompt.Rows.Count);
        Assert.Equal("sup xhigh", prompt.Rows[3][0].Label);
        Assert.Equal("imp xhigh", prompt.Rows[3][1].Label);
        Assert.Equal((ModelEffortKinds.Effort, "option-lab-2", "sup", "xhigh"), ModelEffortButton_Data.Parse_OrNull(prompt.Rows[3][0].Data));
    }

    /// <summary>
    /// A ROLE was typed ("/effort sup") but no value: the buttons come for that role only, one per
    /// row, so the owner is not offered the other role's dial they did not ask about.
    /// </summary>
    [Fact]
    public void ARoleWithoutAValue_GetsThatRolesButtonsOnly()
    {
        var prompt = ModelEffortPrompt_Builder.Build_EffortPrompt_ForRole("option-lab-2", ModelEffortButton_Data.SUPERVISOR_ROLE);

        Assert.Equal(2, prompt.Rows.Count);
        Assert.Equal(["low", "medium", "high"], prompt.Rows[0].Select(button => button.Label).ToList());
        Assert.All(prompt.Rows.SelectMany(row => row), button => Assert.Equal("sup", ModelEffortButton_Data.Parse_OrNull(button.Data)?.Role));
    }

    /// <summary>
    /// A VALUE was typed in a crew topic with no role ("/model fable" where there is a supervisor
    /// and implementers): two buttons settle who it is for, rather than guessing or respawning both.
    /// </summary>
    [Fact]
    public void ATypedValueInACrew_AsksWhichRole_WithTwoButtons()
    {
        var prompt = ModelEffortPrompt_Builder.Build_RolePickPrompt(ModelEffortKinds.Model, "option-lab-2", "fable");

        var row = Assert.Single(prompt.Rows);

        Assert.Equal(["sup Fable 5.1", "imp Fable 5.1"], row.Select(button => button.Label).ToList());
        Assert.Equal((ModelEffortKinds.Model, "option-lab-2", "sup", "fable"), ModelEffortButton_Data.Parse_OrNull(row[0].Data));
        Assert.Equal((ModelEffortKinds.Model, "option-lab-2", "imp", "fable"), ModelEffortButton_Data.Parse_OrNull(row[1].Data));
        Assert.Contains("Fable 5.1", prompt.Text);
    }

    [Fact]
    public void ATypedEffortInACrew_AsksWhichRole_WithTwoButtons()
    {
        var prompt = ModelEffortPrompt_Builder.Build_RolePickPrompt(ModelEffortKinds.Effort, "option-lab-2", "xhigh");

        var row = Assert.Single(prompt.Rows);

        Assert.Equal(["sup xhigh", "imp xhigh"], row.Select(button => button.Label).ToList());
        Assert.Equal((ModelEffortKinds.Effort, "option-lab-2", "imp", "xhigh"), ModelEffortButton_Data.Parse_OrNull(row[1].Data));
    }

    /// <summary>Every label stays readable on a phone — the numbering fallback of the option layout is never needed here.</summary>
    [Fact]
    public void EveryLabel_IsShort()
    {
        var all = ModelEffortPrompt_Builder.Build_ModelPrompt("x", true).Rows
            .Concat(ModelEffortPrompt_Builder.Build_EffortPrompt("x", true).Rows)
            .SelectMany(row => row);

        Assert.All(all, button => Assert.True(button.Label.Length <= 16, $"label too long: '{button.Label}'"));
    }
}
