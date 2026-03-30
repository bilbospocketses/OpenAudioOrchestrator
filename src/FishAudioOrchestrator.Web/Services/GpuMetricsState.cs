using FishAudioOrchestrator.Web.Hubs;

namespace FishAudioOrchestrator.Web.Services;

public class GpuMetricsState
{
    public int MemoryUsedMb { get; private set; }
    public int MemoryTotalMb { get; private set; }
    public int UtilizationPercent { get; private set; }
    public DateTime LastUpdated { get; private set; }

    public event Action? OnChange;

    public void Update(GpuMetricsEvent metrics)
    {
        MemoryUsedMb = metrics.MemoryUsedMb;
        MemoryTotalMb = metrics.MemoryTotalMb;
        UtilizationPercent = metrics.UtilizationPercent;
        LastUpdated = DateTime.UtcNow;
        OnChange?.Invoke();
    }
}
