using FishAudioOrchestrator.Web.Data;
using Microsoft.EntityFrameworkCore;

namespace FishAudioOrchestrator.Web.Middleware;

public class SetupGuardMiddleware
{
    private readonly RequestDelegate _next;
    private volatile bool _setupComplete;

    public SetupGuardMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var isSetupPath = context.Request.Path.StartsWithSegments("/setup");
        var isStaticResource = context.Request.Path.StartsWithSegments("/_framework")
            || context.Request.Path.StartsWithSegments("/_blazor")
            || context.Request.Path.StartsWithSegments("/_content")
            || context.Request.Path.StartsWithSegments("/css")
            || context.Request.Path.Value?.Contains('.') == true;

        if (isStaticResource)
        {
            await _next(context);
            return;
        }

        // Once users exist, cache the result — users don't un-exist
        if (!_setupComplete)
        {
            var db = context.RequestServices.GetRequiredService<AppDbContext>();
            if (await db.Users.AnyAsync())
                _setupComplete = true;
        }

        if (isSetupPath)
        {
            // Block setup page once users exist — prevents creating rogue admins
            if (_setupComplete)
            {
                context.Response.Redirect("/");
                return;
            }
            await _next(context);
            return;
        }

        if (!_setupComplete)
        {
            context.Response.Redirect("/setup");
            return;
        }

        await _next(context);
    }
}
