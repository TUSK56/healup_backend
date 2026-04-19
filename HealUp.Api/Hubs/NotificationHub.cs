using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace HealUp.Api.Hubs;

[Authorize]
public class NotificationHub : Hub
{
    public override async Task OnConnectedAsync()
    {
        // Clients subscribe to HealUp channels based on claims (role/guard)
        var user = Context.User;
        if (user != null)
        {
            var guard = user.Claims.FirstOrDefault(c => c.Type == "guard")?.Value;
            var id = user.Claims.FirstOrDefault(c => c.Type == "sub" || c.Type.EndsWith("/nameidentifier"))?.Value;

            if (!string.IsNullOrEmpty(guard) && !string.IsNullOrEmpty(id))
            {
                var role = user.Claims.FirstOrDefault(c => c.Type.EndsWith("/role") || c.Type == "role")?.Value;

                if (guard == "user" && string.Equals(role, "admin", StringComparison.OrdinalIgnoreCase))
                {
                    await Groups.AddToGroupAsync(Context.ConnectionId, $"healup.admin.{id}");
                }
                else if (guard == "user")
                {
                    await Groups.AddToGroupAsync(Context.ConnectionId, $"healup.patient.{id}");
                }
                else if (guard == "pharmacy")
                {
                    await Groups.AddToGroupAsync(Context.ConnectionId, $"healup.pharmacy.{id}");
                }
            }
        }

        await base.OnConnectedAsync();
    }
}

