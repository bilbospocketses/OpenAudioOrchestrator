using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;
using FishAudioOrchestrator.Web.Data;
using FishAudioOrchestrator.Web.Data.Entities;
using FishAudioOrchestrator.Web.Hubs;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Configuration;

namespace FishAudioOrchestrator.Web.Services;

public class TtsClientService : ITtsClientService
{
    private readonly HttpClient _httpClient;
    private readonly string _outputPath;
    private readonly AppDbContext _context;
    private readonly IHubContext<OrchestratorHub> _hub;

    public TtsClientService(HttpClient httpClient, IConfiguration config, AppDbContext context, IHubContext<OrchestratorHub> hub)
    {
        _httpClient = httpClient;
        var dataRoot = config["FishOrchestrator:DataRoot"]!;
        _outputPath = Path.Combine(dataRoot, "Output");
        _context = context;
        _hub = hub;
    }

    public async Task<TtsResult> GenerateAsync(string containerBaseUrl, TtsRequest request,
        int modelProfileId, int? referenceVoiceId)
    {
        var outputFileName = GenerateOutputFileName(request.Format);
        var outputFilePath = Path.Combine(_outputPath, outputFileName);

        var json = BuildRequestJson(request);
        var content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");

        var sw = Stopwatch.StartNew();
        var response = await _httpClient.PostAsync(
            $"{containerBaseUrl.TrimEnd('/')}/v1/tts", content);
        response.EnsureSuccessStatusCode();
        sw.Stop();

        await using (var fs = File.Create(outputFilePath))
        {
            await response.Content.CopyToAsync(fs);
        }

        await SaveGenerationLogAsync(modelProfileId, referenceVoiceId,
            request.Text, outputFileName, request.Format, sw.ElapsedMilliseconds);

        var notification = new TtsNotificationEvent(
            null,
            request.Text.Length > 50 ? request.Text[..50] + "..." : request.Text,
            outputFileName,
            sw.ElapsedMilliseconds,
            true,
            null);
        await _hub.Clients.All.SendAsync("ReceiveTtsNotification", notification);

        return new TtsResult
        {
            OutputFileName = outputFileName,
            OutputPath = outputFilePath,
            DurationMs = sw.ElapsedMilliseconds
        };
    }

    public async Task<bool> GetHealthAsync(string baseUrl)
    {
        try
        {
            var response = await _httpClient.GetAsync($"{baseUrl.TrimEnd('/')}/v1/health");
            return response.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }

    public async Task<GenerationLog> SaveGenerationLogAsync(int modelProfileId, int? referenceVoiceId,
        string inputText, string outputFileName, string format, long durationMs)
    {
        var log = new GenerationLog
        {
            ModelProfileId = modelProfileId,
            ReferenceVoiceId = referenceVoiceId,
            InputText = inputText,
            OutputFileName = outputFileName,
            Format = format,
            DurationMs = durationMs
        };

        _context.GenerationLogs.Add(log);
        await _context.SaveChangesAsync();
        return log;
    }

    public static string BuildRequestJson(TtsRequest request)
    {
        var options = new JsonSerializerOptions
        {
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
        };

        var body = new Dictionary<string, object?>
        {
            ["text"] = request.Text,
            ["format"] = request.Format,
            ["streaming"] = false
        };

        if (request.ReferenceId is not null)
        {
            body["reference_id"] = request.ReferenceId;
        }

        return JsonSerializer.Serialize(body, options);
    }

    public static string GenerateOutputFileName(string format)
    {
        return $"gen_{DateTime.UtcNow:yyyyMMdd_HHmmss}_{Guid.NewGuid().ToString("N")[..8]}.{format}";
    }
}
