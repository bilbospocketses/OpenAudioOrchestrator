using Docker.DotNet;
using Docker.DotNet.Models;
using FishAudioOrchestrator.Web.Data;
using FishAudioOrchestrator.Web.Data.Entities;
using FishAudioOrchestrator.Web.Hubs;
using FishAudioOrchestrator.Web.Proxy;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;

namespace FishAudioOrchestrator.Web.Services;

public class DockerOrchestratorService : IDockerOrchestratorService
{
    private readonly IDockerClient _docker;
    private readonly IContainerConfigService _configService;
    private readonly AppDbContext _context;
    private readonly FishProxyConfigProvider _proxyProvider;
    private readonly IDockerNetworkService _networkService;
    private readonly IHubContext<OrchestratorHub> _hub;
    private readonly OrchestratorEventBus _eventBus;

    public DockerOrchestratorService(
        IDockerClient docker,
        IContainerConfigService configService,
        AppDbContext context,
        FishProxyConfigProvider proxyProvider,
        IDockerNetworkService networkService,
        IHubContext<OrchestratorHub> hub,
        OrchestratorEventBus eventBus)
    {
        _docker = docker;
        _configService = configService;
        _context = context;
        _proxyProvider = proxyProvider;
        _networkService = networkService;
        _hub = hub;
        _eventBus = eventBus;
    }

    private async Task PushStatusUpdateAsync()
    {
        var allModels = await _context.ModelProfiles.ToListAsync();
        var statusEvents = allModels.Select(m => new ContainerStatusEvent(
            m.Id, m.Name, m.Status.ToString(), m.HostPort, m.LastStartedAt)).ToList();
        await _hub.Clients.All.SendAsync("ReceiveContainerStatus", statusEvents);
        _eventBus.RaiseContainerStatus(statusEvents);
    }

    public async Task CreateAndStartModelAsync(ModelProfile profile)
    {
        // If we already have a container ID, try to start the existing container
        if (profile.ContainerId is not null)
        {
            try
            {
                var inspect = await _docker.Containers.InspectContainerAsync(profile.ContainerId);
                // Container exists — start it if not already running
                if (inspect.State.Status != "running")
                {
                    await _docker.Containers.StartContainerAsync(
                        profile.ContainerId, new ContainerStartParameters());
                }

                profile.Status = ModelStatus.Running;
                profile.LastStartedAt = DateTime.UtcNow;
                await _context.SaveChangesAsync();
                await UpdateProxyAsync(profile);
                await PushStatusUpdateAsync();
                return;
            }
            catch (DockerContainerNotFoundException)
            {
                // Container was removed externally — fall through to create a new one
                profile.ContainerId = null;
            }
        }

        var createParams = _configService.BuildCreateParams(profile);
        var response = await _docker.Containers.CreateContainerAsync(createParams);
        profile.ContainerId = response.ID;

        await _docker.Containers.StartContainerAsync(
            response.ID, new ContainerStartParameters());

        profile.Status = ModelStatus.Running;
        profile.LastStartedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync();
        await UpdateProxyAsync(profile);
        await PushStatusUpdateAsync();
    }

    private async Task UpdateProxyAsync(ModelProfile profile)
    {
        var containerIp = await _networkService.GetContainerIpAsync(profile.ContainerId!);
        if (containerIp is not null)
        {
            _proxyProvider.UpdateDestination($"http://{containerIp}:8080");
        }
        else
        {
            _proxyProvider.UpdateDestination($"http://localhost:{profile.HostPort}");
        }
    }

    public async Task StopModelAsync(ModelProfile profile)
    {
        if (profile.ContainerId is null) return;

        await ThrowIfJobProcessingAsync();

        await _docker.Containers.StopContainerAsync(
            profile.ContainerId,
            new ContainerStopParameters { WaitBeforeKillSeconds = 30 });

        profile.Status = ModelStatus.Stopped;
        await _context.SaveChangesAsync();

        _proxyProvider.ClearDestination();

        await PushStatusUpdateAsync();
    }

    public async Task RemoveModelAsync(ModelProfile profile)
    {
        if (profile.ContainerId is null) return;

        if (profile.Status == ModelStatus.Running)
        {
            await StopModelAsync(profile);
        }

        await _docker.Containers.RemoveContainerAsync(
            profile.ContainerId,
            new ContainerRemoveParameters { Force = true });

        profile.ContainerId = null;
        profile.Status = ModelStatus.Created;
        await _context.SaveChangesAsync();

        await PushStatusUpdateAsync();
    }

    public async Task SwapModelAsync(ModelProfile newModel)
    {
        await ThrowIfJobProcessingAsync();

        var running = await _context.ModelProfiles
            .FirstOrDefaultAsync(m => m.Status == ModelStatus.Running);

        if (running is not null && running.Id != newModel.Id)
        {
            await StopModelAsync(running);
        }

        await CreateAndStartModelAsync(newModel);
    }

    private async Task ThrowIfJobProcessingAsync()
    {
        var hasActiveJob = await _context.TtsJobs
            .AnyAsync(j => j.Status == TtsJobStatus.Processing);

        if (hasActiveJob)
            throw new InvalidOperationException(
                "Cannot change models while a TTS job is processing. Wait for the job to finish or cancel it first.");
    }

    public async Task<string> GetContainerStatusAsync(string containerId)
    {
        var inspect = await _docker.Containers.InspectContainerAsync(containerId);
        return inspect.State.Status;
    }
}
