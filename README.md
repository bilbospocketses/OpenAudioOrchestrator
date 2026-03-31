# Fish Audio Orchestrator

A local Blazor Server dashboard for managing Fish Speech Docker containers with voice cloning, TTS generation, and real-time monitoring.

[![License: GPL v3](https://img.shields.io/badge/License-GPLv3-blue.svg)](https://www.gnu.org/licenses/gpl-3.0)

## Overview

Fish Audio Orchestrator runs locally on Windows and manages [Fish Speech](https://github.com/fishaudio/fish-speech) Docker containers via Docker Desktop. It provides a web interface for deploying TTS models, managing a voice reference library, generating speech via a background job queue, and monitoring container health and GPU metrics in real time.

## Features

- **Docker container orchestration** — create, start, stop, swap, and remove Fish Speech containers
- **Voice library** — upload reference audio (10-30s WAV recommended) for voice cloning, tag and organize voices
- **Background TTS job queue** — submit speech generation requests that process serially in the background; navigate freely while jobs run, with real-time status updates
- **App restart resilience** — TTS generation runs inside the container via `docker exec curl`, surviving app restarts; on recovery, completed files are detected automatically
- **Real-time dashboard** — live container status, GPU memory/core utilization (5-second refresh), and latest model output
- **Container log streaming** — live Docker log viewer with backfill, per-container subscription, newest-first ordering
- **Generation history** — log of all TTS generations with playback, download, and delete; updates dynamically when jobs complete
- **Authentication** — ASP.NET Identity with mandatory TOTP/MFA
- **Role-based access control** — Admin (full access) and User (TTS, voice browsing, own history)
- **First-run setup wizard** — 7-step guided installer covering data directories, model download, Docker image pull, server configuration, admin account creation, and TOTP enrollment
- **HTTPS** — optional automatic certificate provisioning via Let's Encrypt (ports 80/443)
- **API gateway** — YARP reverse proxy for the Fish Speech TTS API
- **Health monitoring** — periodic container health checks; uses Docker container status during active TTS generation to avoid false errors
- **Server-side event bus** — in-process event system for real-time UI updates without client-side SignalR connections

## Prerequisites

- Windows 10/11 with [Docker Desktop](https://www.docker.com/products/docker-desktop/)
- NVIDIA GPU with CUDA drivers (tested on RTX 3060 12 GB)
- [.NET 9 SDK](https://dotnet.microsoft.com/download/dotnet/9.0)
- [Git](https://git-scm.com/) with [Git LFS](https://git-lfs.com/) (for model download during setup)

## Quick Start

1. **Clone and run**

   ```bash
   git clone https://github.com/sfidelisboxtechs/FishAudioOrchestrator.git
   cd FishAudioOrchestrator
   dotnet run --project src/FishAudioOrchestrator.Web
   ```

2. **Complete the setup wizard**

   Navigate to `http://localhost:5206`. The 7-step setup wizard will guide you through:
   1. **Data Storage** — choose directories for checkpoints, references, and output files
   2. **Model Download** — download the Fish Audio s2-pro model (~11 GB) from HuggingFace, or skip to download later
   3. **Docker Image** — download the Fish Speech Docker image (~12 GB)
   4. **Server Configuration** — container port range, optional domain + HTTPS via Let's Encrypt
   5. **Admin Account** — create your administrator username and password
   6. **TOTP Setup** — scan QR code with your authenticator app
   7. **Complete** — review settings and restart instructions

   Downloads in steps 2 and 3 can run in the background while you continue through the wizard. The final page will wait for any active downloads to complete before showing restart instructions.

3. **Restart and log in**

   Stop the app (Ctrl+C) and restart it. Navigate to the URL shown in the setup summary. Log in with your admin credentials.

4. **Deploy a model**

   Go to the Deploy page and click Deploy. The model takes 4-5 minutes to initialize on first start. If the model was not downloaded during setup, the Deploy page will offer to download it.

5. **Generate speech**

   Navigate to the TTS Playground, enter text, and click "Submit to Queue". Jobs process in the background — you can navigate freely. Completed audio appears on the History page.

## Model Notes

- The **s2-pro** model requires the `server-cuda-v2.0.0-beta` Docker image (the latest `server-cuda` tag has a [torchaudio compatibility issue](https://github.com/fishaudio/fish-speech/issues/1118))
- The model uses ~22 GB VRAM — on a 12 GB card it spills to system RAM, resulting in slower generation (~9s/token). A 24 GB+ GPU is recommended for production use
- FP16 (`--half`) is enabled by default and recommended for cards with 12 GB VRAM or less
- Voice cloning reference audio should be 10-30 seconds of clean WAV audio with an accompanying transcript

## Configuration

Most settings are configured automatically by the setup wizard. For advanced use or manual changes, edit `src/FishAudioOrchestrator.Web/appsettings.json`:

| Key | Description | Default |
|-----|-------------|---------|
| `ConnectionStrings:Default` | SQLite database path | `C:\MyFishAudioProj\fishorch.db` |
| `FishOrchestrator:DockerEndpoint` | Docker API endpoint | `npipe://./pipe/docker_engine` |
| `FishOrchestrator:DataRoot` | Root directory for data files | `C:\MyFishAudioProj` |
| `FishOrchestrator:PortRange:Start` | Start of container port range | `9001` |
| `FishOrchestrator:PortRange:End` | End of container port range | `9099` |
| `FishOrchestrator:DefaultImageTag` | Default Fish Speech Docker image | `fishaudio/fish-speech:server-cuda` |
| `FishOrchestrator:DockerNetworkName` | Docker bridge network name | `fish-orchestrator` |
| `FishOrchestrator:HealthCheckIntervalSeconds` | Health check frequency (seconds) | `30` |
| `FishOrchestrator:Domain` | FQDN for Let's Encrypt (blank = localhost) | `""` |
| `FishOrchestrator:AdminUser` | Seed admin username (env var override) | `""` |
| `FishOrchestrator:AdminPassword` | Seed admin password (env var override) | `""` |
| `LettuceEncrypt:AcceptTermsOfService` | Accept Let's Encrypt terms | `true` |
| `LettuceEncrypt:DomainNames` | Domain names for certificate | `[]` |
| `LettuceEncrypt:EmailAddress` | Email for certificate renewal notices | `""` |

For automated deployments, set `FishOrchestrator__AdminUser` and `FishOrchestrator__AdminPassword` as environment variables to seed the admin account on first run (TOTP setup required on first login).

## Architecture

- **Blazor Server** (.NET 9) — interactive server-side UI with dark theme
- **SQLite** (EF Core) — model profiles, voice library, generation logs, TTS job queue, Identity tables
- **Docker.DotNet** — container lifecycle management via Docker Desktop
- **docker exec curl** — TTS generation runs inside the container, writing output directly to mounted volume; survives app restarts
- **OrchestratorEventBus** — singleton in-process event bus for real-time UI updates (replaces client-side SignalR hub connections)
- **YARP** — reverse proxy routing to the active Fish Speech container
- **SignalR** — hub retained for future external client support (authorized)
- **ASP.NET Identity** — authentication with mandatory TOTP/MFA; cookie operations via API endpoints for Blazor Server compatibility
- **LettuceEncrypt** — automatic Let's Encrypt HTTPS on ports 80/443 (optional, enabled when Domain is configured)

Design specifications are in [`docs/superpowers/specs/`](docs/superpowers/specs/).

## Development

```bash
# Build
dotnet build

# Run in development mode
dotnet run --project src/FishAudioOrchestrator.Web

# Add an EF Core migration
cd src/FishAudioOrchestrator.Web
dotnet ef migrations add <MigrationName>
```

## Attribution

This product includes Fish Audio Materials developed by [Fish Audio](https://fish.audio). Copyright (c) 2026 Fish Audio. All Rights Reserved.

**Built with Fish Audio.** The Fish Audio S2 Pro models are for non-commercial research use only. Commercial use requires a separate license from Fish Audio. See the [Fish Audio Research License](https://huggingface.co/fishaudio/s2-pro/blob/main/LICENSE.md) for details.

## License

This project is licensed under the GNU General Public License v3.0 — see the [LICENSE](LICENSE) file for details.

See [NOTICE](NOTICE) for third-party attribution requirements.
