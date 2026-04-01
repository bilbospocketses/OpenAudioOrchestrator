using FishAudioOrchestrator.Web.Hubs;
using FishAudioOrchestrator.Web.Services;
using Docker.DotNet;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace FishAudioOrchestrator.Tests.SignalR;

public class ContainerLogServiceTests
{
    private static (ContainerLogService service, Mock<IHubContext<OrchestratorHub>> hubMock) CreateService()
    {
        var dockerMock = new Mock<IDockerClient>();
        var hubMock = new Mock<IHubContext<OrchestratorHub>>();
        var clientsMock = new Mock<IHubClients>();
        var singleClientProxyMock = new Mock<ISingleClientProxy>();
        var clientProxyMock = new Mock<IClientProxy>();
        clientsMock.Setup(c => c.Client(It.IsAny<string>())).Returns(singleClientProxyMock.Object);
        clientsMock.Setup(c => c.Clients(It.IsAny<IReadOnlyList<string>>())).Returns(clientProxyMock.Object);
        hubMock.Setup(h => h.Clients).Returns(clientsMock.Object);

        var service = new ContainerLogService(dockerMock.Object, hubMock.Object, NullLogger<ContainerLogService>.Instance);
        return (service, hubMock);
    }

    [Fact]
    public async Task Subscribe_AddsConnectionToContainer()
    {
        var (service, _) = CreateService();
        await service.SubscribeAsync("container-1", "conn-a");
        Assert.True(service.HasSubscribers("container-1"));
    }

    [Fact]
    public async Task Unsubscribe_RemovesConnectionFromContainer()
    {
        var (service, _) = CreateService();
        await service.SubscribeAsync("container-1", "conn-a");
        await service.UnsubscribeAsync("container-1", "conn-a");
        Assert.False(service.HasSubscribers("container-1"));
    }

    [Fact]
    public async Task UnsubscribeAll_RemovesConnectionFromAllContainers()
    {
        var (service, _) = CreateService();
        await service.SubscribeAsync("container-1", "conn-a");
        await service.SubscribeAsync("container-2", "conn-a");
        await service.UnsubscribeAllAsync("conn-a");
        Assert.False(service.HasSubscribers("container-1"));
        Assert.False(service.HasSubscribers("container-2"));
    }

    [Fact]
    public async Task MultipleSubscribers_StreamSurvivesPartialUnsubscribe()
    {
        var (service, _) = CreateService();
        await service.SubscribeAsync("container-1", "conn-a");
        await service.SubscribeAsync("container-1", "conn-b");
        await service.UnsubscribeAsync("container-1", "conn-a");
        Assert.True(service.HasSubscribers("container-1"));
    }

    [Fact]
    public void SubscribeCallback_AddsCallback()
    {
        var (service, _) = CreateService();
        Action<LogLineEvent> callback = _ => { };

        service.SubscribeCallback("container-1", "sub-1", callback);

        Assert.True(service.HasSubscribers("container-1"));
    }

    [Fact]
    public void UnsubscribeCallback_RemovesCallback()
    {
        var (service, _) = CreateService();
        Action<LogLineEvent> callback = _ => { };

        service.SubscribeCallback("container-1", "sub-1", callback);
        Assert.True(service.HasSubscribers("container-1"));

        service.UnsubscribeCallback("container-1", "sub-1");
        Assert.False(service.HasSubscribers("container-1"));
    }

    [Fact]
    public void UnsubscribeAllCallbacks_RemovesAllForContainer()
    {
        var (service, _) = CreateService();
        Action<LogLineEvent> callback1 = _ => { };
        Action<LogLineEvent> callback2 = _ => { };

        service.SubscribeCallback("container-1", "sub-1", callback1);
        service.SubscribeCallback("container-2", "sub-1", callback2);
        Assert.True(service.HasSubscribers("container-1"));
        Assert.True(service.HasSubscribers("container-2"));

        service.UnsubscribeAllCallbacks("sub-1");
        Assert.False(service.HasSubscribers("container-1"));
        Assert.False(service.HasSubscribers("container-2"));
    }
}
