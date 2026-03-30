namespace FishAudioOrchestrator.Web.Services;

public interface IContainerLogService
{
    Task SubscribeAsync(string containerId, string connectionId);
    Task UnsubscribeAsync(string containerId, string connectionId);
    Task UnsubscribeAllAsync(string connectionId);
    bool HasSubscribers(string containerId);
}
