using FishAudioOrchestrator.Web.Data;
using FishAudioOrchestrator.Web.Data.Entities;
using FishAudioOrchestrator.Web.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace FishAudioOrchestrator.Tests.Services;

public class TtsClientServiceTests : IDisposable
{
    private readonly string _testDataRoot;
    private readonly AppDbContext _context;

    public TtsClientServiceTests()
    {
        _testDataRoot = Path.Combine(Path.GetTempPath(), $"fish-tts-test-{Guid.NewGuid()}");
        Directory.CreateDirectory(Path.Combine(_testDataRoot, "Output"));

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        _context = new AppDbContext(options);
        _context.Database.EnsureCreated();
    }

    public void Dispose()
    {
        _context.Dispose();
        if (Directory.Exists(_testDataRoot))
            Directory.Delete(_testDataRoot, true);
    }

    private IConfiguration CreateConfig()
    {
        return new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["FishOrchestrator:DataRoot"] = _testDataRoot
            })
            .Build();
    }

    [Fact]
    public void BuildTtsRequestBody_IncludesRequiredFields()
    {
        var request = new TtsRequest
        {
            Text = "Hello world",
            ReferenceId = "narrator",
            Format = "wav"
        };

        var json = TtsClientService.BuildRequestJson(request);

        Assert.Contains("\"text\"", json);
        Assert.Contains("Hello world", json);
        Assert.Contains("\"reference_id\"", json);
        Assert.Contains("narrator", json);
        Assert.Contains("\"format\"", json);
        Assert.Contains("wav", json);
    }

    [Fact]
    public void BuildTtsRequestBody_OmitsReferenceIdWhenNull()
    {
        var request = new TtsRequest
        {
            Text = "No voice",
            ReferenceId = null,
            Format = "mp3"
        };

        var json = TtsClientService.BuildRequestJson(request);

        Assert.Contains("No voice", json);
        Assert.DoesNotContain("reference_id", json);
        Assert.Contains("mp3", json);
    }

    [Fact]
    public async Task SaveGenerationLogAsync_CreatesDbRecord()
    {
        var model = new ModelProfile
        {
            Name = "test-model", CheckpointPath = @"D:\path",
            ImageTag = "fishaudio/fish-speech:server-cuda",
            HostPort = 9001, Status = ModelStatus.Running
        };
        _context.ModelProfiles.Add(model);
        await _context.SaveChangesAsync();

        var service = new TtsClientService(new HttpClient(), CreateConfig(), _context);

        var log = await service.SaveGenerationLogAsync(
            modelProfileId: model.Id, referenceVoiceId: null,
            inputText: "Test generation", outputFileName: "gen_001.wav",
            format: "wav", durationMs: 1234);

        var retrieved = await _context.GenerationLogs.FirstAsync();
        Assert.Equal(model.Id, retrieved.ModelProfileId);
        Assert.Equal("Test generation", retrieved.InputText);
        Assert.Equal("gen_001.wav", retrieved.OutputFileName);
        Assert.Equal(1234, retrieved.DurationMs);
    }

    [Fact]
    public void GenerateOutputFileName_IncludesTimestamp()
    {
        var fileName = TtsClientService.GenerateOutputFileName("wav");

        Assert.StartsWith("gen_", fileName);
        Assert.EndsWith(".wav", fileName);
        Assert.True(fileName.Length > 10);
    }
}
