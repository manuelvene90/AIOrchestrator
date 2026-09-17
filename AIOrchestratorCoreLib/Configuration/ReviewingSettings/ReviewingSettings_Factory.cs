using AIOrchestratorCoreLib.Reviewing;

namespace AIOrchestratorCoreLib.Configuration.ReviewingSettings;

public static class ReviewingSettings_Factory
{
    public static IReviewingSettings Create(TimeSpan reviewCap, TimeSpan holdCeiling, int handledMemory)
    {
        return new ReviewingSettingsModel(reviewCap, holdCeiling, handledMemory);
    }

    /// <summary>
    /// WHAT A CALLER WITH NO CONFIG IN REACH GETS, read from the three constants that already govern
    /// an unconfigured machine rather than retyped here — the same contract
    /// <c>SettingsCatalog.Build_Reviewing</c> keeps for the shipped defaults of the same three rows.
    /// Two literals for one dial is how the catalogue and the code come to disagree about what
    /// "nothing configured" means (CLAUDE.md decision 12).
    /// </summary>
    public static IReviewingSettings Create_Default()
    {
        return Create(RerouteContract_Policy.REVIEW_CAP, RerouteContract_Policy.HOLD_CEILING, RerouteContract_Store.HANDLED_MEMORY);
    }
}
