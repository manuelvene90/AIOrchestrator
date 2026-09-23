namespace AIOrchestratorCoreLib.Configuration.EndeavourSettings;

public static class EndeavourSettings_Factory
{
    /// <summary>
    /// The shipped cap, owner decision O2 (2026-09-23): three open members per endeavour. A const, and
    /// the catalogue row READS it rather than restating it (CLAUDE.md decision 12) — the guardrail
    /// constants' direction, which carries no type-initializer cycle because a const is not initialised
    /// at run time.
    /// </summary>
    public const int DEFAULT_MAX_OPEN_SIBLINGS = 3;

    /// <summary>
    /// A cap below one would refuse the requester itself, since the count includes it — an invariant
    /// violation, so it throws naming the value. The JSON path never reaches this with such a value: the
    /// catalogue row's minimum refuses it at the resolver, which falls to the layer below.
    /// </summary>
    public static IEndeavourSettings Create(int maxOpenSiblings)
    {
        if (maxOpenSiblings < 1)
            throw new ArgumentOutOfRangeException(nameof(maxOpenSiblings), maxOpenSiblings, $"An endeavour cap of {maxOpenSiblings} would refuse the requester itself — it counts open members, the requester included, so it must be at least 1");

        return new EndeavourSettingsModel(maxOpenSiblings);
    }

    /// <summary>
    /// What a config ASSEMBLED IN MEMORY gets (the Settings window, <c>Create_Empty</c> in tests). The
    /// loader never uses it: it resolves through <see cref="EndeavourSettings_Json.Parse"/>, preset included.
    /// </summary>
    public static IEndeavourSettings Create_Default()
    {
        return Create(DEFAULT_MAX_OPEN_SIBLINGS);
    }
}
