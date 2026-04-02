using System.Runtime.InteropServices;
using System.Security.AccessControl;
using System.Security.Principal;
using FishAudioOrchestrator.Web.Data;
using FishAudioOrchestrator.Web.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace FishAudioOrchestrator.Web;

public static class StartupTasks
{
    public static async Task RunAsync(WebApplication app)
    {
        await RunMigrationsAsync(app);
        await SeedRolesAsync(app);
        await SeedAdminAsync(app);
        await EnsureDockerNetworkAsync(app);
        RestrictDatabaseFilePermissions(app);
    }

    private static async Task RunMigrationsAsync(WebApplication app)
    {
        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await db.Database.MigrateAsync();
    }

    private static async Task SeedRolesAsync(WebApplication app)
    {
        using var scope = app.Services.CreateScope();
        var roleMgr = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();
        foreach (var role in new[] { "Admin", "User" })
        {
            if (!await roleMgr.RoleExistsAsync(role))
                await roleMgr.CreateAsync(new IdentityRole(role));
        }
    }

    private static async Task SeedAdminAsync(WebApplication app)
    {
        using var scope = app.Services.CreateScope();
        var seeder = scope.ServiceProvider.GetRequiredService<IAdminSeedService>();
        await seeder.SeedIfConfiguredAsync();
    }

    private static async Task EnsureDockerNetworkAsync(WebApplication app)
    {
        try
        {
            var networkService = app.Services.GetRequiredService<IDockerNetworkService>();
            await networkService.EnsureNetworkExistsAsync();
        }
        catch (Exception ex)
        {
            app.Logger.LogWarning(ex, "Could not ensure Docker bridge network exists. Docker may not be running.");
        }
    }

    private static void RestrictDatabaseFilePermissions(WebApplication app)
    {
        var connectionString = app.Configuration.GetConnectionString("Default");
        if (connectionString is null) return;

        // Extract the file path from "Data Source=..."
        var dataSourcePrefix = "Data Source=";
        var idx = connectionString.IndexOf(dataSourcePrefix, StringComparison.OrdinalIgnoreCase);
        if (idx < 0) return;

        var pathStart = idx + dataSourcePrefix.Length;
        var semicolonIdx = connectionString.IndexOf(';', pathStart);
        var dbPath = semicolonIdx >= 0
            ? connectionString[pathStart..semicolonIdx]
            : connectionString[pathStart..];

        if (!File.Exists(dbPath)) return;

        try
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                // Windows: restrict to current user only
                var fileInfo = new FileInfo(dbPath);
                var security = fileInfo.GetAccessControl();
                security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);

                var currentUser = WindowsIdentity.GetCurrent().Name;
                security.AddAccessRule(new FileSystemAccessRule(
                    currentUser,
                    FileSystemRights.FullControl,
                    AccessControlType.Allow));

                fileInfo.SetAccessControl(security);
            }
            else
            {
                // Linux/macOS: chmod 600
                File.SetUnixFileMode(dbPath,
                    UnixFileMode.UserRead | UnixFileMode.UserWrite);
            }

            app.Logger.LogInformation("Database file permissions restricted: {Path}", dbPath);
        }
        catch (Exception ex)
        {
            app.Logger.LogWarning(ex, "Could not restrict database file permissions for {Path}", dbPath);
        }
    }
}
