using FabricTenantManager.Models;
using Microsoft.Fabric.Api;
using Microsoft.Fabric.Api.Core.Models;
using Microsoft.Fabric.Api.Utils;
using PowerBiModels = Microsoft.PowerBI.Api.Models;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using AdminModels = Microsoft.Fabric.Api.Admin.Models;
using LakehouseModels = Microsoft.Fabric.Api.Lakehouse.Models;
using NotebookModels = Microsoft.Fabric.Api.Notebook.Models;
using ReportModels = Microsoft.Fabric.Api.Report.Models;
using SemanticModelModels = Microsoft.Fabric.Api.SemanticModel.Models;
using WarehouseModels = Microsoft.Fabric.Api.Warehouse.Models;

namespace FabricTenantManager.Services {

  public class FabricRestApi {

    private EntraIdTokenManager tokenManager { get; }
    private SignalRLogger appLogger;
    private FabricClient fabricClient;
    private string fabricRestApiBaseUri;
    private string fabricCapacityId { get; }
    private string adminUserId { get; }
    private string azureStorageSasToken { get; }

    public FabricRestApi(IConfiguration Configuration,
                       EntraIdTokenManager TokenManager,
                       SignalRLogger AppLogger) {
      tokenManager = TokenManager;
      appLogger = AppLogger;
      fabricRestApiBaseUri = Configuration["Fabric:FabricRestApiBaseUrl"];
      fabricCapacityId = Configuration["Fabric:FabricCapacityId"];
      adminUserId = Configuration["Fabric:AdminUserId"];
      azureStorageSasToken = Configuration["AzureStorage:SasToken"];
    }

    public async Task SetFabricClientIdentity(string tenantClientId) {
      await appLogger.LogSubstep("Getting access token using federated token exchange");
      await appLogger.LogSubstep($"Tenant application has client id [{tenantClientId}] ");
      string accessToken = await tokenManager.GetAccessTokenForTenant(tenantClientId);
      await appLogger.LogSubstep($"Access token for tenant application acquired using token exchange");
      fabricClient = new FabricClient(accessToken, new Uri(fabricRestApiBaseUri));
    }

    async Task<FabricClient> GetFabricClient() {
      if (fabricClient == null) {
        throw new ApplicationException("Fabric client is not initialized. Call SetFabricClient first.");
      }
      return fabricClient;
    }

    public async Task EnsureAccessToFabricApis() {
      bool canAccessApis = false;
      while (!canAccessApis) {
        try {
          var workspaces = await GetWorkspaces();
          await appLogger.LogSubstep("Test call to Fabric APIs completed successfully");
          canAccessApis = true;
        }
        catch {
          await appLogger.LogSubstep("Tenant application failed calling Fabric APIs - waiting 10 seconds");
          await Task.Delay(10000);
        }
      }
    }

    public async Task<List<Workspace>> GetWorkspaces() {

      var fabricClient = await GetFabricClient();

      // get all workspaces (this includes My Workspapce)
      var allWorkspaces = fabricClient.Core.Workspaces.ListWorkspacesAsync().ToBlockingEnumerable().ToList();

      // filter out My Workspace
      return allWorkspaces.Where(workspace => workspace.Type == WorkspaceType.Workspace).ToList();
    }

    public async Task<List<WorkspaceRow>> GetWorkspaceRows() {

      var fabricClient = await GetFabricClient();

      var capacities = (await GetCapacities()).ToDictionary(capacity => capacity.Id, capacity => capacity);

      // get all workspaces
      var workspaces = fabricClient.Core.Workspaces.ListWorkspacesAsync().ToBlockingEnumerable().ToList();

      // order by workspace name
      workspaces = workspaces.OrderBy(workspace => workspace.DisplayName).ToList();

      var workpaceRows = new List<WorkspaceRow>();
      foreach (var workspace in workspaces) {
        workpaceRows.Add(new WorkspaceRow {
          Workspace = workspace,
          Capacity = workspace.CapacityId.HasValue ? capacities[workspace.CapacityId.Value] : null,
        });
      }
      return workpaceRows;
    }

    public async Task<Capacity> GetCapacity(Guid CapacityId) {
      var capacities = await GetCapacities();
      foreach (var capacity in capacities) {
        if (capacity.Id == CapacityId) {
          return capacity;
        }
      }
      throw new ApplicationException("Could not find capcity");
    }

    public async Task<List<Capacity>> GetCapacities() {
      var fabricClient = await GetFabricClient();
      var capacities = fabricClient.Core.Capacities.ListCapacitiesAsync();
      return capacities.ToBlockingEnumerable().ToList();
    }

    public async Task<Workspace> GetWorkspace(Guid WorkspaceId) {
      var fabricClient = await GetFabricClient();
      return await fabricClient.Core.Workspaces.GetWorkspaceAsync(WorkspaceId);
    }

