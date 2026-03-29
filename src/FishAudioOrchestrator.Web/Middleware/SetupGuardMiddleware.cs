using FishAudioOrchestrator.Web.Data;
using Microsoft.EntityFrameworkCore;

namespace FishAudioOrchestrator.Web.Middleware;

public class SetupGuardMiddleware
{
    private readonly RequestDelegate _next;

    public SetupGuardMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        if (context.Request.Path.StartsWithSegments("/setup")
            || context.Request.Path.StartsWithSegments("/_framework")
            || context.Request.Path.StartsWithSegments("/_blazor"))
        {
            await _next(context);
            return;
        }

        var db = context.RequestServices.GetRequiredService<AppDbContext>();
        var hasUsers = await db.Users.AnyAsync();

        if (!hasUsers)
        {
            context.Response.Redirect("/setup");
            return;
        }

        await _next(context);
    }
}
