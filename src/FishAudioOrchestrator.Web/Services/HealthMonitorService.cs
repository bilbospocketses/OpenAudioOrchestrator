using FishAudioOrchestrator.Web.Data;
using FishAudioOrchestrator.Web.Data.Entities;
using FishAudioOrchestrator.Web.Hubs;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace FishAudioOrchestrator.Web.Services;

public class HealthMonitorService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ITtsClientService _ttsClient;
    private readonly int _intervalSeconds;
    private readonly ILogger<HealthMonitorService> _logger;
    private readonly IHubContext<OrchestratorHub> _hub;
    private readonly GpuMetricsState _gpuState;
    private int _consecutiveFailures;

    public HealthMonitorService(
        IServiceScopeFactory scopeFactory,
        ITtsClientService ttsClient,
        IConfiguration config,
        ILogger<HealthMonitorService> logger,
        IHubContext<OrchestratorHub> hub,
        GpuMetricsState gpuState)
    {
        _scopeFactory = scopeFactory;
        _ttsClient = ttsClient;
        _intervalSeconds = int.Parse(
            config["FishOrchestrator:HealthCheckIntervalSeconds"] ?? "30");
        _logger = logger;
        _hub = hub;
        _gpuState = gpuState;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            await Task.Delay(TimeSpan.FromSeconds(_intervalSeconds), stoppingToken);
            await CheckHealthAsync();
        }
    }

    public async Task CheckHealthAsync()
    {
        using var scope = _scopeFactory.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var running = await context.ModelProfiles
            .FirstOrDefaultAsync(m => m.Status == ModelStatus.Running);

        if (running is null)
        {
            _consecutiveFailures = 0;
            return;
        }

        var baseUrl = $"http://localhost:{running.HostPort}";
        var healthy = await _ttsClient.GetHealthAsync(baseUrl);

        if (healthy)
        {
            _consecutiveFailures = 0;
            if (running.Status != ModelStatus.Running)
            {
                running.Status = ModelStatus.Running;
                await context.SaveChangesAsync();
            }
        }
        else
        {
            _consecutiveFailures++;
            _logger.LogWarning(
                "Health check failed for {ModelName} ({Failures} consecutive)",
                running.Name, _consecutiveFailures);

            running.Status = ModelStatus.Error;
            await context.SaveChangesAsync();
        }

        // Push container status for all models
        var allModels = await context.ModelProfiles.ToListAsync();
        var statusEvents = allModels.Select(m => new ContainerStatusEvent(
            m.Id, m.Name, m.Status.ToString(), m.HostPort, m.LastStartedAt)).ToList();
        await _hub.Clients.All.SendAsync("ReceiveContainerStatus", statusEvents);

        // Collect and push GPU metrics
        var gpuMetrics = await GpuMetricsParser.CollectAsync();
        if (gpuMetrics is not null)
        {
            _gpuState.Update(gpuMetrics);
            await _hub.Clients.All.SendAsync("ReceiveGpuMetrics", gpuMetrics);
        }
    }
}