    public async Task<Workspace> GetWorkspaceByName(string WorkspaceName) {
      var fabricClient = await GetFabricClient();
      var workspaces = fabricClient.Core.Workspaces.ListWorkspaces().ToList();

      foreach (var workspace in workspaces) {
        if (workspace.DisplayName.Equals(WorkspaceName)) {
          return workspace;
        }
      }

      return null;
    }

    public async Task<WorkspaceInfo> GetWorkspaceInfo(Guid WorkspaceId) {
      var fabricClient = await GetFabricClient();
      return fabricClient.Core.Workspaces.GetWorkspace(WorkspaceId);
    }

    public async Task<WorkspaceDetails> GetWorkspaceDetails(string WorkspaceId) {

      var fabricClient = await GetFabricClient();

      Guid workspaceId = new Guid(WorkspaceId);
      var workspace = await GetWorkspaceInfo(workspaceId);
      var capacity = await GetCapacity(workspace.CapacityId.Value);

      var workspaceItems = await GetWorkspaceItems(workspaceId);

      var sequencedWorkspaceItems = new List<Item>();

      var lakehouses = workspaceItems.Where(item => item.Type == ItemType.Lakehouse);
      var lakehouseNames = lakehouses.Select(item => item.DisplayName);

      sequencedWorkspaceItems.AddRange(lakehouses);
      sequencedWorkspaceItems.AddRange(workspaceItems.Where(item => item.Type == ItemType.Notebook));
      sequencedWorkspaceItems.AddRange(workspaceItems.Where(item => item.Type == ItemType.DataPipeline));

      // add semantic models that are not lakehouse default semantic models
      var semanticModels = workspaceItems.Where(item => item.Type == ItemType.SemanticModel);
      foreach (var model in semanticModels) {
        if (!lakehouseNames.Contains(model.DisplayName)) {
          sequencedWorkspaceItems.Add(model);
        }
      }

      sequencedWorkspaceItems.AddRange(workspaceItems.Where(item => item.Type == ItemType.Report));

      var gitConnection = fabricClient.Core.Git.GetConnection(workspaceId).Value;
      string gitProviderType = "";
      string gitConnectionId = "";

      if (gitConnection.GitConnectionState != "NotConnected") {
        // determine the git provider type from the connection details type
        gitProviderType = gitConnection.GitProviderDetails.GetType().Name.Replace("Details", "");
        var gitCredentialResponse = (await fabricClient.Core.Git.GetMyGitCredentialsAsync(workspaceId)).Value;
        if (gitCredentialResponse.GetType().Name == "ConfiguredConnectionGitCredentialsResponse") {
          var gitConfiguredConnectionResponse = (ConfiguredConnectionGitCredentialsResponse)gitCredentialResponse;
          gitConnectionId = gitConfiguredConnectionResponse.ConnectionId.ToString();
        }


      }

      return new WorkspaceDetails {
        DisplayName = workspace.DisplayName,
        WorkspaceId = workspace.Id.ToString(),
        WorkpaceItems = sequencedWorkspaceItems,
        Capacity = capacity,
        WorkpaceConnetions = new List<Connection>(),
        WorkspaceMembers = await GetWorkspaceRoleAssignments(workspaceId),
        WorkspaceUrl = $"https://app.powerbi.com/groups/{WorkspaceId}",
        Description = workspace.Description
      };

    }

    public async Task<Workspace> CreateWorkspace(string WorkspaceName, string Description = "Workspace created by Fabric Tenant Manager") {
      await appLogger.LogStep($"Creating workspace [{WorkspaceName}]...");
      var fabricClient = await GetFabricClient();

      // delete workspace with same name if it exists
      var workspace = await GetWorkspaceByName(WorkspaceName);
      if (workspace != null) {
        await appLogger.LogSubstep($"Deleting existing workspace with matching display name");
        await DeleteWorkspace(workspace.Id);
        workspace = null;
        await appLogger.LogSubstep($"Calling CreateWorkspace API...");
      }

      var createRequest = new CreateWorkspaceRequest(WorkspaceName);
      createRequest.CapacityId = new Guid(fabricCapacityId);
      createRequest.Description = Description;

      workspace = fabricClient.Core.Workspaces.CreateWorkspace(createRequest);

      await appLogger.LogSubstep($"Workspace created with id [{workspace.Id}]");

      await appLogger.LogSubstep($"Adding admin user into workspace role of Admin");
      Guid AdminUserId = new Guid(adminUserId);
      await AddUserAsWorkspaceMember(workspace.Id, AdminUserId, WorkspaceRole.Admin);

      return workspace;
    }

    public async Task<Workspace> UpdateWorkspace(Guid WorkspaceId, string WorkspaceName, string Description = null) {

      var fabricClient = await GetFabricClient();

      var updateRequest = new UpdateWorkspaceRequest {
        DisplayName = WorkspaceName,
        Description = Description
      };

      return fabricClient.Core.Workspaces.UpdateWorkspace(WorkspaceId, updateRequest).Value;
    }

    public async Task<Workspace> UpdateWorkspaceDescription(string TargetWorkspace, string Description) {
      var workspace = await GetWorkspaceByName(TargetWorkspace);
      return await UpdateWorkspaceDescription(workspace.Id, Description);
    }

