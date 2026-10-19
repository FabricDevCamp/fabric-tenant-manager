using Microsoft.AspNetCore.SignalR;

namespace FabricTenantManager.Services.AsyncProcessing {

  public class ClientNotificaionHub : Hub {

    public async Task SendClientNotification(string message) {
      await this.Clients.All.SendAsync("ClientNotification", message);
    }

  }
}
