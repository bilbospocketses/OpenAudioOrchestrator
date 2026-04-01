using FishAudioOrchestrator.Web.Data;
using FishAudioOrchestrator.Web.Data.Entities;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

namespace FishAudioOrchestrator.Web.Middleware;

public class PostLoginRedirectMiddleware
{
    private readonly RequestDelegate _next;

    private static readonly string[] _exemptPrefixes = new[]
    {
        "/account/change-password",
        "/account/setup-totp",
        "/login",
        "/setup",
        "/access-denied",
        "/_framework",
        "/_blazor",
        "/_content",
        "/api/",
        "/hubs/",
        "/css",
        "/audio/"
    };

    public PostLoginRedirectMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var user = context.User;
        if (user.Identity?.IsAuthenticated != true)
        {
            await _next(context);
            return;
        }

        var path = context.Request.Path;

        // Skip static assets and non-page routes to avoid unnecessary DB queries
        if (path.Value?.Contains('.') == true)
        {
            await _next(context);
            return;
        }

        foreach (var prefix in _exemptPrefixes)
        {
            if (path.StartsWithSegments(prefix))
            {
                await _next(context);
                return;
            }
        }

        var userId = user.FindFirstValue(ClaimTypes.NameIdentifier);
        if (userId is null)
        {
            await _next(context);
            return;
        }

        var db = context.RequestServices.GetRequiredService<AppDbContext>();
        var appUser = await db.Users
            .OfType<AppUser>()
            .FirstOrDefaultAsync(u => u.Id == userId);

        if (appUser is null)
        {
            await _next(context);
            return;
        }

        if (appUser.MustChangePassword)
        {
            context.Response.Redirect("/account/change-password");
            return;
        }

        if (appUser.MustSetupTotp)
        {
            context.Response.Redirect("/account/setup-totp");
            return;
        }

        await _next(context);
    }
}
