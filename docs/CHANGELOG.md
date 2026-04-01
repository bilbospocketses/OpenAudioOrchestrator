# Changelog

All notable changes and post-plan decisions for Fish Audio Orchestrator.

Design specs and implementation plans in `superpowers/specs/` and `superpowers/plans/` reflect decisions made at the time they were written. This changelog captures adjustments made after those documents were finalized.

---

## 2026-04-01

### Security: TOTP Sign-In Endpoint Hardened
- **Before:** `/api/auth/signin?userId=<guid>` signed in any user by ID with no verification
- **After:** Endpoint requires a cryptographic one-time token generated after successful TOTP verification; token expires after 60 seconds and is single-use
- **Also fixed:** `returnUrl` parameter validated as local path to prevent open redirects
- **Affected files:** `Program.cs`, `LoginTotp.razor`

### Security: User ID Removed from TOTP Query String
- **Before:** `/login/totp?uid=<user-guid>` exposed internal user IDs in the URL
- **After:** Login flow uses opaque `totp-pending` tokens stored in `IMemoryCache`; user IDs never appear in URLs
- **Affected files:** `Program.cs`, `LoginTotp.razor`

### Security: Audio Files Now Require Authentication
- **Before:** `UseStaticFiles` served audio output and reference files to anyone who knew the filename
- **After:** Audio files served via `MapGet` endpoints with `.RequireAuthorization()` and path traversal validation
- **Affected files:** `Program.cs`

### Security: Setup Page Blocked After Completion
- **Before:** `SetupGuardMiddleware` always allowed `/setup` through; only a client-side Blazor redirect prevented access after setup
- **After:** Middleware returns server-side redirect to `/` when users already exist, preventing rogue admin creation
- **Affected files:** `SetupGuardMiddleware.cs`

### Security: Rate Limiting on Login Endpoint
- **Added:** Fixed-window rate limiter (10 requests/minute per IP) on `/api/auth/login`
- **Affected files:** `Program.cs`

### Security: Voice ID Path Traversal Prevention
- **Added:** Regex validation (`^[a-zA-Z0-9_-]+$`) on voice IDs before use as directory names
- **Affected files:** `VoiceLibrary.razor`

### Fix: Test Suite Compilation Restored
- **Before:** 12 compilation errors — `OrchestratorEventBus` parameter added to service constructors without updating tests; `HealthMonitorService` constructor changed from `ITtsClientService` to `IDockerClient`
- **After:** All 4 broken test files fixed; `HealthMonitorServiceTests` also corrected to call `CheckHealthAsync` 5 times (matching consecutive-failure threshold). Two pre-existing `ContainerConfigServiceTests` assertions fixed (volume bind mount path, `--half` flag moved from env var to Cmd).
- **Result:** 74/74 tests passing
- **Affected files:** `DockerOrchestratorServiceTests.cs`, `HealthMonitorServiceTests.cs`, `TtsClientServiceTests.cs`, `HealthMonitorHubTests.cs`, `ContainerConfigServiceTests.cs`, `VoiceLibraryServiceTests.cs`

### Fix: Process Handle Leaks in TtsJobProcessor
- **Before:** `Process.Start()` returned objects that were never disposed, leaking OS handles per TTS job
- **After:** All `Process` objects wrapped in `using` statements
- **Affected files:** `TtsJobProcessor.cs`

### Fix: Graceful Shutdown Blocked by Task.Delay
- **Before:** `Task.Delay(PollInterval, CancellationToken.None)` in poll loop ignored shutdown signal
- **After:** Uses `stoppingToken` for cancellation-aware delay
- **Affected files:** `TtsJobProcessor.cs`

### Fix: Stderr Pipe Deadlock Risk
- **Before:** `StandardError.ReadToEndAsync()` called only after process exit; if stderr buffer filled, process would hang indefinitely
- **After:** Stderr read started immediately as a `Task<string>` and passed through to the poll loop
- **Affected files:** `TtsJobProcessor.cs`

