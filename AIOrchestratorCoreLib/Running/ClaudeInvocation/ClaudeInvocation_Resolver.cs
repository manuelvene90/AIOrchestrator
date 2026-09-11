using System.Runtime.InteropServices;

namespace AIOrchestratorCoreLib.Running.ClaudeInvocation;

/// <summary>
/// The real CLI, per OS. On Windows the npm shim is <c>claude.cmd</c>, which only cmd.exe can
/// start (the WPF app has no shell of its own); elsewhere <c>claude</c> resolves through PATH like
/// any executable. The prompt never
/// travels through this command line — it goes on stdin — so cmd.exe's quoting rules never meet
/// user text.
/// </summary>
public static class ClaudeInvocation_Resolver
{
    public static IClaudeInvocation Resolve_ForThisOs()
    {
        return RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
            ? ClaudeInvocation_Factory.Create("cmd.exe", ["/c", "claude"])
            : ClaudeInvocation_Factory.Create("claude", []);
    }
}
