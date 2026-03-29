using FishAudioOrchestrator.Web.Data;
using FishAudioOrchestrator.Web.Data.Entities;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace FishAudioOrchestrator.Tests.Auth;

public class UserManagementTests
{
    private static (ServiceProvider sp, UserManager<AppUser> userMgr, RoleManager<IdentityRole> roleMgr) BuildServices()
    {
        var services = new ServiceCollection();
        services.AddDbContext<AppDbContext>(o =>
            o.UseInMemoryDatabase(Guid.NewGuid().ToString()));
        services.AddDataProtection();
        services.AddIdentityCore<AppUser>(opts =>
            {
                opts.Password.RequiredLength = 8;
                opts.Password.RequireUppercase = true;
                opts.Password.RequireLowercase = true;
                opts.Password.RequireDigit = true;
                opts.Password.RequireNonAlphanumeric = true;
            })
            .AddRoles<IdentityRole>()
            .AddEntityFrameworkStores<AppDbContext>()
            .AddDefaultTokenProviders();
        services.AddLogging();
        var sp = services.BuildServiceProvider();
        var db = sp.GetRequiredService<AppDbContext>();
        db.Database.EnsureCreated();

        var roleMgr = sp.GetRequiredService<RoleManager<IdentityRole>>();
        roleMgr.CreateAsync(new IdentityRole("Admin")).GetAwaiter().GetResult();
        roleMgr.CreateAsync(new IdentityRole("User")).GetAwaiter().GetResult();

        return (sp, sp.GetRequiredService<UserManager<AppUser>>(), roleMgr);
    }

    [Fact]
    public async Task AdminCanCreateUserWithTempPassword()
    {
        var (sp, userMgr, _) = BuildServices();

        var user = new AppUser
        {
            UserName = "newuser",
            DisplayName = "New User",
            MustChangePassword = true,
            MustSetupTotp = true,
            CreatedAt = DateTimeOffset.UtcNow
        };
        var result = await userMgr.CreateAsync(user, "TempPass1!");
        await userMgr.AddToRoleAsync(user, "User");

        Assert.True(result.Succeeded);
        Assert.True(user.MustChangePassword);
        Assert.True(user.MustSetupTotp);
        var roles = await userMgr.GetRolesAsync(user);
        Assert.Contains("User", roles);
    }

    [Fact]
    public async Task AdminCanResetUserPassword()
    {
        var (sp, userMgr, _) = BuildServices();

        var user = new AppUser
        {
            UserName = "resetme",
            DisplayName = "Reset",
            CreatedAt = DateTimeOffset.UtcNow
        };
        await userMgr.CreateAsync(user, "OldPass1!");

        var token = await userMgr.GeneratePasswordResetTokenAsync(user);
        var result = await userMgr.ResetPasswordAsync(user, token, "NewPass1!");

        Assert.True(result.Succeeded);
        Assert.True(await userMgr.CheckPasswordAsync(user, "NewPass1!"));
    }

    [Fact]
    public async Task CannotDeleteLastAdmin()
    {
        var (sp, userMgr, _) = BuildServices();

        var admin = new AppUser
        {
            UserName = "admin",
            DisplayName = "Admin",
            CreatedAt = DateTimeOffset.UtcNow
        };
        await userMgr.CreateAsync(admin, "Admin123!@");
        await userMgr.AddToRoleAsync(admin, "Admin");

        var admins = await userMgr.GetUsersInRoleAsync("Admin");
        Assert.Single(admins);
    }

    [Fact]
    public async Task CanChangeUserRole()
    {
        var (sp, userMgr, _) = BuildServices();

        var user = new AppUser
        {
            UserName = "roletest",
            DisplayName = "Role Test",
            CreatedAt = DateTimeOffset.UtcNow
        };
        await userMgr.CreateAsync(user, "Test123!@");
        await userMgr.AddToRoleAsync(user, "User");

        await userMgr.RemoveFromRoleAsync(user, "User");
        await userMgr.AddToRoleAsync(user, "Admin");

        var roles = await userMgr.GetRolesAsync(user);
        Assert.Contains("Admin", roles);
        Assert.DoesNotContain("User", roles);
    }
}
