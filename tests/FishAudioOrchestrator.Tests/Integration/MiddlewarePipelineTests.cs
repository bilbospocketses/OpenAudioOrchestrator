using System.Net;
using FishAudioOrchestrator.Web.Data.Entities;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;

namespace FishAudioOrchestrator.Tests.Integration;

public class MiddlewarePipelineTests : IClassFixture<CustomWebApplicationFactory>, IAsyncLifetime
{
    private readonly CustomWebApplicationFactory _factory;

    public MiddlewarePipelineTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
    }

    public async Task InitializeAsync() => await _factory.SeedTestDataAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task SetupPage_WhenUsersExist_RedirectsToRoot()
    {
        var client = _factory.CreateNonRedirectClient();

        var response = await client.GetAsync("/setup");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/", response.Headers.Location!.OriginalString);
    }

    [Fact]
    public async Task ProtectedPage_Unauthenticated_RedirectsToLogin()
    {
        var client = _factory.CreateNonRedirectClient();

        var response = await client.GetAsync("/");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Contains("/login", response.Headers.Location!.OriginalString);
    }

    [Fact]
    public async Task ProtectedPage_Authenticated_Returns200()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();

        var response = await client.GetAsync("/");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task MustChangePassword_RedirectsToChangePassword()
    {
        // Flag the user as needing password change
        using (var scope = _factory.Services.CreateScope())
        {
            var userMgr = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
            var user = await userMgr.FindByNameAsync(CustomWebApplicationFactory.UserUsername);
            user!.MustChangePassword = true;
            await userMgr.UpdateAsync(user);
        }

        try
        {
            var client = await _factory.CreateAuthenticatedClientAsync(
                CustomWebApplicationFactory.UserUsername, CustomWebApplicationFactory.UserPassword);

            var response = await client.GetAsync("/");

            Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
            Assert.Contains("/account/change-password", response.Headers.Location!.OriginalString);
        }
        finally
        {
            using var scope = _factory.Services.CreateScope();
            var userMgr = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
            var user = await userMgr.FindByNameAsync(CustomWebApplicationFactory.UserUsername);
            user!.MustChangePassword = false;
            await userMgr.UpdateAsync(user);
        }
    }

    [Fact]
    public async Task MustSetupTotp_RedirectsToSetupTotp()
    {
        using (var scope = _factory.Services.CreateScope())
        {
            var userMgr = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
            var user = await userMgr.FindByNameAsync(CustomWebApplicationFactory.UserUsername);
            user!.MustSetupTotp = true;
            await userMgr.UpdateAsync(user);
        }

        try
        {
            var client = await _factory.CreateAuthenticatedClientAsync(
                CustomWebApplicationFactory.UserUsername, CustomWebApplicationFactory.UserPassword);

            var response = await client.GetAsync("/");

            Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
            Assert.Contains("/account/setup-totp", response.Headers.Location!.OriginalString);
        }
        finally
        {
            using var scope = _factory.Services.CreateScope();
            var userMgr = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
            var user = await userMgr.FindByNameAsync(CustomWebApplicationFactory.UserUsername);
            user!.MustSetupTotp = false;
            await userMgr.UpdateAsync(user);
        }
    }

    [Fact]
    public async Task Signout_ThenAccessProtectedPage_RedirectsToLogin()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();

        // Verify authenticated first
        var authed = await client.GetAsync("/");
        Assert.Equal(HttpStatusCode.OK, authed.StatusCode);

        // Sign out
        await client.GetAsync("/api/auth/signout");

        // Try to access protected page
        var response = await client.GetAsync("/");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Contains("/login", response.Headers.Location!.OriginalString);
    }
}
