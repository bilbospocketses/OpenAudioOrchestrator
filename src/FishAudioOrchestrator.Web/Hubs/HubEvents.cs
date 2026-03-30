namespace FishAudioOrchestrator.Web.Hubs;

public record ContainerStatusEvent(
    int ModelId,
    string Name,
    string Status,
    int HostPort,
    DateTime? LastStartedAt);

public record GpuMetricsEvent(
    int MemoryUsedMb,
    int MemoryTotalMb,
    int UtilizationPercent);

public record TtsNotificationEvent(
    string? UserId,
    string Text,
    string OutputFileName,
    long DurationMs,
    bool Success,
    string? Error);

public record LogLineEvent(
    string ContainerId,
    DateTime Timestamp,
    string Line);
