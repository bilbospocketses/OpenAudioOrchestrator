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
    private static readonly TimeSpan JobTimeout = TimeSpan.FromHours(2);
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(5);

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
        await RecoverInterruptedJobsAsync(stoppingToken);

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

    private async Task RecoverInterruptedJobsAsync(CancellationToken stoppingToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var interruptedJobs = await db.TtsJobs
            .Include(j => j.ModelProfile)
            .Where(j => j.Status == TtsJobStatus.Processing)
            .ToListAsync(stoppingToken);

        foreach (var job in interruptedJobs)
        {
            var filePath = Path.Combine(_outputPath, job.OutputFileName);

            if (File.Exists(filePath) && new FileInfo(filePath).Length > 0)
            {
                // File written during restart — promote to completed
                _logger.LogInformation("Recovering completed job {JobId} — output file exists", job.Id);
                PromoteToGenerationLog(db, job);
                await db.SaveChangesAsync(CancellationToken.None);
                continue;
            }

            // Check if curl is still running inside the container
            if (job.ModelProfile?.ContainerId is not null && await IsCurlRunningAsync(job.ModelProfile.ContainerId))
            {
                _logger.LogInformation("Job {JobId} still processing in container — resuming poll", job.Id);
                await PollForOutputFileAsync(db, job, stoppingToken);
                continue;
            }

            // curl not running, file doesn't exist — re-queue
            _logger.LogInformation("Re-queuing interrupted job {JobId}", job.Id);
            job.Status = TtsJobStatus.Queued;
            job.StartedAt = null;
            await db.SaveChangesAsync(CancellationToken.None);
        }
    }

    private async Task ProcessNextJobAsync(CancellationToken stoppingToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var job = await db.TtsJobs
            .OrderBy(j => j.CreatedAt)
            .FirstOrDefaultAsync(j => j.Status == TtsJobStatus.Queued, stoppingToken);

        if (job is null) return;

        // Mark as processing
        job.Status = TtsJobStatus.Processing;
        job.StartedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(CancellationToken.None);
        _eventBus.RaiseTtsJobStatus(new TtsJobStatusEvent(job.Id, "Processing", null));

        // Find the running model
        var model = await db.ModelProfiles
            .FirstOrDefaultAsync(m => m.Status == ModelStatus.Running || m.Status == ModelStatus.Error, stoppingToken);

        if (model?.ContainerId is null)
        {
            await FailJob(db, job, "No running model available");
            return;
        }

        // Build the curl command
        var request = new TtsRequest
        {
            Text = job.InputText,
            ReferenceId = job.ReferenceId,
            Format = job.Format
        };
        var json = TtsClientService.BuildRequestJson(request);
        var containerOutputPath = $"/app/output/{job.OutputFileName}";

        // Escape the JSON for shell (replace single quotes with escaped version)
        var escapedJson = json.Replace("'", "'\\''");

        var dockerArgs = $"exec {model.ContainerId} curl -s -X POST http://localhost:8080/v1/tts " +
                         $"-H \"Content-Type: application/json\" " +
                         $"-d '{escapedJson}' " +
                         $"--output {containerOutputPath} " +
                         $"--max-time 7200";

        _logger.LogInformation("Starting docker exec curl for job {JobId}", job.Id);

        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "docker",
                Arguments = dockerArgs,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            var process = Process.Start(psi);
            if (process is null)
            {
                await FailJob(db, job, "Failed to start docker exec process");
                return;
            }

            // Poll for completion: check file, job status (for cancel), and timeout
            await PollForOutputFileAsync(db, job, stoppingToken, process);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "TTS job {JobId} failed", job.Id);
            await FailJob(db, job, ex.Message);
        }
    }

    private async Task PollForOutputFileAsync(AppDbContext db, TtsJob job,
        CancellationToken stoppingToken, Process? process = null)
    {
        var filePath = Path.Combine(_outputPath, job.OutputFileName);
        var startTime = job.StartedAt ?? DateTime.UtcNow;

        while (!stoppingToken.IsCancellationRequested)
        {
            await Task.Delay(PollInterval, CancellationToken.None);

            // Check if file has been written
            if (File.Exists(filePath) && new FileInfo(filePath).Length > 0)
            {
                _logger.LogInformation("Job {JobId} completed — output file found", job.Id);

                // Wait a moment for file write to finish
                await Task.Delay(1000, CancellationToken.None);

                var duration = (long)(DateTime.UtcNow - startTime).TotalMilliseconds;
                PromoteToGenerationLog(db, job, duration);
                await db.SaveChangesAsync(CancellationToken.None);
                return;
            }

            // Check if job was cancelled via UI
            var currentStatus = await db.TtsJobs
                .Where(j => j.Id == job.Id)
                .Select(j => j.Status)
                .FirstOrDefaultAsync(CancellationToken.None);

            if (currentStatus == TtsJobStatus.Failed || currentStatus == default)
            {
                // Job was cancelled or deleted — kill curl if running
                _logger.LogInformation("Job {JobId} was cancelled", job.Id);
                if (process is not null && !process.HasExited)
                {
                    try { process.Kill(); } catch { }
                }
                return;
            }

            // Check timeout
            if (DateTime.UtcNow - startTime > JobTimeout)
            {
                _logger.LogWarning("Job {JobId} timed out after {Hours} hours", job.Id, JobTimeout.TotalHours);
                if (process is not null && !process.HasExited)
                {
                    try { process.Kill(); } catch { }
                }
                await FailJob(db, job, $"Generation timed out after {JobTimeout.TotalHours} hours");
                return;
            }

            // Check if docker exec process exited without producing a file
            if (process is not null && process.HasExited && process.ExitCode != 0)
            {
                var stderr = await process.StandardError.ReadToEndAsync();
                _logger.LogError("docker exec failed for job {JobId}: {Error}", job.Id, stderr);
                await FailJob(db, job, $"docker exec failed: {stderr}");
                return;
            }
        }
    }

    /// <summary>
    /// Cancels a processing job by killing curl inside the container.
    /// Called from the UI via the event bus or directly.
    /// </summary>
    public async Task CancelJobAsync(int jobId)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var job = await db.TtsJobs
            .Include(j => j.ModelProfile)
            .FirstOrDefaultAsync(j => j.Id == jobId);

        if (job is null) return;

        // Kill curl inside the container
        if (job.ModelProfile?.ContainerId is not null)
        {
            await KillCurlInContainerAsync(job.ModelProfile.ContainerId);
        }

        // Clean up partial output file
        var filePath = Path.Combine(_outputPath, job.OutputFileName);
        if (File.Exists(filePath))
        {
            try { File.Delete(filePath); } catch { }
        }

        job.Status = TtsJobStatus.Failed;
        job.ErrorMessage = "Cancelled by user";
        job.CompletedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(CancellationToken.None);
        _eventBus.RaiseTtsJobStatus(new TtsJobStatusEvent(job.Id, "Failed", job.ErrorMessage));
    }

    private async Task FailJob(AppDbContext db, TtsJob job, string error)
    {
        try
        {
            job.Status = TtsJobStatus.Failed;
            job.ErrorMessage = error;
            job.CompletedAt = DateTime.UtcNow;
            await db.SaveChangesAsync(CancellationToken.None);
            _eventBus.RaiseTtsJobStatus(new TtsJobStatusEvent(job.Id, "Failed", error));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to update job {JobId} status to Failed", job.Id);
        }
    }

    private void PromoteToGenerationLog(AppDbContext db, TtsJob job, long durationMs = 0)
    {
        var log = new GenerationLog
        {
            ModelProfileId = job.ModelProfileId,
            ReferenceVoiceId = job.ReferenceVoiceId,
            UserId = job.UserId,
            InputText = job.InputText,
            OutputFileName = job.OutputFileName,
            Format = job.Format,
            DurationMs = durationMs,
            CreatedAt = job.CreatedAt
        };

        db.GenerationLogs.Add(log);
        db.TtsJobs.Remove(job);

        _eventBus.RaiseTtsJobStatus(new TtsJobStatusEvent(job.Id, "Completed", null));

        var notification = new TtsNotificationEvent(
            job.UserId,
            job.InputText.Length > 50 ? job.InputText[..50] + "..." : job.InputText,
            job.OutputFileName,
            durationMs,
            true,
            null);
        _eventBus.RaiseTtsNotification(notification);
    }

    private static async Task<bool> IsCurlRunningAsync(string containerId)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "docker",
                Arguments = $"exec {containerId} pgrep -f curl",
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            var process = Process.Start(psi);
            if (process is null) return false;
            await process.WaitForExitAsync();
            return process.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }

    private static async Task KillCurlInContainerAsync(string containerId)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "docker",
                Arguments = $"exec {containerId} pkill -f curl",
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            var process = Process.Start(psi);
            if (process is not null)
                await process.WaitForExitAsync();
        }
        catch { }
    }
}
