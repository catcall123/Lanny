using Microsoft.AspNetCore.SignalR;

namespace Lanny.Hubs;

public class DeviceHub : Hub
{
    public override async Task OnConnectedAsync()
    {
        await base.OnConnectedAsync();
    }
}
