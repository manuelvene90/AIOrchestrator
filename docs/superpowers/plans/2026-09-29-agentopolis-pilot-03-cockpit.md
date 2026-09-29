# Agentopolis pilot — plan 03: the pilot cockpit (M1, Manu's track)

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended)
> or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`)
> syntax for tracking.

**Goal:** a minimal Avalonia desktop app (Windows + macOS) with which each of Guido's developers
connects to their team's Runner, sees what waits for them, follows the task board, talks to their
desk and the project lead with replies streaming in, and presses approval cards.

**Architecture:** one C# solution under `app/` (plan 01). A copied subset of the DVFT desktop controls
(`Agentopolis.UiKit`) gives the window chrome, theme and controls; `Agentopolis.Api` (plan 01) holds the
contract DTOs; `Agentopolis.Runner` is the client (HTTP + Server-Sent Events + an SSH tunnel);
`AgentopolisCockpitApp` is the Avalonia app with CommunityToolkit.Mvvm viewmodels. The app never embeds
the engine; it only talks to the Runner API.

**Tech Stack:** .NET 10, Avalonia 12.0.3 (Desktop, Fonts.Inter, Themes.Fluent), CommunityToolkit.Mvvm
8.4.2, System.Text.Json, `System.Net.ServerSentEvents` (.NET's SSE parser), xunit.v3 3.2.2 +
xunit.runner.visualstudio 3.1.5 + Microsoft.NET.Test.Sdk 17.11.1, Avalonia.Headless.XUnit 12.0.3 — all
already used by the DVFT suite; no new package.

**Spec:** `docs/superpowers/specs/2026-09-29-agentopolis-product-design.md` §4.2-4.3, D5, D6, D7;
contract `api/v1/openapi.yaml` (plan 01 point 2); overview P1, P2.

## Global Constraints

- `app/CODING_PATTERNS_QUICKREF.md` (copied in plan 01) binds every `.cs` file: component = folder of
  three files (`IName.cs`, `NameModel.cs` `internal sealed`, `Name_Factory.cs` with a static `Create`
  returning the interface); `new XxxModel` only in factories; no `{ get; set; }` and no expression
  bodies in library projects (folders ending in `App` may use both, rules 3-4 relaxed); no `!`;
  single-statement bodies on the next line without braces; switch expressions end `_ => throw`;
  exception messages carry the values; `IReadOnlyList`/`IReadOnlyDictionary` in signatures; `static`
  where `this` is unused; ALL_CAPS constants in a constants project; no hex colours, pixel or font-size
  literals in AXAML (`DynamicResource` for brushes, `StaticResource` for tokens); files over 400 lines
  need a reason, over 700 are split.
- Views use `dv:` controls only (the raw-controls hook refuses `Button`, `TextBox`, … in `Views/*.axaml`).
- `DvWindow` is the root of every window; never set its `Background`.
- Every copied file keeps a first-line comment naming its DVFT source path and commit; the list lives
  in `app/src/Agentopolis.UiKit/COPIED_FROM.md`.
- The member's API token is never written in plain text: Windows DPAPI (`ProtectedData`, CurrentUser),
  macOS the same AES key-file scheme `AccountLib` uses — copied, not referenced.
- Build Release as well as Debug before calling a task done (NEW_PRODUCT_APP_GUIDE H-bis §3).
- Commits: one per task, explicit paths, `git commit -F <tempfile>`. Branch `m1/pilot-cockpit`.

## Review Focus

1. **The tunnel dies silently** — laptop sleep, Wi-Fi change, server restart. The app must show
   "disconnected — reconnecting" within 20 s and recover on its own, never show stale data as live.
   Task 4 tests a tunnel process exit and a `/v1/me` failure.
2. **A press on a card that changed meanwhile** — another member already approved it. The app must
   show "someone already answered this" (the `stale` result) and refresh the card, never retry the
   press. Task 5 tests it.
3. **SSE reconnect losing or duplicating a streamed reply** — `Last-Event-ID` resume after a drop in
   the middle of `reply.delta` events; the thread must end with the text exactly once. Task 7 tests it.
4. **A message sent twice** — double-click on Send, or a retry after a timeout. `clientId` generated
   once per compose, reused on retry. Task 7 tests it.
5. **A missing theme resource** — a `DynamicResource` key absent from the copied theme fails silently
   and paints nothing. Task 1's headless test renders every copied control and asserts the named
   brushes resolve.

---

### Task 1: Agentopolis.UiKit — the copied DVFT controls and theme

**Files:**
- Create: `app/src/Agentopolis.UiKit/Agentopolis.UiKit.csproj` (Avalonia 12.0.3 only)
- Create, copied from `R\02_ClientSide\UniversalControlsDesktopLib\Controls\` (R = the DVFT suite root):
  `WindowChrome/`, `Theming/`, `Primitives/`, `NavigationRail/`, `Card/`, `SelectableRow/`, `Spinner/`,
  `BusyOverlay/`, `StatusBar/`, `NoticeBar/`, `OverflowText/`, `Menus/`, `Chat/`; `_Namespace.cs`;
  a trimmed `UniversalControlsDesktopLib.Themes.axaml` → `UiKit.Themes.axaml` (only the copied folders)
- Create, copied from `R\02_ClientSide\DvftThemeLib\`: `DaVinciFintechTheme.axaml` →
  `AgentopolisTheme.axaml`, `Suite/*` (without the CompactNumericUpDown style in
  `Suite_ControlStyles.axaml`), `SemanticBrushes*.cs`, `Controls/DialogShell`
- Create, fixed files from the generated tokens: `Theme/Suite_SemanticColors.axaml`,
  `Theme/Agentopolis_Theme.axaml`, `Theme/Agentopolis_Identity.axaml` (copied from one product's
  generated `obj\…\GeneratedTokens` output, renamed; `ProductLightBrush` defined — DvWindow binds it)
- Create: `app/src/Agentopolis.Constants/` (`Languages { En, It }`, `Themes`, copied from `EnumsLib`)
- Create: `app/src/Agentopolis.UiKit/COPIED_FROM.md`
- Test: `app/tests/Agentopolis.UiKit.Tests/HeadlessSetup.cs`, `ThemeResourcesTests.cs`

**Interfaces:** Produces the `dv:` XML namespace (`DvWindow`, `DvButton`, `DvTextBox`, `DvToggleButton`,
`DvNavigationRail`, `DvCard`, `DvSelectableRow`, `DvChat`, `DvSpinner`, `DvBusyOverlay`,
`DvStatusBar`, `DvNoticeBar`) and `UiKit.Themes.axaml`, `AgentopolisTheme.axaml`.

**The cut rule:** `DvWindow`'s AXAML hosts `Dialogs/Hosting` (→ `AssetTreePanel` → `DataGrid/Formatting`)
and `Drawer/Hosting` (→ `InlinePanel` → `Help` → `Languages`). Copy `Dialogs/` for `DvConfirmDialog`
and its chain only if it builds with Avalonia alone; remove the `Drawer` host from the copied
`DvWindow` (the cockpit has no help drawer). Any other file the build asks for that is outside the list
above is **cut, not copied**, and the cut is written in `COPIED_FROM.md`.

- [ ] **Step 1: Write the failing headless test**

```csharp
// app/tests/Agentopolis.UiKit.Tests/ThemeResourcesTests.cs
public sealed class ThemeResourcesTests
{
    [AvaloniaTheory]
    [InlineData("ProductLightBrush")]
    [InlineData("TextPrimaryBrush")]
    [InlineData("SurfaceBrush")]
    [InlineData("PositiveBrush")]
    [InlineData("NegativeBrush")]
    public void Every_named_brush_resolves_in_both_theme_variants(string key)
    {
        HeadlessSetup.Ensure_AppResourcesLoaded();
        foreach (var variant in new[] { ThemeVariant.Light, ThemeVariant.Dark })
        {
            var found = Application.Current!.TryGetResource(key, variant, out var value);
            Assert.True(found && value is IBrush, $"brush '{key}' missing in {variant}");
        }
    }

    [AvaloniaFact]
    public void A_DvWindow_with_rail_card_and_chat_shows_and_closes()
    {
        HeadlessSetup.Ensure_AppResourcesLoaded();
        var window = new DvWindow { Content = new DvNavigationRail() };
        window.Show();
        Assert.True(window.IsVisible, "DvWindow did not show");
        window.Close();
    }
}
```

The brush keys above are the ones `SemanticBrushes` names in the DVFT source; replace them with the
exact keys found in `SemanticBrushKeys` when copying (read the file, do not guess). `Application.Current!`
is test code: the `!` rule binds library code; if the hook refuses it, use
`?? throw new Exception("no Application in headless test")`.

- [ ] **Step 2:** run `dotnet test app/tests/Agentopolis.UiKit.Tests` — expected FAIL (no project/types).
- [ ] **Step 3:** copy the folders and files listed, trim `UiKit.Themes.axaml`, apply the cut rule,
  write `COPIED_FROM.md`; `HeadlessSetup.Ensure_AppResourcesLoaded()` mirrors the future `App.axaml`
  order exactly (NEW_PRODUCT_APP_GUIDE D.3: `RequestedThemeVariant`, theme resources, `InterFontFamily`,
  FluentTheme, TypographyStyles, ControlStyles, umbrella themes).
- [ ] **Step 4:** run the test again — expected PASS; build Release.
- [ ] **Step 5:** commit `feat(app): the DVFT controls and theme the cockpit needs, copied and trimmed`.

### Task 2: Runner client — HTTP and Server-Sent Events

**Files:**
- Create: `app/src/Agentopolis.Runner/RunnerClient/IRunnerClient.cs`, `RunnerClientModel.cs`,
  `RunnerClient_Factory.cs`
- Create: `app/src/Agentopolis.Runner/EventStream/IRunnerEventStream.cs`, `RunnerEventStreamModel.cs`,
  `RunnerEventStream_Factory.cs`
- Create: `app/src/Agentopolis.Constants/RunnerConstants.cs` (`API_PORT = 4894`,
  `RECONNECT_MIN_SECONDS = 1`, `RECONNECT_MAX_SECONDS = 20`, `REQUEST_TIMEOUT_SECONDS = 30`)
- Test: `app/tests/Agentopolis.Runner.Tests/RunnerClientTests.cs`, `RunnerEventStreamTests.cs`,
  `FakeRunnerHandler.cs` (an `HttpMessageHandler` serving `api/v1/fixtures/*.json`)

**Interfaces:**
- Consumes: DTOs in `Agentopolis.Api` (plan 01): `Member`, `InboxItem`, `TaskLine`, `TaskDetail`,
  `Room`, `Message`, `Card`, `CardOption`, `PressResult`, `RunnerEvent` (a record with `Id`, `Name`,
  `Json`).
- Produces:

```csharp
public interface IRunnerClient
{
    Task<Member> Get_Me_Async(CancellationToken ct);
    Task<IReadOnlyList<InboxItem>> Get_Inbox_Async(CancellationToken ct);
    Task<IReadOnlyList<TaskLine>> Get_Tasks_Async(bool closed, CancellationToken ct);
    Task<TaskDetail> Get_Task_Async(string taskId, CancellationToken ct);
    Task<IReadOnlyList<Room>> Get_Rooms_Async(CancellationToken ct);
    Task<IReadOnlyList<Message>> Get_Messages_Async(string roomId, string? afterMessageId, CancellationToken ct);
    Task<string> Post_Message_Async(string roomId, Guid clientId, string text, CancellationToken ct);
    Task<Card> Get_Card_Async(string cardId, CancellationToken ct);
    Task<PressResult> Press_Card_Async(string cardId, string action, long epoch, string? text, CancellationToken ct);
    Task<PinResult> Post_Pin_Async(string windowId, string pin, CancellationToken ct);
}

public interface IRunnerEventStream
{
    // Yields events for ever, reconnecting with Last-Event-ID and backoff 1 s → 20 s;
    // raises ConnectionChanged(true/false) on every transition.
    IAsyncEnumerable<RunnerEvent> Read_Events_Async(CancellationToken ct);
    event EventHandler<bool>? ConnectionChanged;
}
```

`RunnerClient_Factory.Create(Uri baseAddress, string bearerToken, HttpMessageHandler? handlerForTests)`;
JSON options `static readonly`: `PropertyNameCaseInsensitive = true`, `JsonStringEnumConverter`. A 409
or a `result: stale` body both map to `PressResult.Stale`. Non-2xx other than 409 throws
`RunnerHttpException` carrying method, path and status in the message.

- [ ] **Step 1: Write the failing tests**

```csharp
[Fact]
public async Task Press_on_an_old_epoch_returns_Stale_for_both_the_409_and_the_200_shapes()
{
    var client409 = RunnerClient_Factory.Create(BASE, "t", FakeRunnerHandler.Returning(409, "{\"result\":\"stale\"}"));
    var client200 = RunnerClient_Factory.Create(BASE, "t", FakeRunnerHandler.Returning(200, "{\"result\":\"stale\"}"));
    Assert.Equal(PressOutcome.Stale, (await client409.Press_Card_Async("c1", "approve", 3, null, default)).Outcome);
    Assert.Equal(PressOutcome.Stale, (await client200.Press_Card_Async("c1", "approve", 3, null, default)).Outcome);
}

[Fact]
public async Task Every_request_carries_the_bearer_token()
{
    var handler = FakeRunnerHandler.FromFixtures();
    var client = RunnerClient_Factory.Create(BASE, "secret-token", handler);
    await client.Get_Inbox_Async(default);
    Assert.Equal("Bearer secret-token", handler.LastRequest?.Headers.Authorization?.ToString());
}

[Fact]
public async Task The_event_stream_resumes_with_Last_Event_ID_after_a_drop()
{
    var handler = FakeRunnerHandler.SseThenDrop(
        firstConnection: "id: 41\nevent: reply.delta\ndata: {\"roomId\":\"r\",\"turnId\":\"t\",\"text\":\"Hel\"}\n\n",
        secondConnectionExpectsLastEventId: "41",
        secondConnection: "id: 42\nevent: reply.end\ndata: {\"roomId\":\"r\",\"turnId\":\"t\",\"messageId\":\"m\"}\n\n");
    var stream = RunnerEventStream_Factory.Create(BASE, "t", handler);
    var names = await stream.Read_Events_Async(default).Take(2).Select(e => e.Name).ToListAsync();
    Assert.Equal(new[] { "reply.delta", "reply.end" }, names);
    Assert.Equal("41", handler.LastEventIdSeenOnReconnect);
}
```

- [ ] **Step 2:** run — expected FAIL (types missing).
- [ ] **Step 3:** implement with `HttpClient` (`HttpCompletionOption.ResponseHeadersRead` for the stream)
  and `System.Net.ServerSentEvents.SseParser.Create(stream).EnumerateAsync(ct)`; keep the last `id`;
  on any exception or end of stream: raise `ConnectionChanged(false)`, wait the backoff, reconnect with
  `Last-Event-ID`, raise `ConnectionChanged(true)` on the first byte.
- [ ] **Step 4:** run — expected PASS. Build Release.
- [ ] **Step 5:** commit `feat(app): the Runner client, with a stream that resumes where it dropped`.

### Task 3: Connection settings, stored safely

**Files:**
- Create: `app/src/Agentopolis.Runner/ConnectionSettings/` triple (`IConnectionSettingsStore`: host,
  ssh user, ssh key path, member token — `Load_OrNull()`, `Save(settings)`), with
  `ProtectedSecret/` triple (DPAPI CurrentUser on Windows; the copied AES key-file scheme on macOS)
- Test: `app/tests/Agentopolis.Runner.Tests/ConnectionSettingsStoreTests.cs`

**Interfaces:** Produces `ConnectionSettings(string Host, string SshUser, string SshKeyPath, string MemberToken)`
(a DTO record) and `IConnectionSettingsStore`.

- [ ] **Step 1: failing tests** — saving then loading returns equal settings; the file on disk does not
  contain the token's bytes (read it raw and assert `!contains`); loading a corrupted file returns null
  and logs, never throws.
- [ ] **Step 2:** run — FAIL. **Step 3:** implement, file under the user's app-data folder
  `Agentopolis/connection.json`. **Step 4:** run — PASS. **Step 5:** commit
  `feat(app): the member's connection and token, stored encrypted`.

### Task 4: The SSH tunnel and the connection state

**Files:**
- Create: `app/src/Agentopolis.Runner/Tunnel/` triple (`ITunnel`: `Start_Async`, `Stop`,
  `LocalPort`, event `Exited`), `ProcessRunner/` triple (wraps `Process` so tests can fake it)
- Create: `app/src/Agentopolis.Runner/Connection/` triple (`IRunnerConnection`: state
  `Disconnected|Connecting|Connected|Reconnecting`, `Me`, `Client`, `Events`; health check `GET /v1/me`
  every 15 s)
- Test: `app/tests/Agentopolis.Runner.Tests/TunnelTests.cs`, `RunnerConnectionTests.cs`

**Interfaces:**
- Consumes: `IRunnerClient`, `IRunnerEventStream` (task 2), `ConnectionSettings` (task 3).
- Produces: `IRunnerConnection` with `event EventHandler<ConnectionState>? StateChanged`.

The tunnel command, exactly (built as an argument list, never a shell string):
`ssh -N -o ExitOnForwardFailure=yes -o ServerAliveInterval=15 -o ServerAliveCountMax=2
-o BatchMode=yes -i <key> -L 127.0.0.1:<free local port>:127.0.0.1:4894 <user>@<host>`.
The free local port is taken by binding a `TcpListener` on port 0 and releasing it.

- [ ] **Step 1: failing tests** — the argument list equals the one above for given settings; when the
  fake process exits, state goes `Connected → Reconnecting` within one health interval and a new
  process is started with a backoff; when `/v1/me` answers 401, state goes `Disconnected` with the
  reason "your access was revoked or the token is wrong" and no restart loop.
- [ ] **Step 2:** FAIL. **Step 3:** implement. **Step 4:** PASS. **Step 5:** commit
  `feat(app): the SSH tunnel to the team's Runner, and a connection that says when it is down`.

### Task 5: The app shell and the inbox with card presses

**Files:**
- Create: `app/src/AgentopolisCockpitApp/` — `AgentopolisCockpitApp.csproj` (WinExe, net10.0,
  `AvaloniaUseCompiledBindingsByDefault`), `App.axaml` (the D.3 order), `App.axaml.cs` (wiring only),
  `Services/AppServices.cs` (one static class; members that degrade vs refuse; `Set_X_ForTests`),
  `Logging/` (the 3 `AppLogger` files copied from `R\00_Shared\LoggingLib\AppLogger\`, JSON lines, one
  file per UTC day), `Views/Main/MainWindow.axaml(.cs)` (DvWindow; a `DvNavigationRail` with Inbox,
  Tasks, Rooms; a `DvStatusBar` showing the connection state and the member's name),
  `Views/Main/MainViewModel.cs`, `Views/Connect/ConnectWindow.axaml(.cs)` + `ConnectViewModel.cs`
  (host, ssh user, key file, token; "Connect" tests `/v1/me` before saving),
  `Views/Inbox/InboxView.axaml(.cs)` + `InboxViewModel.cs` + `CardViewModel.cs`,
  `Views/Inbox/PinDialog.axaml(.cs)` + `PinDialogViewModel.cs`
- Test: `app/tests/AgentopolisCockpitApp.Tests/InboxViewModelTests.cs`, `CardViewModelTests.cs`,
  `MainWindowTests.cs` (headless), `Fakes/FakeRunnerClient.cs`, `Fakes/FakeRunnerConnection.cs`

**Interfaces:**
- Consumes: `IRunnerConnection` (task 4), the DTOs.
- Produces: `CardViewModel` — `[ObservableProperty] string title, body; bool isHighRisk; bool isBusy;
  string? notice;` `IReadOnlyList<CardOptionViewModel> Options`; `[RelayCommand] Task Press(CardOptionViewModel)`.

Behaviour: the inbox reloads on `inbox.changed` and `card.changed`; a high-risk card shows its
approve option with the danger style; pressing sends `{action, epoch}`; `Stale` → notice "Someone
already answered this — here is the card as it is now" and the card is re-fetched, never re-pressed;
`PinRequired` → the PIN dialog, then `POST /v1/pin/{windowId}`; `Refused` → the server's reason shown
as the notice (e.g. a Viewer); while `isBusy` every option is disabled (no double press).

- [ ] **Step 1: failing tests**

```csharp
[Fact]
public async Task A_stale_press_shows_the_notice_refetches_and_does_not_press_again()
{
    var runner = FakeRunnerClient.WithCard("c1", epoch: 3).PressReturning(PressOutcome.Stale).ThenCardEpoch(4);
    var card = CardViewModel_For(runner, "c1");
    await card.PressCommand.ExecuteAsync(card.Options[0]);
    Assert.Equal(1, runner.PressCount);
    Assert.Equal(4, card.Epoch);
    Assert.StartsWith("Someone already answered this", card.Notice);
}

[Fact]
public async Task Options_are_disabled_while_a_press_is_in_flight()
{
    var runner = FakeRunnerClient.WithCard("c1", epoch: 3).PressHanging();
    var card = CardViewModel_For(runner, "c1");
    _ = card.PressCommand.ExecuteAsync(card.Options[0]);
    Assert.True(card.IsBusy);
    Assert.False(card.PressCommand.CanExecute(card.Options[1]));
}

[Fact]
public async Task A_refused_press_shows_the_servers_reason()
{
    var runner = FakeRunnerClient.WithCard("c1", epoch: 3).PressRefused("viewers cannot approve");
    var card = CardViewModel_For(runner, "c1");
    await card.PressCommand.ExecuteAsync(card.Options[0]);
    Assert.Equal("viewers cannot approve", card.Notice);
}
```

- [ ] **Step 2:** FAIL. **Step 3:** implement; viewmodels in the `…App` project may use
  `{ get; set; }`/`=>` (rules 3-4 relaxed), everything else strict. **Step 4:** PASS; headless
  `MainWindowTests` shows and closes the main window with a fake connection. Build Release.
- [ ] **Step 5:** commit `feat(app): the cockpit shell and the inbox, with presses that never double`.

### Task 6: The task board

**Files:** `Views/Tasks/TasksView.axaml(.cs)`, `TasksViewModel.cs`, `TaskRowViewModel.cs`,
`TaskDetailViewModel.cs`; test `TasksViewModelTests.cs`.

Behaviour: open tasks by default, a toggle for closed; each row: title, project, state word, since,
worker; the detail shows acceptance lines, rounds, cost with its label ("estimated" or "billed", never
a bare number), and — when `measure` is present — "before → after (×speedup)" formatted as
`12.4 s → 5.1 s (×2.4)`; refresh on `task.changed` for that id only.

- [ ] **Step 1: failing tests** — the measure line formats as above for `Measure { Metric = "wall",
  Unit = "s", Baseline = 12.4, Latest = 5.1, Speedup = 2.43 }` (speedup shown to one decimal); a task without `measure` shows no measure line; a `task.changed` for another id does not
  refetch this one; the cost label is always shown.
- [ ] **Step 2:** FAIL. **Step 3:** implement. **Step 4:** PASS. **Step 5:** commit
  `feat(app): the task board, with the house's before and after`.

### Task 7: Rooms and threads, with streaming replies

**Files:** `Views/Rooms/RoomsView.axaml(.cs)`, `RoomsViewModel.cs`, `ThreadViewModel.cs`,
`ComposerViewModel.cs` (uses the copied `DvChat`); test `ThreadViewModelTests.cs`, `ComposerViewModelTests.cs`.

Behaviour: rooms list (desk first, then projects, topics, tasks); a thread loads its messages and
appends on `message.new`; `reply.delta` events for the room grow one pending bubble keyed by `turnId`;
`reply.end` replaces it with the final message fetched by id (so a resumed stream never shows the text
twice); `turn.started` shows "<colleague> is working…"; the composer generates one `clientId` per
compose and reuses it on retry; Send is disabled while sending.

- [ ] **Step 1: failing tests**

```csharp
[Fact]
public async Task Deltas_then_end_leave_exactly_the_final_message_once()
{
    var thread = ThreadViewModel_For(FakeRunnerClient.WithMessage("m9", "Hello world"), roomId: "r");
    thread.On_Event(Delta(id: 41, "r", "t1", "Hello "));
    thread.On_Event(Delta(id: 42, "r", "t1", "world"));
    thread.On_Event(Delta(id: 42, "r", "t1", "world"));   // the same event again after a resume
    await thread.On_Event_Async(End(id: 43, "r", "t1", "m9"));
    Assert.Single(thread.Messages, m => m.Text == "Hello world");
    Assert.DoesNotContain(thread.Messages, m => m.IsPending);
}

[Fact]
public async Task A_retried_send_reuses_the_same_client_id()
{
    var runner = FakeRunnerClient.PostFailingOnceThenOk();
    var composer = ComposerViewModel_For(runner, roomId: "r");
    composer.Text = "run the benchmark";
    await composer.SendCommand.ExecuteAsync(null);   // fails
    await composer.SendCommand.ExecuteAsync(null);   // retry
    Assert.Equal(runner.ClientIdsSeen[0], runner.ClientIdsSeen[1]);
}
```

  Duplicate deltas: the engine's event ids are monotonic, so the thread ignores any event whose id is
  not greater than the last one it applied — the test's third call repeats id 42. `Delta`/`End` are
  test helpers building a `RunnerEvent` with the given id and the contract's JSON.
- [ ] **Step 2:** FAIL. **Step 3:** implement. **Step 4:** PASS. **Step 5:** commit
  `feat(app): rooms and threads, replies streaming in and landing once`.

### Task 8: End-to-end against the engine's fake mode, and the owner's test

**Files:** `app/tools/run-against-fake.ps1` and `.sh` (start the engine with `--fake --no-slack`
from plan 02 on `127.0.0.1:4894`, create a member with `member add`, print the token, start the app
pointed at `http://127.0.0.1:4894` with the tunnel disabled by a `--no-tunnel` debug switch compiled
only in Debug); `app/README.md` (how to run; how a developer at the customer connects).

- [ ] **Step 1:** with plan 02's points 3-4 merged, run the script; in the app: connect, see the desk
  room, write "hello", see the reply stream in, see a question card in the inbox, answer it, see the
  task board. Record the run (screen capture) in the PR.
- [ ] **Step 2:** the owner's test, once, on Windows and on a Mac, against a Debian 13 VM installed
  with plan 02 point 6, through the real SSH tunnel.
- [ ] **Step 3:** commit `docs(app): how to run the cockpit against a Runner`.
