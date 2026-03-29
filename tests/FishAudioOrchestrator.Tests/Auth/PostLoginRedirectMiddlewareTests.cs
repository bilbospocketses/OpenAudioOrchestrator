using FishAudioOrchestrator.Web.Data;
using FishAudioOrchestrator.Web.Data.Entities;
using FishAudioOrchestrator.Web.Middleware;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using System.Security.Claims;

namespace FishAudioOrchestrator.Tests.Auth;

public class PostLoginRedirectMiddlewareTests
{
    private static (ServiceProvider sp, AppUser user) BuildServicesWithUser(
        bool mustChangePassword = false, bool mustSetupTotp = false)
    {
        var services = new ServiceCollection();
        services.AddDbContext<AppDbContext>(o =>
            o.UseInMemoryDatabase(Guid.NewGuid().ToString()));
        services.AddIdentityCore<AppUser>()
            .AddRoles<IdentityRole>()
            .AddEntityFrameworkStores<AppDbContext>();
        services.AddLogging();
        var sp = services.BuildServiceProvider();
        var db = sp.GetRequiredService<AppDbContext>();
        db.Database.EnsureCreated();
        var userMgr = sp.GetRequiredService<UserManager<AppUser>>();
        var user = new AppUser
        {
            UserName = "testuser",
            DisplayName = "Test",
            MustChangePassword = mustChangePassword,
            MustSetupTotp = mustSetupTotp,
            CreatedAt = DateTimeOffset.UtcNow
        };
        userMgr.CreateAsync(user, "Test123!@").GetAwaiter().GetResult();
        return (sp, user);
    }

    private static HttpContext CreateAuthenticatedContext(ServiceProvider sp, AppUser user, string path)
    {
        var context = new DefaultHttpContext { RequestServices = sp };
        context.Request.Path = path;
        context.User = new ClaimsPrincipal(new ClaimsIdentity(new[]
        {
            new Claim(ClaimTypes.NameIdentifier, user.Id),
            new Claim(ClaimTypes.Name, user.UserName!)
        }, "TestAuth"));
        return context;
    }

    [Fact]
    public async Task RedirectsToChangePassword_WhenMustChangePasswordIsTrue()
    {
        var (sp, user) = BuildServicesWithUser(mustChangePassword: true);
        var middleware = new PostLoginRedirectMiddleware(_ => Task.CompletedTask);
        var context = CreateAuthenticatedContext(sp, user, "/");

        await middleware.InvokeAsync(context);

        Assert.Equal(302, context.Response.StatusCode);
        Assert.Equal("/account/change-password", context.Response.Headers.Location.ToString());
    }

    [Fact]
    public async Task RedirectsToSetupTotp_WhenMustSetupTotpIsTrue()
    {
        var (sp, user) = BuildServicesWithUser(mustSetupTotp: true);
        var middleware = new PostLoginRedirectMiddleware(_ => Task.CompletedTask);
        var context = CreateAuthenticatedContext(sp, user, "/");

        await middleware.InvokeAsync(context);

        Assert.Equal(302, context.Response.StatusCode);
        Assert.Equal("/account/setup-totp", context.Response.Headers.Location.ToString());
    }

    [Fact]
    public async Task ChangePasswordTakesPriority_OverSetupTotp()
    {
        var (sp, user) = BuildServicesWithUser(mustChangePassword: true, mustSetupTotp: true);
        var middleware = new PostLoginRedirectMiddleware(_ => Task.CompletedTask);
        var context = CreateAuthenticatedContext(sp, user, "/");

        await middleware.InvokeAsync(context);

        Assert.Equal(302, context.Response.StatusCode);
        Assert.Equal("/account/change-password", context.Response.Headers.Location.ToString());
    }

    [Fact]
    public async Task AllowsRequest_WhenNoFlagsSet()
    {
        var (sp, user) = BuildServicesWithUser();
        var nextCalled = false;
        var middleware = new PostLoginRedirectMiddleware(_ => { nextCalled = true; return Task.CompletedTask; });
        var context = CreateAuthenticatedContext(sp, user, "/");

        await middleware.InvokeAsync(context);

        Assert.True(nextCalled);
    }

    [Fact]
    public async Task AllowsTargetPath_WhenRedirectFlagSet()
    {
        var (sp, user) = BuildServicesWithUser(mustChangePassword: true);
        var nextCalled = false;
        var middleware = new PostLoginRedirectMiddleware(_ => { nextCalled = true; return Task.CompletedTask; });
        var context = CreateAuthenticatedContext(sp, user, "/account/change-password");

        await middleware.InvokeAsync(context);

        Assert.True(nextCalled);
    }

    [Fact]
    public async Task SkipsUnauthenticatedRequests()
    {
        var (sp, _) = BuildServicesWithUser(mustChangePassword: true);
        var nextCalled = false;
        var middleware = new PostLoginRedirectMiddleware(_ => { nextCalled = true; return Task.CompletedTask; });
        var context = new DefaultHttpContext { RequestServices = sp };
        context.Request.Path = "/";

        await middleware.InvokeAsync(context);

        Assert.True(nextCalled);
    }
}