    public async Task<Workspace> UpdateWorkspaceDescription(Guid WorkspaceId, string Description) {

      var fabricClient = await GetFabricClient();

      var updateRequest = new UpdateWorkspaceRequest {
        Description = Description
      };

      return (await fabricClient.Core.Workspaces.UpdateWorkspaceAsync(WorkspaceId, updateRequest)).Value;
    }

    public async Task DeleteWorkspace(Guid WorkspaceId) {

      var fabricClient = await GetFabricClient();
      var workspace = await GetWorkspaceInfo(WorkspaceId);

      if (workspace.WorkspaceIdentity?.ServicePrincipalId != null) {
        await DeprovisionWorkspaceIdentity(WorkspaceId);
      }

      fabricClient.Core.Workspaces.DeleteWorkspace(WorkspaceId);
    }

    public async Task DeleteWorkspaceByName(string WorkspaceName) {
      var workspace = await GetWorkspaceByName(WorkspaceName);
      await DeleteWorkspace(workspace.Id);
    }

    public async Task DeleteAllTenantResources() {

      // delete all workspaces
      var workspaces = await GetWorkspaces();
      foreach (var workspace in workspaces) {
        await DeleteWorkspace(workspace.Id);
      }

      // delete all connections
      var connections = await GetConnections();
      foreach (var connection in connections) {
        await DeleteConnection(connection.Id);
      }
    }

    public async Task AssignWorkspaceToCapacity(Guid WorkspaceId, Guid CapacityId) {

      var fabricClient = await GetFabricClient();

      var assignRequest = new AssignWorkspaceToCapacityRequest(CapacityId);

      fabricClient.Core.Workspaces.AssignToCapacity(WorkspaceId, assignRequest);

    }

    public async Task ProvisionWorkspaceIdentity(Guid WorkspaceId) {
      await appLogger.LogStep($"Provisioning workspace identity...");
      var fabricClient = await GetFabricClient();
      var workspaceIdentity = (await fabricClient.Core.Workspaces.ProvisionIdentityAsync(WorkspaceId)).Value;
      await appLogger.LogSubstep($"Workspace identity provisioning complete");
      await appLogger.LogSubstep($"Workspace identity ApplicationId: [{workspaceIdentity.ApplicationId.ToString()}]");
      await appLogger.LogSubstep($"Workspace identity ServicePrincipalId: [{workspaceIdentity.ServicePrincipalId.ToString()}]");
      await appLogger.LogSubstep("Adding workspace identity into workspace role of contributor");
      await this.AddServicePrincipalAsWorkspaceMember(WorkspaceId, workspaceIdentity.ServicePrincipalId, WorkspaceRole.Contributor);
    }

    public async Task DeprovisionWorkspaceIdentity(Guid WorkspaceId) {
      await appLogger.LogStep($"Deprovisioning workspace identity...");
      var fabricClient = await GetFabricClient();
      await fabricClient.Core.Workspaces.DeprovisionIdentityAsync(WorkspaceId);
      await appLogger.LogSubstep($"Workspace identity deprovisioning complete");
    }

    public async Task AddUserAsWorkspaceMember(Guid WorkspaceId, Guid UserId, WorkspaceRole RoleAssignment) {
      var fabricClient = await GetFabricClient();
      var user = new UserPrincipal(UserId);
      var roleAssignment = new AddWorkspaceRoleAssignmentRequest(user, RoleAssignment);
      fabricClient.Core.Workspaces.AddWorkspaceRoleAssignment(WorkspaceId, roleAssignment);
    }

    public async Task AddGroupAsWorkspaceMember(Guid WorkspaceId, Guid GroupId, WorkspaceRole RoleAssignment) {
      var fabricClient = await GetFabricClient();
      var group = new GroupPrincipal(GroupId);
      var roleAssignment = new AddWorkspaceRoleAssignmentRequest(group, RoleAssignment);
      fabricClient.Core.Workspaces.AddWorkspaceRoleAssignment(WorkspaceId, roleAssignment);
    }

    public async Task AddServicePrincipalAsWorkspaceMember(Guid WorkspaceId, Guid ServicePrincipalObjectId, WorkspaceRole RoleAssignment) {
      var fabricClient = await GetFabricClient();
      var user = new Microsoft.Fabric.Api.Core.Models.ServicePrincipal(ServicePrincipalObjectId);
      var roleAssignment = new AddWorkspaceRoleAssignmentRequest(user, RoleAssignment);
      fabricClient.Core.Workspaces.AddWorkspaceRoleAssignment(WorkspaceId, roleAssignment);
    }

    public async Task ViewWorkspaceRoleAssignments(Guid WorkspaceId) {

      var fabricClient = await GetFabricClient();

      var roleAssignments = fabricClient.Core.Workspaces.ListWorkspaceRoleAssignments(WorkspaceId);

      await appLogger.LogStep("Viewing workspace role assignments");
      foreach (var roleAssignment in roleAssignments) {
        await appLogger.LogSubstep($"{roleAssignment.Principal.DisplayName} ({roleAssignment.Principal.GetType().Name}) added in role of {roleAssignment.Role}");
      }

    }

