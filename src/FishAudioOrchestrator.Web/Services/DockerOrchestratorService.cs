using Docker.DotNet;
using Docker.DotNet.Models;
using FishAudioOrchestrator.Web.Data;
using FishAudioOrchestrator.Web.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace FishAudioOrchestrator.Web.Services;

public class DockerOrchestratorService : IDockerOrchestratorService
{
    private readonly IDockerClient _docker;
    private readonly IContainerConfigService _configService;
    private readonly AppDbContext _context;

    public DockerOrchestratorService(
        IDockerClient docker,
        IContainerConfigService configService,
        AppDbContext context)
    {
        _docker = docker;
        _configService = configService;
        _context = context;
    }

    public async Task CreateAndStartModelAsync(ModelProfile profile)
    {
        var createParams = _configService.BuildCreateParams(profile);
        var response = await _docker.Containers.CreateContainerAsync(createParams);
        profile.ContainerId = response.ID;

        await _docker.Containers.StartContainerAsync(
            response.ID, new ContainerStartParameters());

        profile.Status = ModelStatus.Running;
        profile.LastStartedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync();
    }

    public async Task StopModelAsync(ModelProfile profile)
    {
        if (profile.ContainerId is null) return;

        await _docker.Containers.StopContainerAsync(
            profile.ContainerId,
            new ContainerStopParameters { WaitBeforeKillSeconds = 30 });

        profile.Status = ModelStatus.Stopped;
        await _context.SaveChangesAsync();
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
    }

    public async Task SwapModelAsync(ModelProfile newModel)
    {
        var running = await _context.ModelProfiles
            .FirstOrDefaultAsync(m => m.Status == ModelStatus.Running);

        if (running is not null && running.Id != newModel.Id)
        {
            await StopModelAsync(running);
        }

        await CreateAndStartModelAsync(newModel);
    }

    public async Task<string> GetContainerStatusAsync(string containerId)
    {
        var inspect = await _docker.Containers.InspectContainerAsync(containerId);
        return inspect.State.Status;
    }
}
