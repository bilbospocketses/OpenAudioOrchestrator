using Docker.DotNet;
using FishAudioOrchestrator.Web.Components;
using FishAudioOrchestrator.Web.Data;
using FishAudioOrchestrator.Web.Proxy;
using FishAudioOrchestrator.Web.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.FileProviders;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseSqlite(builder.Configuration.GetConnectionString("Default")));

// Docker client
builder.Services.AddSingleton<IDockerClient>(_ =>
{
    var endpoint = builder.Configuration["FishOrchestrator:DockerEndpoint"]
        ?? "npipe://./pipe/docker_engine";
    return new DockerClientConfiguration(new Uri(endpoint)).CreateClient();
});

// YARP reverse proxy
var proxyProvider = new FishProxyConfigProvider();
builder.Services.AddSingleton(proxyProvider);
builder.Services.AddReverseProxy();

// Application services
builder.Services.AddScoped<IContainerConfigService, ContainerConfigService>();
builder.Services.AddSingleton<IDockerNetworkService, DockerNetworkService>();
builder.Services.AddScoped<IDockerOrchestratorService, DockerOrchestratorService>();
builder.Services.AddScoped<IVoiceLibraryService, VoiceLibraryService>();
builder.Services.AddHttpClient<ITtsClientService, TtsClientService>();

// Health monitoring
builder.Services.AddHostedService<HealthMonitorService>();

var app = builder.Build();

// Run migrations
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    await db.Database.MigrateAsync();
}

// Ensure Docker bridge network exists
try
{
    var networkService = app.Services.GetRequiredService<IDockerNetworkService>();
    await networkService.EnsureNetworkExistsAsync();
}
catch (Exception ex)
{
    app.Logger.LogWarning(ex, "Could not ensure Docker bridge network exists. Docker may not be running.");
}

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    app.UseHsts();
}

app.UseHttpsRedirection();

// Serve audio files from the data directories
var dataRoot = app.Configuration["FishOrchestrator:DataRoot"] ?? @"D:\DockerData\FishAudio";

var outputDir = Path.Combine(dataRoot, "Output");
if (Directory.Exists(outputDir))
{
    app.UseStaticFiles(new StaticFileOptions
    {
        FileProvider = new PhysicalFileProvider(outputDir),
        RequestPath = "/audio/output"
    });
}

var referencesDir = Path.Combine(dataRoot, "References");
if (Directory.Exists(referencesDir))
{
    app.UseStaticFiles(new StaticFileOptions
    {
        FileProvider = new PhysicalFileProvider(referencesDir),
        RequestPath = "/audio/references"
    });
}

app.UseAntiforgery();

// YARP reverse proxy for TTS API
app.MapReverseProxy();

app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();
