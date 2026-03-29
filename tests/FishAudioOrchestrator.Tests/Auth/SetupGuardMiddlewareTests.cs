using FishAudioOrchestrator.Web.Data;
using FishAudioOrchestrator.Web.Data.Entities;
using FishAudioOrchestrator.Web.Middleware;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace FishAudioOrchestrator.Tests.Auth;

public class SetupGuardMiddlewareTests
{
    private static ServiceProvider BuildServices()
    {
        var services = new ServiceCollection();
        services.AddDbContext<AppDbContext>(o =>
            o.UseInMemoryDatabase(Guid.NewGuid().ToString()));
        services.AddIdentityCore<AppUser>()
            .AddRoles<IdentityRole>()
            .AddEntityFrameworkStores<AppDbContext>();
        services.AddLogging();
        return services.BuildServiceProvider();
    }

    [Fact]
    public async Task RedirectsToSetup_WhenNoUsersExist()
    {
        var sp = BuildServices();
        var db = sp.GetRequiredService<AppDbContext>();
        db.Database.EnsureCreated();

        var middleware = new SetupGuardMiddleware(_ => Task.CompletedTask);
        var context = new DefaultHttpContext { RequestServices = sp };
        context.Request.Path = "/";

        await middleware.InvokeAsync(context);

        Assert.Equal(302, context.Response.StatusCode);
        Assert.Equal("/setup", context.Response.Headers.Location.ToString());
    }

    [Fact]
    public async Task AllowsSetupPath_WhenNoUsersExist()
    {
        var sp = BuildServices();
        var db = sp.GetRequiredService<AppDbContext>();
        db.Database.EnsureCreated();

        var nextCalled = false;
        var middleware = new SetupGuardMiddleware(_ => { nextCalled = true; return Task.CompletedTask; });
        var context = new DefaultHttpContext { RequestServices = sp };
        context.Request.Path = "/setup";

        await middleware.InvokeAsync(context);

        Assert.True(nextCalled);
    }

    [Fact]
    public async Task AllowsAnyPath_WhenUsersExist()
    {
        var sp = BuildServices();
        var db = sp.GetRequiredService<AppDbContext>();
        db.Database.EnsureCreated();
        var userMgr = sp.GetRequiredService<UserManager<AppUser>>();
        await userMgr.CreateAsync(new AppUser
        {
            UserName = "admin",
            DisplayName = "Admin",
            CreatedAt = DateTimeOffset.UtcNow
        }, "Test123!@");

        var nextCalled = false;
        var middleware = new SetupGuardMiddleware(_ => { nextCalled = true; return Task.CompletedTask; });
        var context = new DefaultHttpContext { RequestServices = sp };
        context.Request.Path = "/";

        await middleware.InvokeAsync(context);

        Assert.True(nextCalled);
    }
}
