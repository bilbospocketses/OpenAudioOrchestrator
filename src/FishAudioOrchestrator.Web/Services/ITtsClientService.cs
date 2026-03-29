using FishAudioOrchestrator.Web.Data.Entities;

namespace FishAudioOrchestrator.Web.Services;

public class TtsRequest
{
    public required string Text { get; set; }
    public string? ReferenceId { get; set; }
    public string Format { get; set; } = "wav";
}

public class TtsResult
{
    public required string OutputFileName { get; set; }
    public required string OutputPath { get; set; }
    public long DurationMs { get; set; }
}

public interface ITtsClientService
{
    Task<TtsResult> GenerateAsync(string containerBaseUrl, TtsRequest request,
        int modelProfileId, int? referenceVoiceId);
    Task<bool> GetHealthAsync(string baseUrl);
    Task<GenerationLog> SaveGenerationLogAsync(int modelProfileId, int? referenceVoiceId,
        string inputText, string outputFileName, string format, long durationMs);
}