    public async Task<IList<WorkspaceRoleAssignment>> GetWorkspaceRoleAssignments(Guid WorkspaceId) {
      var fabricClient = await GetFabricClient();
      return fabricClient.Core.Workspaces.ListWorkspaceRoleAssignmentsAsync(WorkspaceId).ToBlockingEnumerable().ToList();
    }

    public async Task DeleteWorkspaceRoleAssignments(Guid WorkspaceId, Guid RoleAssignmentId) {
      var fabricClient = await GetFabricClient();
      fabricClient.Core.Workspaces.DeleteWorkspaceRoleAssignment(WorkspaceId, RoleAssignmentId);
    }

    public async Task<List<Connection>> GetConnections() {
      var fabricClient = await GetFabricClient();
      return fabricClient.Core.Connections.ListConnections().ToList();
    }

    public async Task<List<ItemConnection>> GetWorkspaceConnections(Guid WorkspaceId) {
      var fabricClient = await GetFabricClient();

      var workspaceConnections = new List<ItemConnection>();
      var connectionIds = new List<string>();

      var items = await GetWorkspaceItems(WorkspaceId);

      foreach (var item in items) {
        try {
          var itemType = item.Type;
          var itemConnections = fabricClient.Core.Items.ListItemConnections(WorkspaceId, item.Id.Value).ToList();
          foreach (var connection in itemConnections) {
            if (!connectionIds.Contains(connection.Id.ToString())) {
              workspaceConnections.Add(connection);
              connectionIds.Add(connection.Id.ToString());
            }
          }
        }
        catch {

        }
      }

      //var gitConnection = fabricClient.Core.Git.GetConnection(WorkspaceId).Value;
      //if (gitConnection.GitConnectionState != "NotConnected") {
      //  var gitCredentialResponse = (await fabricClient.Core.Git.GetMyGitCredentialsAsync(WorkspaceId)).Value;
      //  if (gitCredentialResponse.GetType().Name == "ConfiguredConnectionGitCredentialsResponse") {
      //    var gitConfiguredConnectionResponse = (ConfiguredConnectionGitCredentialsResponse)gitCredentialResponse;
      //    var gitConnectionId = gitConfiguredConnectionResponse.ConnectionId;
      //    if (!connectionIds.Contains(gitConnectionId.ToString())) {
      //      connectionIds.Add(gitConnectionId.ToString());
      //    }
      //  }
      //}

      return workspaceConnections;
    }

    public async Task<Connection> GetConnection(Guid ConnectionId) {
      var fabricClient = await GetFabricClient();
      return fabricClient.Core.Connections.GetConnection(ConnectionId);
    }

    public async Task DeleteConnection(Guid ConnectionId) {
      var fabricClient = await GetFabricClient();
      fabricClient.Core.Connections.DeleteConnection(ConnectionId);
    }

    public async Task DeleteConnectionIfItExists(string ConnectionName) {

      var connections = await GetConnections();

      foreach (var connection in connections) {
        if (connection.DisplayName == ConnectionName) {
          await DeleteConnection(connection.Id);
        }
      }

    }

    public async Task<Connection> GetConnectionByName(string ConnectionName) {

      var connections = await GetConnections();

      foreach (var connection in connections) {
        if (connection.DisplayName == ConnectionName) {
          return connection;
        }
      }

      return null;

    }

    public async Task<Connection> CreateConnection(CreateConnectionRequest CreateConnectionRequest) {

      await appLogger.LogStep($"Creating [{CreateConnectionRequest.ConnectionDetails.Type}] connection");
      await appLogger.LogSubstep($"Display name: {CreateConnectionRequest.DisplayName}");

      var fabricClient = await GetFabricClient();

      var existingConnection = await GetConnectionByName(CreateConnectionRequest.DisplayName);
      if (existingConnection != null) {
        return existingConnection;
      }
      else {

        if (CreateConnectionRequest.PrivacyLevel == null) {
          CreateConnectionRequest.PrivacyLevel = PrivacyLevel.Organizational;
        }

        var connection = fabricClient.Core.Connections.CreateConnection(CreateConnectionRequest).Value;

        await appLogger.LogSubstep($"Connection created with id [{connection.Id}]");

        await appLogger.LogSubstep("Adding connection role assignment of [Owner] to admin user");
        Guid AdminUserId = new Guid(adminUserId);
        await AddConnectionRoleAssignmentForUser(connection.Id, AdminUserId, ConnectionRole.Owner);
        return connection;
      }

    }

    public async Task AddConnectionRoleAssignmentForUser(Guid ConnectionId, Guid UserId, ConnectionRole Role) {
      var fabricClient = await GetFabricClient();
      var principal = new UserPrincipal(UserId);
      var request = new AddConnectionRoleAssignmentRequest(principal, Role);
      fabricClient.Core.Connections.AddConnectionRoleAssignment(ConnectionId, request);
    }