### Fix: Blazor Pages Migrated to IDbContextFactory
- **Before:** 5 pages injected scoped `AppDbContext`, which in Blazor Server lives for the entire circuit — causing stale tracked entities, memory growth, and concurrent access risk from event handlers
- **After:** All 5 pages use `IDbContextFactory<AppDbContext>` with short-lived `using var db` per operation
- **Affected files:** `Dashboard.razor`, `TtsPlayground.razor`, `GenerationHistory.razor`, `Logs.razor`, `Deploy.razor`, `Program.cs`

### Fix: Timer Disposal in Setup and Deploy Pages
- **Before:** `Setup.razor` (3 timers) and `Deploy.razor` (1 timer) created `Timer` objects without implementing `IAsyncDisposable`; navigating away left timers firing on a disposed circuit
- **After:** Both components implement `IAsyncDisposable` and dispose all timers
- **Affected files:** `Setup.razor`, `Deploy.razor`

### Fix: Process Disposal in SetupService
- **Before:** Background processes (`_modelDownloadProcess`, `_dockerPullProcess`) stored in singleton but never disposed
- **After:** Process disposed in `Exited` event handler
- **Affected files:** `SetupService.cs`

### Fix: Socket Exhaustion in VoiceLibraryService
- **Before:** `new HttpClient()` created per `SyncVoicesToContainerAsync` call, causing TIME_WAIT socket exhaustion
- **After:** Uses `IHttpClientFactory` for proper connection pooling
- **Affected files:** `VoiceLibraryService.cs`, `Program.cs`

### Fix: CancellationTokenSource Leaks in FishProxyConfigProvider
- **Before:** Old CTS cancelled but never disposed on model swaps
- **After:** `oldCts.Dispose()` called after cancel
- **Affected files:** `FishProxyConfigProvider.cs`

