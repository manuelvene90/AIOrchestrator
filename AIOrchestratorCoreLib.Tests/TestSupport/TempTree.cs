namespace AIOrchestratorCoreLib.Tests.TestSupport;

/// <summary>
/// Deletes a fixture's temp folder in a way that cannot fail a test whose assertions already passed.
///
/// <para>
/// THE INCIDENT (2026-09-10, macOS, one run in three): `CloseImplementerGuardProbeTests` went red
/// with `IOException: Directory not empty` raised from its own `Dispose()` — the assertions had all
/// passed. `Directory.Delete(recursive: true)` enumerates, deletes children, then removes the
/// parent; on APFS a child that a still-closing writer (a watcher handle, a `dotnet test` child
/// process winding down) re-materialises between those two steps makes the final removal fail. The
/// window is milliseconds wide, which is why it reads as flakiness and why chasing it in the
/// product is a dead end: there is no product here, only teardown.
/// </para>
/// <para>
/// TWO RULES, BOTH DELIBERATE. It RETRIES rather than deleting once, because the condition is
/// transient by construction. And it SWALLOWS the final failure, because a red must mean a claim
/// about the app was falsified — a red that means "the temp folder outlived the test" trains the
/// reader to discount reds, which is the expensive failure. What is lost is a leaked folder under
/// the OS temp root, which the OS reaps.
/// </para>
/// <para>
/// This is NOT a licence to soften an assertion. Nothing here touches what a test claims; it
/// touches only the cleanup that runs after the claim has already been settled.
/// </para>
/// </summary>
internal static class TempTree
{
    /// <summary>Attempts allowed before the leak is accepted. Three covers a window measured in ms.</summary>
    private const int ATTEMPTS = 3;

    /// <summary>Pause between attempts — long enough for a closing handle, short enough to not pace a suite.</summary>
    private static readonly TimeSpan BETWEEN_ATTEMPTS = TimeSpan.FromMilliseconds(50);

    /// <summary>
    /// Removes <paramref name="folderPath"/> and everything under it, retrying the transient
    /// "directory not empty" race, and never throwing. Safe on a path that is already gone.
    /// </summary>
    public static void Delete_BestEffort(string folderPath)
    {
        if (string.IsNullOrWhiteSpace(folderPath))
            return;

        for (var attempt = 1; attempt <= ATTEMPTS; attempt++)
        {
            try
            {
                if (Directory.Exists(folderPath))
                    Directory.Delete(folderPath, recursive: true);

                return;
            }
            catch (IOException) when (attempt < ATTEMPTS)
            {
                Thread.Sleep(BETWEEN_ATTEMPTS);
            }
            catch (UnauthorizedAccessException) when (attempt < ATTEMPTS)
            {
                Thread.Sleep(BETWEEN_ATTEMPTS);
            }
            catch (IOException)
            {
                return;
            }
            catch (UnauthorizedAccessException)
            {
                return;
            }
        }
    }
}
