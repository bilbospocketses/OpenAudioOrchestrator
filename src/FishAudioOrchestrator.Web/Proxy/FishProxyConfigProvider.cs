using Microsoft.Extensions.Primitives;
using Yarp.ReverseProxy.Configuration;

namespace FishAudioOrchestrator.Web.Proxy;

public class FishProxyConfigProvider : IProxyConfigProvider
{
    private volatile FishProxyConfig _config;
    private CancellationTokenSource _cts = new();

    public FishProxyConfigProvider()
    {
        _config = new FishProxyConfig(null, _cts.Token);
    }

    public IProxyConfig GetConfig() => _config;

    public virtual void UpdateDestination(string destinationUrl)
    {
        var oldCts = _cts;
        _cts = new CancellationTokenSource();

        var destinations = new Dictionary<string, DestinationConfig>
        {
            ["active-model"] = new DestinationConfig { Address = destinationUrl }
        };

        _config = new FishProxyConfig(destinations, _cts.Token);
        oldCts.Cancel();
    }

    public virtual void ClearDestination()
    {
        var oldCts = _cts;
        _cts = new CancellationTokenSource();
        _config = new FishProxyConfig(null, _cts.Token);
        oldCts.Cancel();
    }

    private class FishProxyConfig : IProxyConfig
    {
        private readonly CancellationChangeToken _changeToken;

        public FishProxyConfig(
            IReadOnlyDictionary<string, DestinationConfig>? destinations,
            CancellationToken cancellationToken)
        {
            _changeToken = new CancellationChangeToken(cancellationToken);

            Routes = new[]
            {
                new RouteConfig
                {
                    RouteId = "tts-route",
                    ClusterId = "fish-tts-cluster",
                    Match = new RouteMatch { Path = "/api/tts/{**catch-all}" }
                }
            };

            Clusters = new[]
            {
                new ClusterConfig
                {
                    ClusterId = "fish-tts-cluster",
                    Destinations = destinations
                }
            };
        }

        public IReadOnlyList<RouteConfig> Routes { get; }
        public IReadOnlyList<ClusterConfig> Clusters { get; }
        public IChangeToken ChangeToken => _changeToken;
    }
}