    public async Task AddConnectionRoleAssignmentForServicePrincipal(Guid ConnectionId, Guid ServicePrincipalId, ConnectionRole Role) {
      var fabricClient = await GetFabricClient();
      var principal = new Microsoft.Fabric.Api.Core.Models.ServicePrincipal(ServicePrincipalId);
      var request = new AddConnectionRoleAssignmentRequest(principal, Role);
      fabricClient.Core.Connections.AddConnectionRoleAssignment(ConnectionId, request);
    }

    public async Task<Item> CreateItem(Guid WorkspaceId, CreateItemRequest CreateRequest) {
      var fabricClient = await GetFabricClient();
      await appLogger.LogStep($"Creating [{CreateRequest.Type}] with display name [{CreateRequest.DisplayName}]");
      var newItem = fabricClient.Core.Items.CreateItemAsync(WorkspaceId, CreateRequest).Result.Value;
      await appLogger.LogSubstep($"{CreateRequest.Type} created with id [{newItem.Id}]");
      return newItem;
    }

    public async Task<Folder> CreateFolder(Guid WorkspaceId, string DisplayName) {
      var fabricClient = await GetFabricClient();
      await appLogger.LogStep($"Creating folder [{DisplayName}]");
      var createRequest = new CreateFolderRequest(DisplayName);
      var newFolder = (await fabricClient.Core.Folders.CreateFolderAsync(WorkspaceId, createRequest)).Value;
      await appLogger.LogSubstep($"Folder [{DisplayName}] created with id of [{newFolder.Id.Value.ToString()}]");
      return newFolder;
    }

    public async Task<List<Item>> GetItems(Guid WorkspaceId, string ItemType = null) {
      var fabricClient = await GetFabricClient();
      return fabricClient.Core.Items.ListItems(WorkspaceId, ItemType).ToList();
    }

    public async Task DeleteItem(Guid WorkspaceId, Item item) {
      var fabricClient = await GetFabricClient();
      var newItem = fabricClient.Core.Items.DeleteItem(WorkspaceId, item.Id.Value);
    }

    public async Task<Item> UpdateItem(Guid WorkspaceId, Guid ItemId, string ItemName, string Description = null) {

      var fabricClient = await GetFabricClient();

      var updateRequest = new UpdateItemRequest {
        DisplayName = ItemName,
        Description = Description
      };

      var item = fabricClient.Core.Items.UpdateItem(WorkspaceId, ItemId, updateRequest).Value;

      return item;

    }

    public async Task<List<Item>> GetWorkspaceItems(Guid WorkspaceId, string ItemType = null) {
      var fabricClient = await GetFabricClient();
      return fabricClient.Core.Items.ListItems(WorkspaceId, ItemType).ToList();
    }

    public async Task<List<Item>> GetWorkspaceItems(Guid WorkspaceId, ItemType TargetItemType) {
      var fabricClient = await GetFabricClient();
      return fabricClient.Core.Items.ListItems(WorkspaceId, TargetItemType.ToString()).ToList();
    }

    public async Task<ItemDefinition> GetItemDefinition(Guid WorkspaceId, Guid ItemId, string Format = null) {
      var fabricClient = await GetFabricClient();
      var response = fabricClient.Core.Items.GetItemDefinitionAsync(WorkspaceId, ItemId, Format).Result.Value;
      return response.Definition;
    }

    public async Task UpdateItemDefinition(Guid WorkspaceId, Guid ItemId, UpdateItemDefinitionRequest UpdateRequest) {
      var fabricClient = await GetFabricClient();
      fabricClient.Core.Items.UpdateItemDefinition(WorkspaceId, ItemId, UpdateRequest);
    }

    public async Task<SemanticModelModels.SemanticModel> GetSemanticModelByName(Guid WorkspaceId, string Name) {

      var fabricClient = await GetFabricClient();

      var models = fabricClient.SemanticModel.Items.ListSemanticModels(WorkspaceId);
      foreach (var model in models) {
        if (Name == model.DisplayName) {
          return model;
        }
      }
      return null;
    }

    public async Task<ReportModels.Report> GetReportByName(Guid WorkspaceId, string Name) {

      var fabricClient = await GetFabricClient();

      var reports = fabricClient.Report.Items.ListReports(WorkspaceId);
      foreach (var report in reports) {
        if (Name == report.DisplayName) {
          return report;
        }
      }
      return null;
    }

    public async Task<Item> CreateLakehouse(Guid WorkspaceId, string LakehouseName, bool EnableSchemas = false) {

      // Item create request for lakehouse des not include item definition
      var createRequest = new CreateItemRequest(LakehouseName, ItemType.Lakehouse);

      if (EnableSchemas) {
        createRequest.CreationPayload = new List<KeyValuePair<string, object>>() {
       new KeyValuePair<string, object>("enableSchemas", true)
   };
      }

      // create lakehouse
      return await CreateItem(WorkspaceId, createRequest);
    }

