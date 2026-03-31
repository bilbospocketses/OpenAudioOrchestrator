using System.Diagnostics;
using FishAudioOrchestrator.Web.Data;
using FishAudioOrchestrator.Web.Data.Entities;
using FishAudioOrchestrator.Web.Hubs;
using Microsoft.EntityFrameworkCore;

namespace FishAudioOrchestrator.Web.Services;

public class TtsJobProcessor : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly OrchestratorEventBus _eventBus;
    private readonly ILogger<TtsJobProcessor> _logger;
    private readonly string _outputPath;

    public TtsJobProcessor(
        IServiceScopeFactory scopeFactory,
        OrchestratorEventBus eventBus,
        ILogger<TtsJobProcessor> logger,
        IConfiguration config)
    {
        _scopeFactory = scopeFactory;
        _eventBus = eventBus;
        _logger = logger;
        var dataRoot = config["FishOrchestrator:DataRoot"] ?? @"D:\DockerData\FishAudio";
        _outputPath = Path.Combine(dataRoot, "Output");
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await RecoverInterruptedJobsAsync();

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ProcessNextJobAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in TTS job processor loop");
            }

            await Task.Delay(2000, stoppingToken);
        }
    }

    private async Task RecoverInterruptedJobsAsync()
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var interruptedJobs = await db.TtsJobs
            .Where(j => j.Status == TtsJobStatus.Processing)
            .ToListAsync();

        foreach (var job in interruptedJobs)
        {
            var filePath = Path.Combine(_outputPath, job.OutputFileName);
            if (File.Exists(filePath) && new FileInfo(filePath).Length > 0)
            {
                _logger.LogInformation("Recovering completed job {JobId} — output file exists", job.Id);
                PromoteToGenerationLog(db, job);
            }
            else
            {
                _logger.LogInformation("Re-queuing interrupted job {JobId}", job.Id);
                job.Status = TtsJobStatus.Queued;
                job.StartedAt = null;
            }
        }

        await db.SaveChangesAsync();
    }

    private async Task ProcessNextJobAsync(CancellationToken stoppingToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var job = await db.TtsJobs
            .OrderBy(j => j.CreatedAt)
            .FirstOrDefaultAsync(j => j.Status == TtsJobStatus.Queued, stoppingToken);

        if (job is null) return;

        job.Status = TtsJobStatus.Processing;
        job.StartedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(stoppingToken);
        _eventBus.RaiseTtsJobStatus(new TtsJobStatusEvent(job.Id, "Processing", null));

        var model = await db.ModelProfiles
            .FirstOrDefaultAsync(m => m.Status == ModelStatus.Running, stoppingToken);

        if (model is null)
        {
            job.Status = TtsJobStatus.Failed;
            job.ErrorMessage = "No running model available";
            job.CompletedAt = DateTime.UtcNow;
            await db.SaveChangesAsync(stoppingToken);
            _eventBus.RaiseTtsJobStatus(new TtsJobStatusEvent(job.Id, "Failed", job.ErrorMessage));
            return;
        }

        var baseUrl = $"http://localhost:{model.HostPort}";
        var request = new TtsRequest
        {
            Text = job.InputText,
            ReferenceId = job.ReferenceId,
            Format = job.Format
        };

        try
        {
            var ttsClient = scope.ServiceProvider.GetRequiredService<ITtsClientService>();
            var result = await ttsClient.GenerateAsync(baseUrl, request,
                job.ModelProfileId, job.ReferenceVoiceId);

            db.TtsJobs.Remove(job);
            await db.SaveChangesAsync(stoppingToken);
            _eventBus.RaiseTtsJobStatus(new TtsJobStatusEvent(job.Id, "Completed", null));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "TTS job {JobId} failed", job.Id);
            job.Status = TtsJobStatus.Failed;
            job.ErrorMessage = ex.Message;
            job.CompletedAt = DateTime.UtcNow;
            await db.SaveChangesAsync(stoppingToken);
            _eventBus.RaiseTtsJobStatus(new TtsJobStatusEvent(job.Id, "Failed", ex.Message));
        }
    }

    private void PromoteToGenerationLog(AppDbContext db, TtsJob job)
    {
        var log = new GenerationLog
        {
            ModelProfileId = job.ModelProfileId,
            ReferenceVoiceId = job.ReferenceVoiceId,
            UserId = job.UserId,
            InputText = job.InputText,
            OutputFileName = job.OutputFileName,
            Format = job.Format,
            DurationMs = 0,
            CreatedAt = job.CreatedAt
        };

        db.GenerationLogs.Add(log);
        db.TtsJobs.Remove(job);

        _eventBus.RaiseTtsJobStatus(new TtsJobStatusEvent(job.Id, "Completed", null));

        var notification = new TtsNotificationEvent(
            job.UserId,
            job.InputText.Length > 50 ? job.InputText[..50] + "..." : job.InputText,
            job.OutputFileName,
            0,
            true,
            null);
        _eventBus.RaiseTtsNotification(notification);
    }
}
