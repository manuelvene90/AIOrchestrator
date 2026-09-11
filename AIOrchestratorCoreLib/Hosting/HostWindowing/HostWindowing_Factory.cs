namespace AIOrchestratorCoreLib.Hosting.HostWindowing;

public static class HostWindowing_Factory
{
    /// <summary>
    /// RESOLVED ONCE, AT COMPOSITION, not per call — the same shape
    /// <c>ClaudeInvocation_Resolver.Resolve_ForThisOs</c> already uses in this solution. A per-call
    /// `OperatingSystem.IsWindows()` is a branch every future call site has to remember; a
    /// capability injected at startup is one the compiler hands them.
    /// </summary>
    public static IHostWindowing Create_ForThisHost()
    {
        return OperatingSystem.IsWindows() ? new WindowsHostWindowingModel() : new UnsupportedHostWindowingModel();
    }

    /// <summary>
    /// The "no window server" host, explicitly. For a test that must assert the refusal REGARDLESS
    /// of where the suite runs: a probe that depends on its own host being Linux is a probe that
    /// silently stops testing anything the moment it is run on Windows.
    /// </summary>
    public static IHostWindowing Create_Unsupported()
    {
        return new UnsupportedHostWindowingModel();
    }
}
