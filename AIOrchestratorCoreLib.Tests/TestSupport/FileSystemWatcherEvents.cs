using Xunit;

namespace AIOrchestratorCoreLib.Tests.TestSupport;

/// <summary>
/// THE ONE ANSWER to "does a file write in a subfolder actually reach a <see cref="FileSystemWatcher"/>
/// on this machine". The bridge's mirror loop reacts to channel writes instead of discovering them on
/// its next 2 s tick (owner decision, 2026-09-09), and that reaction is best-effort BY DESIGN: inotify
/// runs out of watches on a Linux box with many folders, a network filesystem reports nothing at all,
/// and a container can be configured with no notification backend. The loop is written to behave
/// exactly as it did before wherever that happens — which is precisely why a test that MEASURES the
/// reaction must be able to say "not here" rather than fail.
/// <para>
/// Probed once, ACTUALLY on this machine, never assumed from <c>RuntimeInformation</c> — the whole
/// point is not to guess a platform's notification semantics from its name. Same reasoning, and the
/// same shape, as <see cref="FileShareEnforcement"/>.
/// </para>
/// </summary>
public static class FileSystemWatcherEvents
{
    /// <summary>Generous: this decides whether a test RUNS, so a slow first FSEvents/inotify registration must not read as "unsupported".</summary>
    const int PROBE_MILLISECONDS = 4_000;

    static readonly Lazy<bool> _areDelivered = new(Probe);

    /// <summary>True when a write to a <c>*.md</c> file one folder down raised a watcher event here.</summary>
    public static bool AreDelivered => _areDelivered.Value;

    static bool Probe()
    {
        var root = Path.Combine(Path.GetTempPath(), $"aiorch-fsw-probe-{Guid.NewGuid():N}");
        var folder = Path.Combine(root, "sub");

        Directory.CreateDirectory(folder);

        try
        {
            using var seen = new ManualResetEventSlim(false);
            using var watcher = new FileSystemWatcher(root, "*.md")
            {
                IncludeSubdirectories = true,
                NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.Size,
            };

            watcher.Changed += (_, _) => seen.Set();
            watcher.Created += (_, _) => seen.Set();
            watcher.EnableRaisingEvents = true;

            // AFTER EnableRaisingEvents, and only then: a write made while the watch is still being
            // registered is not evidence of anything either way.
            File.AppendAllText(Path.Combine(folder, "probe.md"), "probe\n");

            return seen.Wait(PROBE_MILLISECONDS);
        }
        catch
        {
            // A watcher this machine cannot even construct is the case this probe exists to name.
            return false;
        }
        finally
        {
            try
            {
                Directory.Delete(root, recursive: true);
            }
            catch
            {
                // Best-effort cleanup of the probe folder; nothing downstream depends on it, and
                // swallowing this must never turn into a false read of AreDelivered.
            }
        }
    }
}

/// <summary>
/// A <see cref="FactAttribute"/> that runs only where the filesystem actually delivers watcher events —
/// see <see cref="FileSystemWatcherEvents"/>. Elsewhere the bridge falls back to its 2 s tick by design,
/// so a test measuring "faster than a tick" would be asserting against a mechanism the machine does not
/// have. Skipping says so BY NAME, with the reason in the run, instead of a permanent red nothing on the
/// test side can fix — and the behaviour that remains (the tick is served in full) is pinned by tests
/// that carry no attribute at all.
/// </summary>
public sealed class RequiresFileSystemWatcherEventsFactAttribute : FactAttribute
{
    public RequiresFileSystemWatcherEventsFactAttribute()
    {
        if (!FileSystemWatcherEvents.AreDelivered)
        {
            Skip = "This machine's filesystem delivered no FileSystemWatcher event for a write to a "
                 + "*.md file one folder down (confirmed by probe, not by platform name — inotify "
                 + "exhaustion, a network filesystem or a container with no notification backend all "
                 + "look like this). The bridge is written to fall back to its 2 s mirror tick here, "
                 + "so there is no reaction to measure.";
        }
    }
}
