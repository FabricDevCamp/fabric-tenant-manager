using FabricTenantManager.Models;
using FabricTenantManager.Services.AsyncProcessing;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Fabric.Api;
using Microsoft.PowerBI.Api;
using Microsoft.PowerBI.Api.Models;

namespace FabricTenantManager.Services {

  public class PowerBiRestApi {

    private EntraIdTokenManager tokenManager { get; }
    private PowerBIClient pbiClient { get; set; }
    private Guid fabricCapacityId { get; }
    private IHubContext<ClientNotificaionHub> notificationHub;

    public PowerBiRestApi(IConfiguration configuration,
                          EntraIdTokenManager TokenManager,
                          IHubContext<ClientNotificaionHub> NotificationHub) {
      fabricCapacityId = new Guid(configuration["Fabric:FabricCapacityId"]);
      tokenManager = TokenManager;
      notificationHub = NotificationHub;
    }

    public async Task SendClientInProgressPing() {
      await notificationHub.Clients.All.SendAsync("ClientInProgressPing");
    }

    public async Task SetPowerBiClientIdentity(string TenantName) {
      string accessToken = await tokenManager.GetAccessTokenForTenant(TenantName);
      pbiClient = new PowerBIClient(accessToken, new Uri("https://api.powerbi.com/"));
    }

    public async Task<PowerBIClient> GetPowerBiClient() {
      if (pbiClient == null) {
        throw new ApplicationException("Power BI client is not initialized. Call SetPowerBiClient() first.");
      }
      return pbiClient;
    }

    public async Task<Groups> GetPowerBiWorkspaces(PowerBIClient pbiClient) {
      var workspaces = (await pbiClient.Groups.GetGroupsAsync()).Value;
      return workspaces;
    }

    public async Task<Group> CreateWorkspace(string DisplayName) {

      var pbiClient = await GetPowerBiClient();

      // create new app workspace
      GroupCreationRequest request = new GroupCreationRequest(DisplayName);
      Group pbiWorkspace = (await pbiClient.Groups.CreateGroupAsync(request)).Value;

      Guid workspaceId = pbiWorkspace.Id;

      await pbiClient.Groups.AssignToCapacityAsync(workspaceId, new AssignToCapacityRequest(this.fabricCapacityId));

      return pbiWorkspace;
    }

    public async Task BindDatasetToConnection(string WorkspaceId, string DatasetId, string ConnectionId) {

      PowerBIClient pbiClient = await GetPowerBiClient();

      BindToGatewayRequest bindRequest = new BindToGatewayRequest(Guid.Empty);

      bindRequest.DatasourceObjectIds.Add(new Guid(ConnectionId));

      await pbiClient.Datasets.BindToGatewayInGroupAsync(new Guid(WorkspaceId), DatasetId, bindRequest);

    }

    public async Task RefreshDataset(Guid WorkspaceId, string DatasetId) {
      await RefreshDataset(WorkspaceId, new Guid(DatasetId));
    }

    public async Task RefreshDataset(Guid WorkspaceId, Guid DatasetId) {

      PowerBIClient pbiClient = await GetPowerBiClient();

      var refreshRequest = new DatasetRefreshRequest(NotifyOption.NoNotification) {
        Type = DatasetRefreshType.Automatic
      };

      var responseStartFresh = await pbiClient.Datasets.RefreshDatasetInGroupAsync(WorkspaceId, DatasetId.ToString(), refreshRequest);

      if (!responseStartFresh.Headers.TryGetValue("x-ms-request-id", out string refreshId)) {
        throw new ApplicationException("Unable to determine the Power BI refresh request ID.");
      }

      var responseStatusCheck = await pbiClient.Datasets.GetRefreshExecutionDetailsInGroupAsync(WorkspaceId, DatasetId, new Guid(refreshId));

      await SendClientInProgressPing();

      while (responseStatusCheck.Value.Status == "Unknown") {
        await Task.Delay(5000);
        await SendClientInProgressPing();
        responseStatusCheck = await pbiClient.Datasets.GetRefreshExecutionDetailsInGroupAsync(WorkspaceId, DatasetId, new Guid(refreshId));
      }

    }

    public async Task PatchDirectLakeDatasetCredentials(string WorkspaceId, string DatasetId) {
      var pbiClient = await GetPowerBiClient();

      var datasources = (await pbiClient.Datasets.GetDatasourcesInGroupAsync(new Guid(WorkspaceId), DatasetId)).Value;
      var SqlEndpointDatasource = datasources.Value[0];

      while (SqlEndpointDatasource.DatasourceId == null) {
        await Task.Delay(5000);
        datasources = (await pbiClient.Datasets.GetDatasourcesInGroupAsync(new Guid(WorkspaceId), DatasetId)).Value;
        SqlEndpointDatasource = datasources.Value[0];
      }

      Guid? datasourceId = SqlEndpointDatasource.DatasourceId;
      var gatewayId = SqlEndpointDatasource.GatewayId.Value;

      // create credential details
      var CredentialDetails = new CredentialDetails(
        "{}",
        CredentialType.OAuth2,
        EncryptedConnection.Encrypted,
        EncryptionAlgorithm.None,
        PrivacyLevel.Private) {
        UseCallerAADIdentity = true,
        UseEndUserOAuth2Credentials = false
      };

      // create UpdateDatasourceRequest 
      UpdateDatasourceRequest req = new UpdateDatasourceRequest(CredentialDetails);

      // Execute Patch command to update Azure SQL datasource credentials
      await pbiClient.Gateways.UpdateDatasourceAsync((Guid)gatewayId, (Guid)datasourceId, req);
    }

