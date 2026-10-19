namespace FabricTenantManager.Services.AsyncProcessing {

class BackgroundProvisioningService : BackgroundService {

    private readonly IBackgroundTaskQueue taskQueue;
    private readonly IServiceScopeFactory serviceScopeFactory;
    private readonly ILogger<BackgroundProvisioningService> logger;

    public BackgroundProvisioningService(IBackgroundTaskQueue TaskQueue, IServiceScopeFactory ServiceScopeFactory, ILogger<BackgroundProvisioningService> Logger) {
      taskQueue = TaskQueue;
      serviceScopeFactory = ServiceScopeFactory;
      logger = Logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken) {

      // Dequeue and execute tasks until the application is stopped
      while (!stoppingToken.IsCancellationRequested) {
      
        // block until next task becomes available
        var task = await taskQueue.DequeueAsync(stoppingToken);

        try {
          // Run task
          await task(serviceScopeFactory, stoppingToken);
        }
        catch (Exception ex) {
          logger.LogError(ex, "An error occured during execution of a background task");
        }
      }
    }
  }

}
