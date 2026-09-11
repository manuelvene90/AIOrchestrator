using System.Reflection;
using AIOrchestratorCoreLib.Configuration;

namespace AIOrchestratorCoreLib.Planning.PlanBackend;

/// <summary>The backend to use, and what went wrong if it is not the one that was asked for.</summary>
/// <param name="Backend">Never null — a failed load still yields <see cref="PlanMdBackend"/>.</param>
/// <param name="Error">Null on success. A sentence naming what could not be loaded and why.</param>
public readonly record struct PlanBackendLoad(IPlanBackend Backend, string? Error);

/// <summary>
/// Turns <see cref="PlanBackendSettings"/> into a live <see cref="IPlanBackend"/>, loading an external
/// adapter by assembly path and type name.
///
/// <para>
/// A FAILED LOAD FALLS BACK TO PLAN.md AND SAYS SO — both halves, and the second is the one that is
/// easy to drop. Falling back keeps the bridge running: a missing adapter must cost an orchestration
/// its upstream synchronisation, never its supervision. Saying so is what stops that being a silent
/// downgrade, where the owner believes their planning system is connected and it has not been since a
/// path changed. Same rule as a hook that cannot evaluate its predicate (decision 21): allow, and name
/// which predicate failed.
/// </para>
/// <para>
/// AN UNRECOGNISED KIND IS A FAILED LOAD, which the first version got wrong in the direction that
/// matters: <c>"externa1"</c> is not <c>"external"</c>, so it read as the default and produced no error
/// at all — the exact silent downgrade the paragraph above forbids, reachable by one typo in a
/// hand-edited file. Only an ABSENT block means PLAN.md alone without comment.
/// </para>
/// <para>
/// REFLECTION ONLY WHEN ASKED. The default kind never touches <see cref="Assembly"/>, so an
/// installation that configures nothing carries no load, no probe, and no failure mode.
/// </para>
/// </summary>
public static class PlanBackend_Loader
{
    public static PlanBackendLoad Load(PlanBackendSettings? settings)
    {
        if (settings == null)
            return new PlanBackendLoad(new PlanMdBackend(), null);

        var resolved = settings.Value;

        if (resolved.Is_PlanMd())
            return new PlanBackendLoad(new PlanMdBackend(), null);

        if (!resolved.Is_External())
            return Fallback($"planBackend.kind '{resolved.Kind}' is not recognised (expected '{PlanBackendSettings.KIND_PLAN_MD}' or '{PlanBackendSettings.KIND_EXTERNAL}')");

        if (string.IsNullOrWhiteSpace(resolved.AssemblyPath) || string.IsNullOrWhiteSpace(resolved.TypeName))
            return Fallback("planBackend.kind is 'external' but assembly and/or type are missing");

        try
        {
            if (!File.Exists(resolved.AssemblyPath))
                return Fallback($"plan backend assembly '{resolved.AssemblyPath}' does not exist");

            var assembly = Assembly.LoadFrom(resolved.AssemblyPath);
            var type = assembly.GetType(resolved.TypeName, throwOnError: false);

            if (type == null)
                return Fallback($"type '{resolved.TypeName}' was not found in '{resolved.AssemblyPath}'");

            if (!typeof(IPlanBackend).IsAssignableFrom(type))
                return Fallback($"type '{resolved.TypeName}' does not implement IPlanBackend");

            if (Activator.CreateInstance(type) is not IPlanBackend backend)
                return Fallback($"type '{resolved.TypeName}' could not be constructed — it needs a public parameterless constructor");

            return new PlanBackendLoad(backend, null);
        }
        catch (Exception ex)
        {
            return Fallback($"loading '{resolved.TypeName}' from '{resolved.AssemblyPath}' failed: {ex.Message}");
        }
    }

    static PlanBackendLoad Fallback(string error)
    {
        return new PlanBackendLoad(new PlanMdBackend(), $"{error} — running on PLAN.md alone");
    }
}
