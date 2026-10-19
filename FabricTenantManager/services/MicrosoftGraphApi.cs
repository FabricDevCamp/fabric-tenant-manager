using FabricTenantManager.Models;
using FabricTenantManager.Services;
using Microsoft.Fabric.Api;
using Microsoft.Graph;
using Microsoft.Graph.Models;

namespace FabricTenantManager.Services {

  public class MicrosoftGraphApi {

    private EntraIdTokenManager tokenManager { get; }
    private SignalRLogger appLogger;
    private GraphServiceClient graphClient;
    private string tenantId;
    private string managedIdentityPrincipalId;

    public MicrosoftGraphApi(IConfiguration Configuration,
                    EntraIdTokenManager TokenManager,
                    SignalRLogger AppLogger) {
      tenantId = Configuration["AzureAd:TenantId"];
      managedIdentityPrincipalId = Configuration["ManagedIdentity:PrincipalId"];
      tokenManager = TokenManager;
      appLogger = AppLogger;
      graphClient = tokenManager.GetGraphServiceClient().Result; // UNDO LOCAL
    }

    public async Task<List<Application>> GetApplications(string filter = "") {

      List<Application> appList = new List<Application>();

      ApplicationCollectionResponse applications =
          await graphClient.Applications.GetAsync(requestConfiguration =>
          {
            requestConfiguration.QueryParameters.Select =
                ["id", "appId", "displayName", "signInAudience"];
          });

      if (applications is not null) {
        PageIterator<Application, ApplicationCollectionResponse> pageIterator =
            PageIterator<Application, ApplicationCollectionResponse>.CreatePageIterator(
                graphClient,
                applications,
                app =>
                {
                  if (app.DisplayName!.StartsWith(filter, StringComparison.OrdinalIgnoreCase)) {
                    appList.Add(app);
                  }
                  return true;
                });
        await pageIterator.IterateAsync();
      }
      return appList;
    }

    public async Task<Application> GetApplication(string applicationId) {
      return await graphClient.Applications[applicationId].GetAsync();
    }

    public async Task DeleteApplication(string applicationId) {
      await graphClient.Applications[applicationId].DeleteAsync();
    }

    public async Task<FabricTenant> CreateEntraIdApp(string TenantName) {
      
      await appLogger.LogStep($"Creating Entra ID application for tenant {TenantName}...");

      string appDisplayName = $"Tenant-App-{TenantName}";

      Application createAppRequest = new Application {
        DisplayName = appDisplayName,
        SignInAudience = "AzureADMyOrg",
      };

      Application newApp = await graphClient.Applications.PostAsync(createAppRequest);

      string appId = newApp.AppId!;
      await appLogger.LogSubstep($"Entra ID application created with AppId of {appId}...");

      await appLogger.LogSubOperationStart("Creating service prinicpal for Entra ID application");

      ServicePrincipal newServicePrincipal = null;
      int maxRetries = 10;
      for (int i = 0; i < maxRetries; i++) {
        try {
          newServicePrincipal = await graphClient.ServicePrincipals.PostAsync(new ServicePrincipal { AppId = appId });
          break; // Exit the loop if successful
        }
        catch {
          await appLogger.LogOperationInProgress();
          if (i == maxRetries - 1) throw; // Rethrow after maxRetries attempts
          Thread.Sleep(2000); // Wait before retrying
        }
      }
      await appLogger.LogOperationComplete();

      string servicePrincipalObjectId = newServicePrincipal.Id;

      await appLogger.LogSubstep($"Service prinicpal created with object id of {servicePrincipalObjectId}");


      await appLogger.LogSubOperationStart($"Adding service prinicpal to Entra group to enable calling Fabric APIs");

      string entraGroupIdForFabricRESTApis = "1f8dcc1d-82c7-4ccd-b731-ddcc3bc4eff3";

      for (int i = 0; i < maxRetries; i++) {
        try {
          await graphClient.Groups[entraGroupIdForFabricRESTApis].Members.Ref.PostAsync(
            new ReferenceCreate
            {
              OdataId = $"https://graph.microsoft.com/v1.0/directoryObjects/{servicePrincipalObjectId}"
            });

          break; // Exit the loop if successful
        }
        catch {
          await appLogger.LogOperationInProgress();
          if (i == maxRetries - 1) throw; // Rethrow after maxRetries attempts
          Thread.Sleep(2000); // Wait before retrying
        }
      }

      await appLogger.LogOperationComplete();

      await appLogger.LogSubOperationStart($"Adding Federated Credentials to Entra Id application...");


      // Add federated identity so the caller managed identity can exchange tokens for this app.
      FederatedIdentityCredential federatedIdentityRequest;

      for (int i = 0; i < maxRetries; i++) {
        try {
          federatedIdentityRequest = new FederatedIdentityCredential {
            Name = $"{TenantName}-Credentials-For-Fabric-Tenant-Manager",
            Description = "Allows configured caller managed identity to request access tokens.",
            Issuer = $"https://login.microsoftonline.com/{tenantId}/v2.0",
            Subject = managedIdentityPrincipalId,
            Audiences = ["api://AzureADTokenExchange"]
          };
          var federatedIdentity = await graphClient
              .Applications[newApp.Id]
              .FederatedIdentityCredentials
              .PostAsync(federatedIdentityRequest)
              ?? throw new InvalidOperationException("Graph returned no federated identity credential.");
          await appLogger.LogOperationComplete();
          await appLogger.LogSubstep($"Federated credential [{federatedIdentity.Name}] successfully created");
          break; // Exit the loop if successful
        }
        catch {
          await appLogger.LogOperationInProgress();
          if (i == maxRetries - 1) throw; // Rethrow after maxRetries attempts
          Thread.Sleep(5000); // Wait before retrying
        }
      }

      return new FabricTenant {
        Name = TenantName,
        ApplicationObjectId = newApp.Id,
        ApplicationDisplayName = appDisplayName,
        ClientId = newApp.AppId,
        ServicePrincipalId = servicePrincipalObjectId,
        Created = DateTime.UtcNow
      };

    }

  }
}
