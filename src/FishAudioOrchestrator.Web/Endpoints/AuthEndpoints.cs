using FishAudioOrchestrator.Web.Data.Entities;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Caching.Memory;
using System.Security.Cryptography;

namespace FishAudioOrchestrator.Web.Endpoints;

public static class AuthEndpoints
{
    public static void MapAuthEndpoints(this WebApplication app)
    {
        app.MapPost("/api/auth/login", async (
            HttpContext httpContext,
            SignInManager<AppUser> signInManager,
            UserManager<AppUser> userManager) =>
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
                cache.Set($"totp-pending:{totpToken}", user.Id, TimeSpan.FromMinutes(2));
                return Results.Redirect($"/login/totp?tid={Uri.EscapeDataString(totpToken)}");
            }

            // Regenerate security stamp to invalidate any prior sessions (session fixation defense)
            await userManager.UpdateSecurityStampAsync(user);
            await signInManager.SignInAsync(user, isPersistent: false);
            return Results.Redirect("/");
        }).RequireRateLimiting("auth");

        app.MapPost("/api/auth/signin", async (
            HttpContext httpContext,
            SignInManager<AppUser> signInManager,
            UserManager<AppUser> userManager,
            IMemoryCache cache) =>
        {
            var form = await httpContext.Request.ReadFormAsync();
            var token = form["token"].ToString();
            var returnUrl = form["returnUrl"].ToString();

            if (string.IsNullOrEmpty(token))
                return Results.Redirect("/login?error=invalid");

            // Validate the one-time TOTP completion token
            var cacheKey = $"totp-verified:{token}";
            if (!cache.TryGetValue(cacheKey, out string? userId) || userId is null)
                return Results.Redirect("/login?error=invalid");

            // Remove token so it can only be used once
            cache.Remove(cacheKey);

            var user = await userManager.FindByIdAsync(userId);
            if (user is null)
                return Results.Redirect("/login");

            // Regenerate security stamp to invalidate any prior sessions (session fixation defense)
            await userManager.UpdateSecurityStampAsync(user);
            await signInManager.SignInAsync(user, isPersistent: false);

            // Prevent open redirect — only allow local paths
            if (!string.IsNullOrEmpty(returnUrl) && returnUrl.StartsWith('/') && !returnUrl.StartsWith("//"))
                return Results.Redirect(returnUrl);

            return Results.Redirect("/");
        }).RequireRateLimiting("auth");

        app.MapPost("/api/auth/signout", async (SignInManager<AppUser> signInManager) =>
        {
            await signInManager.SignOutAsync();
            return Results.Redirect("/login");
        });
    }
}