    public async Task RefreshLakehouseTableSchema(Guid WorkspaceId, Guid SqlEndpointId) {
      var fabricClient = await GetFabricClient();
      await fabricClient.SQLEndpoint.Items.RefreshSqlEndpointMetadataAsync(WorkspaceId, SqlEndpointId);
    }

    public async Task<Shortcut> CreateLakehouseShortcut(Guid WorkspaceId, Guid LakehouseId, CreateShortcutRequest CreateShortcutRequest) {
      var fabricClient = await GetFabricClient();
      return fabricClient.Core.OneLakeShortcuts.CreateShortcut(WorkspaceId, LakehouseId, CreateShortcutRequest).Value;
    }

    public async Task<List<ShortcutTransformFlagged>> GetLakehouseShortcuts(Guid WorkspaceId, Guid LakehouseId) {
      var fabricClient = await GetFabricClient();
      var response = fabricClient.Core.OneLakeShortcuts.ListShortcuts(WorkspaceId, LakehouseId).ToList();
      return response;
    }

    public async Task<LakehouseModels.Lakehouse> GetLakehouse(Guid WorkspaceId, Guid LakehousId) {
      var fabricClient = await GetFabricClient();
      return fabricClient.Lakehouse.Items.GetLakehouse(WorkspaceId, LakehousId).Value;
    }

    public async Task<LakehouseModels.Lakehouse> GetLakehouseByName(Guid WorkspaceId, string LakehouseName) {

      var fabricClient = await GetFabricClient();

      var lakehouses = fabricClient.Lakehouse.Items.ListLakehouses(WorkspaceId);

      foreach (var lakehouse in lakehouses) {
        if (lakehouse.DisplayName == LakehouseName) {
          return lakehouse;
        }
      }

      return null;
    }

    public async Task<NotebookModels.Notebook> GetNotebookByName(Guid WorkspaceId, string NotebookName) {

      var fabricClient = await GetFabricClient();

      var notebooks = fabricClient.Notebook.Items.ListNotebooks(WorkspaceId);

      foreach (var notebook in notebooks) {
        if (notebook.DisplayName == NotebookName) {
          return notebook;
        }
      }

      return null;
    }

    public async Task<LakehouseModels.SqlEndpointProperties> GetSqlEndpointForLakehouse(Guid WorkspaceId, Guid LakehouseId) {

      var lakehouse = await GetLakehouse(WorkspaceId, LakehouseId);

      while ((lakehouse.Properties.SqlEndpointProperties == null) ||
             (lakehouse.Properties.SqlEndpointProperties.ProvisioningStatus != "Success")) {
        lakehouse = await GetLakehouse(WorkspaceId, LakehouseId);
        Thread.Sleep(10000); // wait 10 seconds
      }

      return lakehouse.Properties.SqlEndpointProperties;

    }

    public async Task<Item> CreateWarehouse(Guid WorkspaceId, string WarehouseName) {

      // Item create request for lakehouse des not include item definition
      var createRequest = new CreateItemRequest(WarehouseName, ItemType.Warehouse);

      // create lakehouse
      return await CreateItem(WorkspaceId, createRequest);
    }

    public async Task<WarehouseModels.Warehouse> GetWareHouseByName(Guid WorkspaceId, string WarehouseName) {

      var fabricClient = await GetFabricClient();

      var warehouses = fabricClient.Warehouse.Items.ListWarehouses(WorkspaceId);

      foreach (var warehouse in warehouses) {
        if (warehouse.DisplayName == WarehouseName) {
          return warehouse;
        }
      }

      return null;
    }

    public async Task<WarehouseModels.Warehouse> GetWarehouse(Guid WorkspaceId, Guid WarehouseId) {
      var fabricClient = await GetFabricClient();
      return fabricClient.Warehouse.Items.GetWarehouse(WorkspaceId, WarehouseId).Value;
    }

    public async Task<string> GetSqlConnectionStringForWarehouse(Guid WorkspaceId, Guid WarehouseId) {
      var warehouse = await GetWarehouse(WorkspaceId, WarehouseId);
      return warehouse.Properties.ConnectionString;
    }

    public async Task LoadLakehouseTableFromParquet(Guid WorkspaceId, Guid LakehouseId, string SourceFile, string TableName) {

      var fabricClient = await GetFabricClient();

      var loadTableRequest = new LakehouseModels.LoadTableRequest(SourceFile, LakehouseModels.PathType.File);
      loadTableRequest.Recursive = false;
      loadTableRequest.Mode = LakehouseModels.ModeType.Overwrite;
      loadTableRequest.FormatOptions = new LakehouseModels.Parquet();

      fabricClient.Lakehouse.Tables.LoadTableAsync(WorkspaceId, LakehouseId, TableName, loadTableRequest).Wait();

    }

