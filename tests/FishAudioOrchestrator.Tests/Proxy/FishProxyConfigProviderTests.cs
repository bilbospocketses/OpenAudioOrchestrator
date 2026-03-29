using FishAudioOrchestrator.Web.Proxy;
using Yarp.ReverseProxy.Configuration;

namespace FishAudioOrchestrator.Tests.Proxy;

public class FishProxyConfigProviderTests
{
    [Fact]
    public void GetConfig_ReturnsRouteForTtsEndpoint()
    {
        var provider = new FishProxyConfigProvider();
        var config = provider.GetConfig();

        var route = Assert.Single(config.Routes);
        Assert.Equal("tts-route", route.RouteId);
        Assert.Equal("/api/tts/{**catch-all}", route.Match.Path);
        Assert.Equal("fish-tts-cluster", route.ClusterId);
    }

    [Fact]
    public void GetConfig_InitiallyHasNoDestination()
    {
        var provider = new FishProxyConfigProvider();
        var config = provider.GetConfig();

        var cluster = Assert.Single(config.Clusters);
        Assert.Equal("fish-tts-cluster", cluster.ClusterId);
        Assert.Null(cluster.Destinations);
    }

    [Fact]
    public void UpdateDestination_ChangesClusterDestination()
    {
        var provider = new FishProxyConfigProvider();
        provider.UpdateDestination("http://172.18.0.2:8080");

        var config = provider.GetConfig();
        var cluster = Assert.Single(config.Clusters);
        Assert.NotNull(cluster.Destinations);
        Assert.True(cluster.Destinations!.ContainsKey("active-model"));
        Assert.Equal("http://172.18.0.2:8080", cluster.Destinations["active-model"].Address);
    }

    [Fact]
    public void UpdateDestination_TriggersConfigChange()
    {
        var provider = new FishProxyConfigProvider();
        var initialToken = provider.GetConfig().ChangeToken;

        provider.UpdateDestination("http://localhost:9001");

        Assert.True(initialToken.HasChanged);
    }

    [Fact]
    public void ClearDestination_RemovesClusterDestination()
    {
        var provider = new FishProxyConfigProvider();
        provider.UpdateDestination("http://localhost:9001");
        provider.ClearDestination();

        var config = provider.GetConfig();
        var cluster = Assert.Single(config.Clusters);
        Assert.Null(cluster.Destinations);
    }
}
