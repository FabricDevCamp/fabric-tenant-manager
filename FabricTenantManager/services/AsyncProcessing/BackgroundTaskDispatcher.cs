using FabricTenantManager.Controllers;
using FabricTenantManager.Models;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Identity.Web;

namespace FabricTenantManager.Services.AsyncProcessing {

  public class BackgroundTaskDispatcher {

    //private readonly SignalRLogger appLogger;
    private readonly ILogger<BackgroundTaskDispatcher> logger;
    private readonly IBackgroundTaskQueue taskQueue;

    public BackgroundTaskDispatcher(
            ILogger<BackgroundTaskDispatcher> Logger,
            IBackgroundTaskQueue TaskQueue) { // , SignalRLogger AppLogger) {
      taskQueue = TaskQueue;
      logger = Logger;
      //appLogger = AppLogger;
    }


    public async Task OnboardTenant(string Customer, string SolutionName) {

      taskQueue.EnqueueTask(async (serviceScopeFactory, cancellationToken) => {

        // pause to allow async opertions monitor page to intialize
        Thread.Sleep(3000);

        using (var scope = serviceScopeFactory.CreateScope()) {

          var entraIdTokenManager = scope.ServiceProvider.GetRequiredService<EntraIdTokenManager>();
          var loggerEntraIdTokenManager = scope.ServiceProvider.GetRequiredService<ILogger<EntraIdTokenManager>>();
          var signalRLogger = scope.ServiceProvider.GetRequiredService<SignalRLogger>();
          var notificationHub = scope.ServiceProvider.GetRequiredService<IHubContext<ClientNotificaionHub>>();
          var powerBiRestApi = scope.ServiceProvider.GetRequiredService<PowerBiRestApi>();
          var loggerPowerBiRestApi = scope.ServiceProvider.GetRequiredService<ILogger<PowerBiRestApi>>();
          var fabricRestApi = scope.ServiceProvider.GetRequiredService<FabricRestApi>();
          var loggerFabricRestApi = scope.ServiceProvider.GetRequiredService<ILogger<FabricRestApi>>();
          var itemDefinitionFactory = scope.ServiceProvider.GetRequiredService<ItemDefinitionFactory>();
          var loggerItemDefinitionFactory = scope.ServiceProvider.GetRequiredService<ILogger<ItemDefinitionFactory>>();
          var microsoftGraphApi = scope.ServiceProvider.GetRequiredService<MicrosoftGraphApi>();
          var loggerMicrosoftGraphApi = scope.ServiceProvider.GetRequiredService<ILogger<MicrosoftGraphApi>>();
          var fabricTenantManagerDb = scope.ServiceProvider.GetRequiredService<FabricTenantManagerDb>();
          var fabricTenantManagerDbService = scope.ServiceProvider.GetRequiredService<FabricTenantManagerDbService>();
          var loggerFabricTenantManagerDbService = scope.ServiceProvider.GetRequiredService<ILogger<FabricTenantManagerDbService>>();
          var tenantManager = scope.ServiceProvider.GetRequiredService<TenantManager>();
          var loggerTenantManager = scope.ServiceProvider.GetRequiredService<ILogger<TenantManager>>();

          try {
            await tenantManager.OnboardTenant(Customer, SolutionName);
          }
          catch(Exception ex) {
            logger.LogCritical($"Error on background thread: {ex.GetType().Name} - {ex.Message}");
          }
        }


      });

      await Task.FromResult(0);

    }

  }
}
