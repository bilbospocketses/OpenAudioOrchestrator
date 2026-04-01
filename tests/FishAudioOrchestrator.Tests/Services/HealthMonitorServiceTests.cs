using Docker.DotNet;
using Docker.DotNet.Models;
using FishAudioOrchestrator.Web.Data;
using FishAudioOrchestrator.Web.Data.Entities;
using FishAudioOrchestrator.Web.Hubs;
using FishAudioOrchestrator.Web.Services;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace FishAudioOrchestrator.Tests.Services;

public class HealthMonitorServiceTests
{
    private static AppDbContext CreateInMemoryContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        var ctx = new AppDbContext(options);
        ctx.Database.EnsureCreated();
        return ctx;
    }

    private static IConfiguration CreateConfig(int intervalSeconds = 1)
    {
        return new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["FishOrchestrator:HealthCheckIntervalSeconds"] = intervalSeconds.ToString()
            })
            .Build();
    }

    [Fact]
    public async Task CheckHealthAsync_SetsErrorOnFailure()
    {
        var context = CreateInMemoryContext();
        var model = new ModelProfile
        {
            Name = "unhealthy",
            CheckpointPath = @"D:\path",
            ImageTag = "fishaudio/fish-speech:server-cuda",
            HostPort = 9001,
            ContainerId = "container-1",
            Status = ModelStatus.Running
        };
        context.ModelProfiles.Add(model);
        await context.SaveChangesAsync();

        var mockTts = new Mock<ITtsClientService>();
        mockTts.Setup(t => t.GetHealthAsync(It.IsAny<string>()))
            .ReturnsAsync(false);

        var mockDocker = new Mock<IDockerClient>();
        var scopeFactory = CreateScopeFactory(context, mockTts.Object);
        var (hubMock, gpuState) = CreateHubMocks();
        var service = new HealthMonitorService(
            scopeFactory, mockDocker.Object, CreateConfig(),
            NullLogger<HealthMonitorService>.Instance, hubMock.Object, gpuState, new OrchestratorEventBus());

        // Need 5 consecutive failures to set Error status
        for (int i = 0; i < 5; i++)
            await service.CheckHealthAsync();

        var updated = await context.ModelProfiles.FirstAsync(m => m.Name == "unhealthy");
        Assert.Equal(ModelStatus.Error, updated.Status);
    }

    [Fact]
    public async Task CheckHealthAsync_ConfirmsRunningOnSuccess()
    {
        var context = CreateInMemoryContext();
        var model = new ModelProfile
        {
            Name = "healthy",
            CheckpointPath = @"D:\path",
            ImageTag = "fishaudio/fish-speech:server-cuda",
            HostPort = 9001,
            ContainerId = "container-1",
            Status = ModelStatus.Running
        };
        context.ModelProfiles.Add(model);
        await context.SaveChangesAsync();

        var mockTts = new Mock<ITtsClientService>();
        mockTts.Setup(t => t.GetHealthAsync(It.IsAny<string>()))
            .ReturnsAsync(true);

        var mockDocker = new Mock<IDockerClient>();
        var scopeFactory = CreateScopeFactory(context, mockTts.Object);
        var (hubMock, gpuState) = CreateHubMocks();
        var service = new HealthMonitorService(
            scopeFactory, mockDocker.Object, CreateConfig(),
            NullLogger<HealthMonitorService>.Instance, hubMock.Object, gpuState, new OrchestratorEventBus());

        await service.CheckHealthAsync();

        var updated = await context.ModelProfiles.FirstAsync(m => m.Name == "healthy");
        Assert.Equal(ModelStatus.Running, updated.Status);
    }

    [Fact]
    public async Task CheckHealthAsync_DoesNothingWhenNoRunningModel()
    {
        var context = CreateInMemoryContext();
        var model = new ModelProfile
        {
            Name = "stopped",
            CheckpointPath = @"D:\path",
            ImageTag = "fishaudio/fish-speech:server-cuda",
            HostPort = 9001,
            Status = ModelStatus.Stopped
        };
        context.ModelProfiles.Add(model);
        await context.SaveChangesAsync();

        var mockTts = new Mock<ITtsClientService>();
        var mockDocker = new Mock<IDockerClient>();
        var scopeFactory = CreateScopeFactory(context, mockTts.Object);
        var (hubMock, gpuState) = CreateHubMocks();
        var service = new HealthMonitorService(
            scopeFactory, mockDocker.Object, CreateConfig(),
            NullLogger<HealthMonitorService>.Instance, hubMock.Object, gpuState, new OrchestratorEventBus());

        await service.CheckHealthAsync();

        mockTts.Verify(t => t.GetHealthAsync(It.IsAny<string>()), Times.Never);
    }

    private static IServiceScopeFactory CreateScopeFactory(AppDbContext context, ITtsClientService? ttsClient = null)
    {
        var mockScope = new Mock<IServiceScope>();
        var mockProvider = new Mock<IServiceProvider>();
        mockProvider.Setup(p => p.GetService(typeof(AppDbContext))).Returns(context);
        if (ttsClient is not null)
            mockProvider.Setup(p => p.GetService(typeof(ITtsClientService))).Returns(ttsClient);
        mockScope.Setup(s => s.ServiceProvider).Returns(mockProvider.Object);

        var mockFactory = new Mock<IServiceScopeFactory>();
        mockFactory.Setup(f => f.CreateScope()).Returns(mockScope.Object);
        return mockFactory.Object;
    }

    private static (Mock<IHubContext<OrchestratorHub>> hubMock, GpuMetricsState gpuState) CreateHubMocks()
    {
        var hubMock = new Mock<IHubContext<OrchestratorHub>>();
        var clientsMock = new Mock<IHubClients>();
        var clientProxyMock = new Mock<IClientProxy>();
        clientsMock.Setup(c => c.All).Returns(clientProxyMock.Object);
        hubMock.Setup(h => h.Clients).Returns(clientsMock.Object);
        return (hubMock, new GpuMetricsState());
    }
}
