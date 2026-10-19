using FabricTenantManager.Services.AsyncProcessing;
using Microsoft.AspNetCore.SignalR;

namespace FabricTenantManager.Services {

  public class SignalRLogger {

    private IHubContext<ClientNotificaionHub> notificationHub;

    public SignalRLogger(IHubContext<ClientNotificaionHub> NotificationHub) {
      notificationHub = NotificationHub;
    }

    public async Task SendClientRedirect(string RedirectPath) {
      await notificationHub.Clients.All.SendAsync("LogStep", $"Redirecting to {RedirectPath} in 3 seconds...");
      Thread.Sleep(3000); // Wait for 3 seconds before redirecting
      await notificationHub.Clients.All.SendAsync("ClientRedirect", RedirectPath);
    }

    public async Task LogSolution(string Message) {
      await notificationHub.Clients.All.SendAsync("LogSolution", Message);
    }

    public async Task LogStep(string Message) {
      await notificationHub.Clients.All.SendAsync("LogStep", Message);
    }

    public async Task LogSubstep(string Message) {
      await notificationHub.Clients.All.SendAsync("LogSubstep", Message);
    }

    public async Task LogOperationStart(string Message) {
      await notificationHub.Clients.All.SendAsync("LogOperationStart", Message);
    }

    public async Task LogSubOperationStart(string Message) {
      await notificationHub.Clients.All.SendAsync("LogSubOperationStart", Message);
    }

    public async Task LogOperationInProgress() {
      await notificationHub.Clients.All.SendAsync("LogOperationInProgress", "");
    }

    public async Task LogOperationComplete() {
      await notificationHub.Clients.All.SendAsync("LogOperationComplete", "");
    }

    public async Task LogTableHeader(string TableTitle) {
      await notificationHub.Clients.All.SendAsync("LogTableHeader", TableTitle);
    }

    public async Task LogTableRow(string FirstColumnValue, string SecondColumnValue) {
      string row = FirstColumnValue + "," + SecondColumnValue;
      await notificationHub.Clients.All.SendAsync("LogTableRow", row);
    }

    public async Task LogRawValue(string value) {
      await notificationHub.Clients.All.SendAsync("LogRawValue", value);
    }

    public async Task LogException(Exception ex) {
      string message = $"Error: {ex.GetType().Name}-{ex.Message}";
      await notificationHub.Clients.All.SendAsync("LogException", message);
    }

    public async Task LogSolutionComplete(string Message) {
      await notificationHub.Clients.All.SendAsync("LogSolutionComplete", Message);
    }

  }

}