### Fix: Tags Not Saved When Adding Voice
- **Before:** Tags field collected in UI but never passed to `AddVoiceAsync` (method didn't accept the parameter)
- **After:** `AddVoiceAsync` accepts optional `tags` parameter; VoiceLibrary passes `_addTags` through
- **Affected files:** `VoiceLibrary.razor`, `VoiceLibraryService.cs`, `IVoiceLibraryService.cs`

### Fix: Missing Config Fallback in TtsClientService
- **Before:** `config["FishOrchestrator:DataRoot"]!` with null-forgiving operator crashed DI if config missing
- **After:** Falls back to default path like other services
- **Affected files:** `TtsClientService.cs`

### Improvement: SetupGuardMiddleware Caches DB Query
- **Before:** `db.Users.AnyAsync()` ran on every non-static request
- **After:** Result cached in `volatile bool _setupComplete` — once users exist, never queries again
- **Affected files:** `SetupGuardMiddleware.cs`

### Improvement: PostLoginRedirectMiddleware Reduced DB Queries
- **Added:** Static assets, API routes, SignalR hubs, and audio paths bypass the DB lookup
- **Affected files:** `PostLoginRedirectMiddleware.cs`

### Improvement: GPU Metrics Exception Handling
- **Before:** Unhandled exception in `CollectGpuMetricsAsync` could kill the entire `HealthMonitorService` (both health and GPU loops)
- **After:** Exceptions caught and logged; loop continues
- **Affected files:** `HealthMonitorService.cs`

### Improvement: Duplicated Truncate Helper Extracted
- **Before:** Identical `Truncate` method defined in 3 separate Razor components
- **After:** Shared `StringHelpers.Truncate` via `@using static` in `_Imports.razor`
- **Affected files:** `StringHelpers.cs` (new), `_Imports.razor`, `TtsPlayground.razor`, `GenerationHistory.razor`, `VoiceLibrary.razor`

### Improvement: DateTimeOffset Standardized Across Entities
- **Before:** `AppUser` used `DateTimeOffset`; all other entities (`ModelProfile`, `ReferenceVoice`, `GenerationLog`, `TtsJob`) used `DateTime`
- **After:** All entities, hub events, and service assignments use `DateTimeOffset` consistently
- **Note:** No migration needed — SQLite stores both types as TEXT; existing data is compatible
- **Affected files:** `ModelProfile.cs`, `ReferenceVoice.cs`, `GenerationLog.cs`, `TtsJob.cs`, `HubEvents.cs`, `GpuMetricsState.cs`, `DockerOrchestratorService.cs`, `VoiceLibraryService.cs`, `TtsJobProcessor.cs`, `ContainerLogService.cs`

---

## 2026-03-31

### Default Data Path Changed
- **Before:** `D:\DockerData\FishAudio` (and subdirectories Checkpoints, References, Output)
- **After:** `C:\MyFishAudioProj` (and subdirectories)
- **Reason:** Removed PII-adjacent path from the codebase before open-source release
- **Affected files:** `appsettings.json`, `Program.cs`, `TtsJobProcessor.cs`, `Deploy.razor`, `GenerationHistory.razor`, `Setup.razor`, `README.md`
- **Note:** The setup wizard now allows users to choose their own directories on first run, so the default path is just an initial suggestion

### Default Docker Image Tag Changed
- **Before:** `fishaudio/fish-speech:server-cuda`
- **After:** `fishaudio/fish-speech:server-cuda-v2.0.0-beta`
- **Reason:** The latest `server-cuda` tag ships with torchaudio 2.9 which removed `list_audio_backends()`, causing a startup crash ([fishaudio/fish-speech#1118](https://github.com/fishaudio/fish-speech/issues/1118)). The beta tag uses torchaudio 2.8 which still has the function.
- **Affected files:** `appsettings.json`, `README.md`

### TTS Generation Method Changed
- **Before:** Blazor app makes HTTP POST to container's `/v1/tts` endpoint, receives audio bytes in the response, writes file to disk
- **After:** App runs `docker exec curl` inside the container, which writes the output file directly to the mounted output volume
- **Reason:** The HTTP approach failed when the app restarted during generation (response lost, audio lost, job had to be re-processed). With `docker exec curl`, the curl process survives app restarts because it runs inside the container. On recovery, the app checks if the output file exists.
- **Affected files:** `TtsJobProcessor.cs`

### Health Check Behavior During TTS Generation
- **Before:** Health monitor always hit the container's HTTP `/v1/health` endpoint every 30 seconds
- **After:** When TTS jobs are queued or processing, health monitor checks Docker container running status instead of the HTTP endpoint
- **Reason:** The Fish Speech container doesn't respond to HTTP health checks while actively generating audio, causing false "Error" status on the dashboard
- **Affected files:** `HealthMonitorService.cs`

### GPU Metrics Refresh Rate
- **Before:** GPU metrics collected every 30 seconds (tied to health check interval)
- **After:** GPU metrics collected every 5 seconds on a separate loop
- **Reason:** 30-second updates felt stale in the navbar display
- **Affected files:** `HealthMonitorService.cs`, `NavMenu.razor`

### SignalR Hub Authorization and Event Bus
- **Before:** Blazor components created client-side `HubConnection` to the SignalR hub for real-time updates
- **After:** Components subscribe to an in-process `OrchestratorEventBus` singleton. The SignalR hub retains `[Authorize]` and is reserved for future external clients.
- **Reason:** Blazor Server components can't reliably forward auth cookies to client-side hub connections, causing 401 errors. Since components already run on the server, an in-process event bus is simpler and more reliable.
- **Affected files:** `OrchestratorEventBus.cs`, `OrchestratorHub.cs`, `Dashboard.razor`, `Logs.razor`, `TtsPlayground.razor`, `ContainerLogService.cs`

### Cookie Authentication Moved to API Endpoints
- **Before:** Blazor components called `SignInManager.SignInAsync()` and `SignOutAsync()` directly
- **After:** Authentication operations use `/api/auth/login` (POST), `/api/auth/signin` (GET), and `/api/auth/signout` (GET) endpoints
- **Reason:** Blazor Server can't set cookies after the response has started streaming over the SignalR circuit. API endpoints handle cookies via normal HTTP requests.
- **Affected files:** `Program.cs`, `Login.razor`, `LoginTotp.razor`, `Setup.razor`, `ManageAccount.razor`

### Fish Audio Attribution Added
- **NOTICE file** added to root with Fish Audio attribution as required by the Research License
- **"Built with Fish Audio"** displayed on setup wizard download pages and TTS Playground
- **Non-commercial research disclaimer** added to wizard and README
- **Reason:** Compliance with the Fish Audio Research License (Section II/III prominent display requirement)
- **Affected files:** `NOTICE`, `Setup.razor`, `TtsPlayground.razor`, `README.md`
