using Docker.DotNet;
using FishAudioOrchestrator.Web.Components;
using FishAudioOrchestrator.Web.Data;
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

// Application services
builder.Services.AddScoped<IContainerConfigService, ContainerConfigService>();
builder.Services.AddScoped<IDockerOrchestratorService, DockerOrchestratorService>();
builder.Services.AddScoped<IVoiceLibraryService, VoiceLibraryService>();
builder.Services.AddHttpClient<ITtsClientService, TtsClientService>();

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    await db.Database.MigrateAsync();
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

app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();
