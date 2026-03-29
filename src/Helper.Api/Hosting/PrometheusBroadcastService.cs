using System.Text.Json;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Hosting;
using Helper.Runtime.Core;

namespace Helper.Api.Hosting;

public sealed class PrometheusBroadcastService : BackgroundService
{
    private readonly IMaintenanceService _maintenance;
    private readonly IHubContext<HelperHub> _hubContext;

    public PrometheusBroadcastService(IMaintenanceService maintenance, IHubContext<HelperHub> hubContext)
    {
        _maintenance = maintenance;
        _hubContext = hubContext;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await _maintenance.RunPrometheusLoopAsync(stoppingToken, async thought =>
            {
                var payload = JsonSerializer.Serialize(new { content = thought, type = "prometheus" });
                await _hubContext.Clients.All.SendAsync("ReceiveThought", payload, stoppingToken);
            });
        }
        catch (OperationCanceledException)
        {
            // Expected during shutdown.
        }
    }
}

