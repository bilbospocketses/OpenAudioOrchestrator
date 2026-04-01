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
using Microsoft.Extensions.Caching.Memory;
using System.Net;
using System.Security.Cryptography;
using System.Threading.RateLimiting;
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

// Rate limiting for auth endpoints
builder.Services.AddRateLimiter(options =>
{
    options.AddPolicy("auth", httpContext =>
        RateLimitPartition.GetFixedWindowLimiter(
            httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 10,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0
            }));
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
});

builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseSqlite(builder.Configuration.GetConnectionString("Default")));
builder.Services.AddDbContextFactory<AppDbContext>(options =>
    options.UseSqlite(builder.Configuration.GetConnectionString("Default")), ServiceLifetime.Scoped);

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
builder.Services.AddHttpClient<ITtsClientService, TtsClientService>(client =>
{
    client.Timeout = Timeout.InfiniteTimeSpan;
});
builder.Services.AddScoped<ITotpService, TotpService>();
builder.Services.AddScoped<IAdminSeedService, AdminSeedService>();

// Health monitoring
builder.Services.AddHostedService<HealthMonitorService>();
builder.Services.AddHostedService<TtsJobProcessor>();

// SignalR
builder.Services.AddSignalR();
builder.Services.AddSingleton<IContainerLogService, ContainerLogService>();
builder.Services.AddSingleton<GpuMetricsState>();
builder.Services.AddSingleton<OrchestratorEventBus>();
builder.Services.AddSingleton<SetupService>();
builder.Services.AddMemoryCache();

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
app.UseRateLimiter();

// Auth middleware
app.UseAuthentication();
app.UseAuthorization();

// Custom middleware
app.UseMiddleware<SetupGuardMiddleware>();
app.UseMiddleware<PostLoginRedirectMiddleware>();

// Serve audio files from the data directories (authenticated endpoints)
var dataRoot = app.Configuration["FishOrchestrator:DataRoot"] ?? @"C:\MyFishAudioProj";

app.MapGet("/audio/output/{fileName}", (string fileName) =>
{
    if (fileName.Contains("..") || fileName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
        return Results.NotFound();

    var filePath = Path.Combine(dataRoot, "Output", fileName);
    if (!File.Exists(filePath))
        return Results.NotFound();

    return Results.File(filePath, "audio/wav", enableRangeProcessing: true);
}).RequireAuthorization();

app.MapGet("/audio/references/{*filePath}", (string filePath) =>
{
    if (filePath.Contains(".."))
        return Results.NotFound();

    var fullPath = Path.Combine(dataRoot, "References", filePath.Replace('/', Path.DirectorySeparatorChar));
    if (!File.Exists(fullPath))
        return Results.NotFound();

    var contentType = Path.GetExtension(fullPath).ToLowerInvariant() switch
    {
        ".wav" => "audio/wav",
        ".mp3" => "audio/mpeg",
        ".flac" => "audio/flac",
        ".ogg" => "audio/ogg",
        _ => "application/octet-stream"
    };
    return Results.File(fullPath, contentType, enableRangeProcessing: true);
}).RequireAuthorization();

app.UseAntiforgery();

// YARP reverse proxy for TTS API
app.MapReverseProxy();

app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.MapHub<OrchestratorHub>("/hubs/orchestrator");

// Auth endpoints (Blazor Server can't set cookies after response starts)
app.MapPost("/api/auth/login", async (HttpContext httpContext, SignInManager<AppUser> signInManager, UserManager<AppUser> userManager) =>
{
    var form = await httpContext.Request.ReadFormAsync();
    var username = form["username"].ToString();
    var password = form["password"].ToString();

    var user = await userManager.FindByNameAsync(username);
    if (user is null)
        return Results.Redirect("/login?error=invalid");

    var result = await signInManager.CheckPasswordSignInAsync(user, password, lockoutOnFailure: true);
    if (!result.Succeeded)
    {
        var errorCode = result.IsLockedOut ? "locked" : "invalid";
        return Results.Redirect($"/login?error={errorCode}");
    }

    var hasTwoFactor = await userManager.GetTwoFactorEnabledAsync(user);
    if (hasTwoFactor)
    {
        var cache = httpContext.RequestServices.GetRequiredService<IMemoryCache>();
        var totpToken = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
        cache.Set($"totp-pending:{totpToken}", user.Id, TimeSpan.FromMinutes(5));
        return Results.Redirect($"/login/totp?tid={Uri.EscapeDataString(totpToken)}");
    }

    await signInManager.SignInAsync(user, isPersistent: false);
    return Results.Redirect("/");
}).RequireRateLimiting("auth");

app.MapGet("/api/auth/signin", async (string token, string? returnUrl, SignInManager<AppUser> signInManager, UserManager<AppUser> userManager, IMemoryCache cache) =>
{
    // Validate the one-time TOTP completion token
    var cacheKey = $"totp-verified:{token}";
    if (!cache.TryGetValue(cacheKey, out string? userId) || userId is null)
        return Results.Redirect("/login?error=invalid");

    // Remove token so it can only be used once
    cache.Remove(cacheKey);

    var user = await userManager.FindByIdAsync(userId);
    if (user is null)
        return Results.Redirect("/login");

    await signInManager.SignInAsync(user, isPersistent: false);

    // Prevent open redirect — only allow local paths
    if (!string.IsNullOrEmpty(returnUrl) && returnUrl.StartsWith('/') && !returnUrl.StartsWith("//"))
        return Results.Redirect(returnUrl);

    return Results.Redirect("/");
});

app.MapGet("/api/auth/signout", async (SignInManager<AppUser> signInManager) =>
{
    await signInManager.SignOutAsync();
    return Results.Redirect("/login");
});

app.Run();
