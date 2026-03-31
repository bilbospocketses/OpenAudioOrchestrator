namespace FishAudioOrchestrator.Web.Data.Entities;

public enum TtsJobStatus
{
    Queued,
    Processing,
    Completed,
    Failed
}

public class TtsJob
{
    public int Id { get; set; }
    public string? UserId { get; set; }
    public int ModelProfileId { get; set; }
    public int? ReferenceVoiceId { get; set; }
    public required string InputText { get; set; }
    public required string Format { get; set; }
    public string? ReferenceId { get; set; }
    public required string OutputFileName { get; set; }
    public TtsJobStatus Status { get; set; } = TtsJobStatus.Queued;
    public string? ErrorMessage { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? StartedAt { get; set; }
    public DateTime? CompletedAt { get; set; }

    public ModelProfile ModelProfile { get; set; } = null!;
    public ReferenceVoice? ReferenceVoice { get; set; }
    public AppUser? User { get; set; }
}
