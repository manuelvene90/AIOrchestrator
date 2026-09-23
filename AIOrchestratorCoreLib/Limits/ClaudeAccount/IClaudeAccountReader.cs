namespace AIOrchestratorCoreLib.Limits.ClaudeAccount;

/// <summary>
/// WHICH CLAUDE ACCOUNT this machine is logged in to right now, as the CLI itself records it.
///
/// <para>
/// WHY THE APP NEEDS TO KNOW (owner, 2026-09-23). The owner runs three accounts and logs in to
/// another one when a limit is reached. A usage-limit pause describes ONE account's allowance, but
/// it used to be kept with no account on it at all: at 05:01 the weekly window of the first account
/// reached 100% and dispatch paused until Tue 29 Sep; at 05:03 the owner logged in to a second
/// account; every restart of the app after that restored the six-day pause and brought no session
/// back, for an allowance that was no longer the one being spent.
/// </para>
/// <para>
/// NULL MEANS "CANNOT TELL", never "a different account". A missing file, a half-written file, an
/// API-key login with no OAuth block — each answers null, and a null never lifts a pause (CLAUDE.md
/// decision 21: a guard that cannot evaluate its predicate says so and leaves things as they were).
/// </para>
/// </summary>
public interface IClaudeAccountReader
{
    string? Read_AccountId_OrNull();
}
