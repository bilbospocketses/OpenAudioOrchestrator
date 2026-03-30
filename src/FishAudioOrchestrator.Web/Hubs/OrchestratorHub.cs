using FishAudioOrchestrator.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace FishAudioOrchestrator.Web.Hubs;

[Authorize]
public class OrchestratorHub : Hub
{
    private readonly IContainerLogService _logService;

    public OrchestratorHub(IContainerLogService logService)
    {
        _logService = logService;
    }

    public async Task SubscribeLogs(string containerId)
    {
        await _logService.SubscribeAsync(containerId, Context.ConnectionId);
    }

    public async Task UnsubscribeLogs(string containerId)
    {
        await _logService.UnsubscribeAsync(containerId, Context.ConnectionId);
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        await _logService.UnsubscribeAllAsync(Context.ConnectionId);
        await base.OnDisconnectedAsync(exception);
    }
}
