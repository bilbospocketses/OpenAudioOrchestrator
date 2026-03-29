using FishAudioOrchestrator.Web.Data;
using FishAudioOrchestrator.Web.Data.Entities;
using FishAudioOrchestrator.Web.Services;
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

        var scopeFactory = CreateScopeFactory(context);
        var service = new HealthMonitorService(
            scopeFactory, mockTts.Object, CreateConfig(),
            NullLogger<HealthMonitorService>.Instance);

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

        var scopeFactory = CreateScopeFactory(context);
        var service = new HealthMonitorService(
            scopeFactory, mockTts.Object, CreateConfig(),
            NullLogger<HealthMonitorService>.Instance);

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
        var scopeFactory = CreateScopeFactory(context);
        var service = new HealthMonitorService(
            scopeFactory, mockTts.Object, CreateConfig(),
            NullLogger<HealthMonitorService>.Instance);

        await service.CheckHealthAsync();

        mockTts.Verify(t => t.GetHealthAsync(It.IsAny<string>()), Times.Never);
    }

    private static IServiceScopeFactory CreateScopeFactory(AppDbContext context)
    {
        var mockScope = new Mock<IServiceScope>();
        var mockProvider = new Mock<IServiceProvider>();
        mockProvider.Setup(p => p.GetService(typeof(AppDbContext))).Returns(context);
        mockScope.Setup(s => s.ServiceProvider).Returns(mockProvider.Object);

        var mockFactory = new Mock<IServiceScopeFactory>();
        mockFactory.Setup(f => f.CreateScope()).Returns(mockScope.Object);
        return mockFactory.Object;
    }
}
