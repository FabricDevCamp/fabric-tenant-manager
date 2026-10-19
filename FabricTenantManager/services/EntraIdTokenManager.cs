using Azure.Core;
using Azure.Identity;
using FabricTenantManager.Models;
using FabricTenantManager.Services.AsyncProcessing;
using Microsoft.Graph;
using Microsoft.Identity.Client;
using Microsoft.Identity.Web;
using AppConfig = Microsoft.Identity.Client.AppConfig;
using Microsoft.VisualStudio.Services.Commerce;
using static Microsoft.Azure.Pipelines.WebApi.PipelinesResources;
using static Microsoft.VisualStudio.Services.Graph.GraphResourceIds;

namespace FabricTenantManager.Services {

  public class EntraIdTokenManager {

    const string fabricApiPermissionScopeforSPN = "https://api.fabric.microsoft.com/.default";
    const string microsoftGraphApiPermissionScopeforSPN = "https://graph.microsoft.com/.default";

    private SignalRLogger appLogger;
    private string tenantId;
    private string clientId;
    private AuthenticationResult fabricAccessTokenResult;

    public EntraIdTokenManager(IConfiguration Configuration, SignalRLogger AppLogger) {
      tenantId = Configuration["ManagedIdentity:TenantId"];
      clientId = Configuration["ManagedIdentity:ClientId"];
      appLogger = AppLogger;
    }

    public async Task<AuthenticationResult> GetAccessTokenResult(string Scope) {
      string tenantSpecificAuthority = $"https://login.microsoftonline.com/{tenantId}/v2.0";
      AppConfig.ManagedIdentityId managedIdentityId = 
        AppConfig.ManagedIdentityId.WithUserAssignedClientId(clientId);
      var app = ManagedIdentityApplicationBuilder.Create(managedIdentityId).Build();
      return await app.AcquireTokenForManagedIdentity(Scope).ExecuteAsync();
    }

    public async Task<AuthenticationResult> GetAccessTokenResult(string[] Scopes) {
      return await GetAccessTokenResult(Scopes[0]);
    }

    public async Task<AuthenticationResult> GetFabricAccessTokenResult() {
      await appLogger.LogStep($"Calling GetFabricAccessTokenResult");
      var timeNow = DateTimeOffset.UtcNow;
      if (fabricAccessTokenResult == null ||
          timeNow >= fabricAccessTokenResult.ExpiresOn) {
        string scope = EntraIdTokenManager.fabricApiPermissionScopeforSPN;
        fabricAccessTokenResult = await GetAccessTokenResult(scope);
      }
      return fabricAccessTokenResult;
    }

    public async Task<string> GetFabricAccessToken() {
      return (await GetFabricAccessTokenResult()).AccessToken;
    }

    public async Task<GraphServiceClient> GetGraphServiceClient() {
      var credentialOptions = new DefaultAzureCredentialOptions { ManagedIdentityClientId = clientId };
      var credential = new DefaultAzureCredential(credentialOptions);      
      return new GraphServiceClient(credential, new[] { microsoftGraphApiPermissionScopeforSPN });
    }

    public async Task<string> GetAccessTokenForTenant(string tenantClientId) {
      string tenantSpecificAuthority = $"https://login.microsoftonline.com/{tenantId}/v2.0";
      string tokenExchangeAudience = "api://AzureADTokenExchange/.default";

      var federatedCredential = new ClientAssertionCredential(
        tenantId,
        tenantClientId,
        async (tokenRequestContext) => {
          var tokenResult = await GetAccessTokenResult(new[] { tokenExchangeAudience });
          return tokenResult.AccessToken;
        });

      string[] scopes = { EntraIdTokenManager.fabricApiPermissionScopeforSPN };
      var tokenResult = await federatedCredential.GetTokenAsync(new TokenRequestContext(scopes));

      return tokenResult.Token;
    }

  }
}
