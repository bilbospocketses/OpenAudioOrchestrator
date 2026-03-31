# Changelog

All notable changes and post-plan decisions for Fish Audio Orchestrator.

Design specs and implementation plans in `superpowers/specs/` and `superpowers/plans/` reflect decisions made at the time they were written. This changelog captures adjustments made after those documents were finalized.

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
