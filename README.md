# Fish Audio Orchestrator

A local Blazor Server dashboard for managing Fish Speech Docker containers with voice cloning, TTS generation, and real-time monitoring.

[![License: GPL v3](https://img.shields.io/badge/License-GPLv3-blue.svg)](https://www.gnu.org/licenses/gpl-3.0)

## Overview

Fish Audio Orchestrator runs locally on Windows and manages [Fish Speech](https://github.com/fishaudio/fish-speech) Docker containers via Docker Desktop. It provides a web interface for deploying TTS models, managing a voice reference library, generating speech, and monitoring container health and GPU metrics in real time.

## Features

- **Docker container orchestration** — create, start, stop, swap, and remove Fish Speech containers
- **Voice library** — upload reference audio for voice cloning, tag and organize voices
- **TTS playground** — generate speech with format selection and optional voice cloning
- **Real-time dashboard** — live container status, GPU memory/utilization, and TTS generation notifications via SignalR
- **Container log streaming** — live Docker log viewer with backfill and per-container subscription
- **Generation history** — searchable log of all TTS generations, scoped per user
- **Authentication** — ASP.NET Identity with mandatory TOTP/MFA
- **Role-based access control** — Admin (full access) and User (TTS, voice browsing, own history)
- **First-run setup wizard** — guided admin account creation with TOTP enrollment
- **HTTPS** — optional automatic certificate provisioning via Let's Encrypt
- **API gateway** — YARP reverse proxy for the Fish Speech TTS API
- **Health monitoring** — periodic container health checks with automatic status updates

## Prerequisites

- Windows 10/11 with [Docker Desktop](https://www.docker.com/products/docker-desktop/)
- NVIDIA GPU with CUDA drivers (tested on RTX 3060 12 GB)
- [.NET 9 SDK](https://dotnet.microsoft.com/download/dotnet/9.0)
- Fish Speech Docker image:
  ```bash
  docker pull fishaudio/fish-speech:server-cuda
  ```

## Quick Start

1. **Clone the repository**

   ```bash
   git clone <repository-url>
   cd FishAudioOrchestrator
   ```

2. **Create data directories**

   ```bash
   mkdir -p D:\DockerData\FishAudio\Checkpoints
   mkdir -p D:\DockerData\FishAudio\References
   mkdir -p D:\DockerData\FishAudio\Output
   ```

3. **Review configuration**

   Edit `src/FishAudioOrchestrator.Web/appsettings.json` if you need to change the data root, Docker endpoint, or port range (see [Configuration](#configuration) below).

4. **Run the application**

   ```bash
   dotnet run --project src/FishAudioOrchestrator.Web
   ```

5. **Complete setup**

   Navigate to `https://localhost:5001`. The first-run setup wizard will guide you through:
   - Optional FQDN configuration (for Let's Encrypt HTTPS)
   - Admin account creation
   - TOTP enrollment (scan QR code with your authenticator app)

6. **Deploy a model**

   Place Fish Speech model checkpoints in `D:\DockerData\FishAudio\Checkpoints\`, then use the Deploy page to register and start a model.

## Configuration

All configuration is in `src/FishAudioOrchestrator.Web/appsettings.json`:

| Key | Description | Default |
|-----|-------------|---------|
| `ConnectionStrings:Default` | SQLite database path | `D:\DockerData\FishAudio\fishorch.db` |
| `FishOrchestrator:DockerEndpoint` | Docker API endpoint | `npipe://./pipe/docker_engine` |
| `FishOrchestrator:DataRoot` | Root directory for data files | `D:\DockerData\FishAudio` |
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

- **Blazor Server** (.NET 9) — interactive server-side UI
- **SQLite** (EF Core) — model profiles, voice library, generation logs, Identity tables
- **Docker.DotNet** — container lifecycle management via Docker Desktop
- **YARP** — reverse proxy routing to the active Fish Speech container
- **SignalR** — real-time container status, GPU metrics, TTS notifications, log streaming
- **ASP.NET Identity** — authentication with mandatory TOTP/MFA
- **LettuceEncrypt** — automatic Let's Encrypt HTTPS (optional)

Design specifications are in [`docs/superpowers/specs/`](docs/superpowers/specs/).

## Development

```bash
# Build
dotnet build

# Run tests
dotnet test

# Run in development mode
dotnet run --project src/FishAudioOrchestrator.Web

# Add an EF Core migration
cd src/FishAudioOrchestrator.Web
dotnet ef migrations add <MigrationName>
```

## License

This project is licensed under the GNU General Public License v3.0 — see the [LICENSE](LICENSE) file for details.