    public async Task LoadLakehouseTableFromCsv(Guid WorkspaceId, Guid LakehouseId, string SourceFile, string TableName) {

      var fabricClient = await GetFabricClient();

      var loadTableRequest = new LakehouseModels.LoadTableRequest(SourceFile, LakehouseModels.PathType.File);
      loadTableRequest.Recursive = false;
      loadTableRequest.Mode = LakehouseModels.ModeType.Overwrite;
      loadTableRequest.FormatOptions = new LakehouseModels.Csv();

      fabricClient.Lakehouse.Tables.LoadTableAsync(WorkspaceId, LakehouseId, TableName, loadTableRequest).Wait();
    }

    public async Task RunNotebook(Guid WorkspaceId, Item Notebook, RunOnDemandItemJobRequest JobRequest = null) {

      await appLogger.LogSubOperationStart($"Running notebook [{Notebook.DisplayName}]");

      var fabricClient = await GetFabricClient();

      await appLogger.LogOperationInProgress();

      var response = fabricClient.Core.JobScheduler.RunOnDemandItemJob(WorkspaceId, Notebook.Id.Value, "RunNotebook", JobRequest);

      if (response.Status == 202) {

        string location = response.GetLocationHeader();
        int? retryAfter = 20; // response.GetRetryAfterHeader();
        Guid JobInstanceId = response.GetTriggeredJobId();

        Thread.Sleep(retryAfter.Value * 1000);

        var jobInstance = fabricClient.Core.JobScheduler.GetItemJobInstance(WorkspaceId, Notebook.Id.Value, JobInstanceId).Value;

        while (jobInstance.Status != ItemJobStatus.Completed && jobInstance.Status != ItemJobStatus.Failed) {
          await appLogger.LogOperationInProgress();
          Thread.Sleep(retryAfter.Value * 1000);
          jobInstance = fabricClient.Core.JobScheduler.GetItemJobInstance(WorkspaceId, Notebook.Id.Value, JobInstanceId).Value;
        }

        if (jobInstance.Status == ItemJobStatus.Completed) {
          await appLogger.LogOperationComplete();
          return;
        }

        if (jobInstance.Status == ItemJobStatus.Failed) {
          await appLogger.LogSubstep("Notebook execution failed");
          await appLogger.LogSubstep(jobInstance.FailureReason.Message);
        }

      }
      else {
        await appLogger.LogStep("Notebook execution failed when starting");
      }

      await appLogger.LogOperationComplete();

    }

    public async Task RunDataPipeline(Guid WorkspaceId, Item DataPipeline) {

      var fabricClient = await GetFabricClient();

      await appLogger.LogOperationInProgress();

      var response = fabricClient.Core.JobScheduler.RunOnDemandItemJob(WorkspaceId, DataPipeline.Id.Value, "Pipeline");

      if (response.Status == 202) {

        string location = response.GetLocationHeader();
        int? retryAfter = 10; // response.GetRetryAfterHeader();
        Guid JobInstanceId = response.GetTriggeredJobId();

        Thread.Sleep(retryAfter.Value * 1000);

        var jobInstance = fabricClient.Core.JobScheduler.GetItemJobInstance(WorkspaceId, DataPipeline.Id.Value, JobInstanceId).Value;

        while (jobInstance.Status != ItemJobStatus.Completed && jobInstance.Status != ItemJobStatus.Failed) {
          Thread.Sleep(retryAfter.Value * 1000);
          await appLogger.LogOperationInProgress();
          jobInstance = fabricClient.Core.JobScheduler.GetItemJobInstance(WorkspaceId, DataPipeline.Id.Value, JobInstanceId).Value;
        }

        if (jobInstance.Status == ItemJobStatus.Completed) {
          return;
        }

        if (jobInstance.Status == ItemJobStatus.Failed) {
          await appLogger.LogStep("Data pipeline execution failed");
          await appLogger.LogSubstep(jobInstance.FailureReason.Message);
        }

      }
      else {
        await appLogger.LogStep("Data pipeline execution failed when starting");
      }
    }

    public async Task CreateShortcut(Guid WorkspaceId, Guid LakehouseId, CreateShortcutRequest CreateShortcutRequest) {
      var fabricClient = await GetFabricClient();
      var response = fabricClient.Core.OneLakeShortcuts.CreateShortcut(WorkspaceId, LakehouseId, CreateShortcutRequest).Value;
    }

    public async Task<Shortcut> CreateAdlsGen2Shortcut(Guid WorkspaceId, Guid LakehouseId, string Name, string Path, Uri Location, string Subpath, Guid ConnectionId) {

      var fabricClient = await GetFabricClient();

      var target = new CreatableShortcutTarget {
        AdlsGen2 = new AdlsGen2(Location, Subpath, ConnectionId)
      };

      var createRequest = new CreateShortcutRequest(Path, Name, target);

      return fabricClient.Core.OneLakeShortcuts.CreateShortcut(WorkspaceId, LakehouseId, createRequest).Value;

    }

    // create different types of connections

