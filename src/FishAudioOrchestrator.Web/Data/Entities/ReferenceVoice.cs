namespace FishAudioOrchestrator.Web.Data.Entities;

public class ReferenceVoice
{
    public int Id { get; set; }
    public required string VoiceId { get; set; }
    public required string DisplayName { get; set; }
    public required string AudioFileName { get; set; }
    public required string TranscriptText { get; set; }
    public string? Tags { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; }
}
