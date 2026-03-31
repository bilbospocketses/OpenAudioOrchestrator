using Docker.DotNet;
using FishAudioOrchestrator.Web.Components;
using FishAudioOrchestrator.Web.Data;
using FishAudioOrchestrator.Web.Data.Entities;
using FishAudioOrchestrator.Web.Hubs;
using FishAudioOrchestrator.Web.Middleware;
using FishAudioOrchestrator.Web.Proxy;
using FishAudioOrchestrator.Web.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using System.Net;
using Microsoft.Extensions.FileProviders;
using Yarp.ReverseProxy.Configuration;

var builder = WebApplication.CreateBuilder(args);

// LettuceEncrypt (only if domain is configured)
var domain = builder.Configuration["FishOrchestrator:Domain"];
if (!string.IsNullOrWhiteSpace(domain))
{
    builder.Services.AddLettuceEncrypt();
    builder.WebHost.UseKestrel(kestrel =>
    {
        kestrel.Listen(IPAddress.Any, 80);
        kestrel.Listen(IPAddress.Any, 443, o => o.UseHttps(h =>
        {
            h.UseLettuceEncrypt(kestrel.ApplicationServices);
        }));
    });
}

// Add services to the container.
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseSqlite(builder.Configuration.GetConnectionString("Default")));

// ASP.NET Identity
builder.Services.AddIdentity<AppUser, IdentityRole>(opts =>
    {
        opts.Password.RequiredLength = 8;
        opts.Password.RequireUppercase = true;
        opts.Password.RequireLowercase = true;
        opts.Password.RequireDigit = true;
        opts.Password.RequireNonAlphanumeric = true;
        opts.Lockout.MaxFailedAccessAttempts = 5;
        opts.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
    })
    .AddEntityFrameworkStores<AppDbContext>()
    .AddDefaultTokenProviders();

builder.Services.ConfigureApplicationCookie(opts =>
{
    opts.Cookie.Name = ".FishOrch.Auth";
    opts.Cookie.HttpOnly = true;
    opts.Cookie.SameSite = SameSiteMode.Strict;
    opts.Cookie.SecurePolicy = CookieSecurePolicy.Always;
    opts.ExpireTimeSpan = TimeSpan.FromHours(24);
    opts.SlidingExpiration = true;
    opts.LoginPath = "/login";
    opts.AccessDeniedPath = "/access-denied";
});

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
builder.Services.AddSingleton<IProxyConfigProvider>(proxyProvider);
builder.Services.AddReverseProxy();

// Application services
builder.Services.AddScoped<IContainerConfigService, ContainerConfigService>();
builder.Services.AddSingleton<IDockerNetworkService, DockerNetworkService>();
builder.Services.AddScoped<IDockerOrchestratorService, DockerOrchestratorService>();
builder.Services.AddScoped<IVoiceLibraryService, VoiceLibraryService>();
builder.Services.AddHttpClient<ITtsClientService, TtsClientService>();
builder.Services.AddScoped<ITotpService, TotpService>();
builder.Services.AddScoped<IAdminSeedService, AdminSeedService>();

// Health monitoring
builder.Services.AddHostedService<HealthMonitorService>();

// SignalR
builder.Services.AddHttpContextAccessor();
builder.Services.AddSignalR();
builder.Services.AddSingleton<IContainerLogService, ContainerLogService>();
builder.Services.AddSingleton<GpuMetricsState>();

var app = builder.Build();

// Run migrations
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    await db.Database.MigrateAsync();
}

// Seed roles
using (var scope = app.Services.CreateScope())
{
    var roleMgr = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();
    foreach (var role in new[] { "Admin", "User" })
    {
        if (!await roleMgr.RoleExistsAsync(role))
            await roleMgr.CreateAsync(new IdentityRole(role));
    }
}

// Seed admin from env vars (if configured and no users exist)
using (var scope = app.Services.CreateScope())
{
    var seeder = scope.ServiceProvider.GetRequiredService<IAdminSeedService>();
    await seeder.SeedIfConfiguredAsync();
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

// Auth middleware
app.UseAuthentication();
app.UseAuthorization();

// Custom middleware
app.UseMiddleware<SetupGuardMiddleware>();
app.UseMiddleware<PostLoginRedirectMiddleware>();

// Serve audio files from the data directories (after auth so only authenticated users can access)
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

app.MapHub<OrchestratorHub>("/hubs/orchestrator");

// Cookie sign-in endpoint (Blazor Server can't set cookies after response starts)
app.MapGet("/api/auth/signin", async (string userId, string? returnUrl, SignInManager<AppUser> signInManager, UserManager<AppUser> userManager, HttpContext httpContext) =>
{
    var user = await userManager.FindByIdAsync(userId);
    if (user is null)
        return Results.Redirect("/login");

    await signInManager.SignInAsync(user, isPersistent: false);
    return Results.Redirect(returnUrl ?? "/");
});

app.MapGet("/api/auth/signout", async (SignInManager<AppUser> signInManager) =>
{
    await signInManager.SignOutAsync();
    return Results.Redirect("/login");
});

app.Run();