    public async Task<Connection> CreateAnonymousWebConnection(string Url, Workspace TargetWorkspace = null) {

      string displayName = string.Empty;

      if (TargetWorkspace != null) {
        displayName += $"FTM-Workspace[{TargetWorkspace.Id.ToString()}]-";
      }

      displayName += $"Web";

      string connectionType = "Web";
      string creationMethod = "Web";

      var creationMethodParams = new List<ConnectionDetailsParameter> {
   new ConnectionDetailsTextParameter("url", Url)
 };

      var createConnectionDetails = new CreateConnectionDetails(connectionType, creationMethod, creationMethodParams);

      Credentials credentials = new AnonymousCredentials();

      var createCredentialDetails = new CreateCredentialDetails(credentials) {
        SingleSignOnType = SingleSignOnType.None,
        ConnectionEncryption = ConnectionEncryption.NotEncrypted,
        SkipTestConnection = false
      };

      var createConnectionRequest = new CreateCloudConnectionRequest(displayName,
                                                                     createConnectionDetails,
                                                                     createCredentialDetails);

      var connection = await CreateConnection(createConnectionRequest);

      return connection;
    }

    public async Task<Connection> CreateAzureStorageConnectionWithWorkspaceIdentity(string Server, string Path, bool ReuseExistingConnection = false) {

      string displayName = $"FTM-ADLS-WorkspaceIdentity-{Server}-{Path}";

      string connectionType = "AzureDataLakeStorage";
      string creationMethod = "AzureDataLakeStorage";

      var creationMethodParams = new List<ConnectionDetailsParameter> {
   new ConnectionDetailsTextParameter("server", Server),
   new ConnectionDetailsTextParameter("path", Path)
 };

      var createConnectionDetails = new CreateConnectionDetails(connectionType, creationMethod, creationMethodParams);

      Credentials creds = new WorkspaceIdentityCredentials();

      var createCredentialDetails = new CreateCredentialDetails(creds) {
        SingleSignOnType = SingleSignOnType.None,
        ConnectionEncryption = ConnectionEncryption.NotEncrypted,
        SkipTestConnection = false
      };

      var createConnectionRequest = new CreateCloudConnectionRequest(displayName,
                                                                     createConnectionDetails,
                                                                     createCredentialDetails);

      return await CreateConnection(createConnectionRequest);

    }

    public async Task<Connection> CreateAzureStorageConnectionWithSasToken(string Server, string Path) {

      string displayName = $"FTM-ADLS-SAS-[{Server}{Path}]";

      string connectionType = "AzureDataLakeStorage";
      string creationMethod = "AzureDataLakeStorage";

      var creationMethodParams = new List<ConnectionDetailsParameter> {
   new ConnectionDetailsTextParameter("server", Server),
   new ConnectionDetailsTextParameter("path", Path)
 };

      var createConnectionDetails = new CreateConnectionDetails(connectionType, creationMethod, creationMethodParams);

      Credentials creds = new SharedAccessSignatureCredentials(azureStorageSasToken); ;

      var createCredentialDetails = new CreateCredentialDetails(creds) {
        SingleSignOnType = SingleSignOnType.None,
        ConnectionEncryption = ConnectionEncryption.NotEncrypted,
        SkipTestConnection = false
      };

      var createConnectionRequest = new CreateCloudConnectionRequest(displayName,
                                                                     createConnectionDetails,
                                                                     createCredentialDetails);

      var connection = await CreateConnection(createConnectionRequest);

      return connection;
    }

    public async Task<Connection> CreateSqlConnectionWithWorkspaceIdentity(string Server, string Database, Workspace TargetWorkspace = null, Item TargetLakehouse = null) {

      string displayName = "FTM-SQL-WorkspaceIdentity-";

      if (TargetWorkspace != null) {
        displayName += $"Workspace[{TargetWorkspace.Id.ToString()}]-";
        if (TargetLakehouse != null) {
          displayName += $"Lakehouse[{TargetLakehouse.DisplayName}]";
        }
        else {
          displayName += $"FTM-SQL-WorkspaceIdentity-[{Server}:{Database}]";
        }
      }
      else {
        displayName += $"SQL-WI-{Server}:{Database}";
      }

      string connectionType = "SQL";
      string creationMethod = "Sql";

      var creationMethodParams = new List<ConnectionDetailsParameter> {
      new ConnectionDetailsTextParameter("server", Server),
      new ConnectionDetailsTextParameter("database", Database)
    };

      var createConnectionDetails = new CreateConnectionDetails(connectionType, creationMethod, creationMethodParams);

      Credentials credentials = new WorkspaceIdentityCredentials();

      var createCredentialDetails = new CreateCredentialDetails(credentials) {
        SingleSignOnType = SingleSignOnType.None,
        ConnectionEncryption = ConnectionEncryption.NotEncrypted,
        SkipTestConnection = false
      };

      var createConnectionRequest = new CreateCloudConnectionRequest(displayName,
                                                                     createConnectionDetails,
                                                                     createCredentialDetails);

      var connection = await CreateConnection(createConnectionRequest);

      return connection;

    }

  }
}