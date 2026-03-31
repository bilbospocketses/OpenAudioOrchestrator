using FishAudioOrchestrator.Web.Hubs;

namespace FishAudioOrchestrator.Web.Services;

public class OrchestratorEventBus
{
    public event Action<List<ContainerStatusEvent>>? OnContainerStatus;
    public event Action<TtsNotificationEvent>? OnTtsNotification;
    public event Action<LogLineEvent>? OnLogLine;
    public event Action<GpuMetricsEvent>? OnGpuMetrics;

    public void RaiseContainerStatus(List<ContainerStatusEvent> events)
        => OnContainerStatus?.Invoke(events);

    public void RaiseTtsNotification(TtsNotificationEvent notification)
        => OnTtsNotification?.Invoke(notification);

    public void RaiseLogLine(LogLineEvent logLine)
        => OnLogLine?.Invoke(logLine);

    public void RaiseGpuMetrics(GpuMetricsEvent metrics)
        => OnGpuMetrics?.Invoke(metrics);
}