    public async Task<Datasources> GetDatasourcesForSemanticModel(Guid WorkspaceId, Guid DatasetId) {
      var pbiClient = await GetPowerBiClient();
      return await pbiClient.Datasets.GetDatasourcesInGroupAsync(WorkspaceId, DatasetId.ToString());
    }

    public class AzureStorageDatasource {
      public string Server { get; set; }
      public string Path { get; set; }
    }

    public async Task<AzureStorageDatasource> GetAzureStorageDatasource(Guid WorkspaceId, Guid DatasetId) {
      var pbiClient = await GetPowerBiClient();
      var datasource = (await pbiClient.Datasets.GetDatasourcesInGroupAsync(WorkspaceId, DatasetId.ToString())).Value.Value.First();
      var datasourceType = datasource.DatasourceType;
      if (datasource.DatasourceType.Equals("AzureDataLakeStorage")) {
        return new AzureStorageDatasource {
          Server = datasource.ConnectionDetails.Server,
          Path = datasource.ConnectionDetails.Path
        };
      }
      else {
        throw new ApplicationException("Error - expecting AzureDataLakeStorage connection");
      }
    }

    public async Task<Guid> GetDatasetIdForReport(Guid WorkspaceId, Guid ReportId) {
      var pbiClient = await GetPowerBiClient();
      var report = (await pbiClient.Reports.GetReportInGroupAsync(WorkspaceId, ReportId)).Value;
      return new Guid(report.DatasetId);
    }

    public async Task<Dataset> GetDatasetForReport(Guid WorkspaceId, Guid ReportId) {
      var pbiClient = await GetPowerBiClient();
      var report = (await pbiClient.Reports.GetReportInGroupAsync(WorkspaceId, ReportId)).Value;
      return (await pbiClient.Datasets.GetDatasetInGroupAsync(WorkspaceId, report.DatasetId)).Value;
    }

    public async Task<string> GetWebDatasourceUrl(Guid WorkspaceId, Guid DatasetId) {
      var pbiClient = await GetPowerBiClient();
      var datasources = (await pbiClient.Datasets.GetDatasourcesInGroupAsync(WorkspaceId, DatasetId.ToString())).Value;
      var datasource = datasources.Value.First();
      if (datasource.DatasourceType.Equals("Web")) {
        return datasource.ConnectionDetails.Url;
      }
      else {
        throw new ApplicationException("Error - expecting Web connection");
      }
    }

    public async Task BindReportToSemanticModel(Guid WorkspaceId, Guid SemanticModelId, Guid ReportId) {
      var pbiClient = await GetPowerBiClient();
      RebindReportRequest bindRequest = new RebindReportRequest(SemanticModelId.ToString());
      await pbiClient.Reports.RebindReportInGroupAsync(WorkspaceId, ReportId, bindRequest);
    }

    public async Task BindSemanticModelToConnection(Guid WorkspaceId, Guid SemanticModelId, Guid ConnectionId) {
      var pbiClient = await GetPowerBiClient();

      BindToGatewayRequest bindRequest = new BindToGatewayRequest(Guid.Empty);

      bindRequest.DatasourceObjectIds.Add(ConnectionId);

      await pbiClient.Datasets.BindToGatewayInGroupAsync(WorkspaceId, SemanticModelId.ToString(), bindRequest);

    }

    public async Task<EmbeddedViewModel> GetEmbeddedViewModel(Guid WorkspaceId) {

      var pbiClient = await GetPowerBiClient();
      var workspace = (await pbiClient.Groups.GetGroupAsync(WorkspaceId)).Value;
      var datasets = (await pbiClient.Datasets.GetDatasetsInGroupAsync(WorkspaceId)).Value;
      var embeddedDatasets = new List<EmbeddedDataset>();

      foreach (var dataset in datasets.Value) {
        embeddedDatasets.Add(new EmbeddedDataset
        {
          id = dataset.Id,
          name = dataset.Name,
          createReportEmbedURL = dataset.CreateReportEmbedURL
        });
      }

      var reports = (await pbiClient.Reports.GetReportsInGroupAsync(WorkspaceId)).Value;

      var embeddedReports = new List<EmbeddedReport>();
      foreach (var report in reports.Value) {
        embeddedReports.Add(new EmbeddedReport
        {
          id = report.Id.ToString(),
          name = report.Name,
          embedUrl = report.EmbedUrl,
          datasetId = report.DatasetId,
          reportType = report.ReportType?.ToString()
        });
      }

      GenerateTokenRequestV2 tokenRequest = new GenerateTokenRequestV2();

      foreach (var dataset in datasets.Value) {
        tokenRequest.Datasets.Add(new GenerateTokenRequestV2Dataset(dataset.Id) {
          XmlaPermissions = XmlaPermissions.ReadOnly
        });
      }

      foreach (var report in reports.Value) {
        Boolean userCanEdit = report.ReportType == ReportBasePropertiesReportType.PowerBIReport;
        tokenRequest.Reports.Add(new GenerateTokenRequestV2Report(report.Id) {
          AllowEdit = userCanEdit
        });
      }

      tokenRequest.TargetWorkspaces.Add(new GenerateTokenRequestV2TargetWorkspace(WorkspaceId));

      // call to Power BI Service API and pass GenerateTokenRequest object to generate embed token
      var EmbedTokenResult = pbiClient.EmbedToken.GenerateToken(tokenRequest);

      return new EmbeddedViewModel
      {
        tenantName = workspace.Name.Replace("Tenant - ", ""),
        reports = embeddedReports,
        datasets = embeddedDatasets,
        embedToken = EmbedTokenResult.Value.Token,
      };

    }
    
  }
}
