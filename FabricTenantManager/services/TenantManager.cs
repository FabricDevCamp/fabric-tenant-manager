using FabricTenantManager.Models;
using Microsoft.Fabric.Api.Core.Models;
using Microsoft.Fabric.Api.Environment.Models;
using Microsoft.Graph;
using FabricModels = Microsoft.Fabric.Api.Core.Models;
using GraphModels = Microsoft.Graph.Models;

namespace FabricTenantManager.Services {
  public class TenantManager {

    private readonly FabricTenantManagerDbService dbService;
    private readonly EntraIdTokenManager tokenManager;
    private readonly MicrosoftGraphApi graphApi;
    private readonly ItemDefinitionFactory itemDefinitionFactory;
    private readonly FabricRestApi fabricRestApi;
    private readonly PowerBiRestApi powerBiRestApi;
    private readonly SignalRLogger appLogger;

    public TenantManager(
        FabricTenantManagerDbService dbService,
        EntraIdTokenManager tokenManager,
        MicrosoftGraphApi graphApi,
        ItemDefinitionFactory ItemDefinitionFactory,
        FabricRestApi fabricRestApi,
        PowerBiRestApi powerBiRestApi,
        SignalRLogger appLogger) {
      this.dbService = dbService;
      this.tokenManager = tokenManager;
      this.graphApi = graphApi;
      this.itemDefinitionFactory = ItemDefinitionFactory;
      this.fabricRestApi = fabricRestApi;
      this.powerBiRestApi = powerBiRestApi;
      this.appLogger = appLogger;
    }


    public async Task<bool> DoesCustomerTenantExist(string Customer) {
      return await dbService.DoesTenantExist(Customer);
    }

    public async Task<IList<FabricTenant>> GetTenants() {
      return await dbService.GetTenants();
    }

    public async Task<FabricTenant> GetTenant(string TenantName) {
      FabricTenant tenant = await dbService.GetTenant(TenantName);

      return tenant;

    }

    public async Task OnboardTenant(string Customer, string SolutionName) {

      try {
    
        FabricTenant tenant = null;
        Workspace workspace = null;

        // check if customer exists
        if (await DoesCustomerTenantExist(Customer)) {
          tenant = await dbService.GetTenant(Customer);
          await appLogger.LogSolution($"Deploying solution to existing customer tenant [{Customer}]");
          await appLogger.LogStep($"Setting identity for API calling context to customer [{Customer}]");
          await fabricRestApi.SetFabricClientIdentity(tenant.ClientId);
          await powerBiRestApi.SetPowerBiClientIdentity(tenant.ClientId);
          await fabricRestApi.EnsureAccessToFabricApis();
          workspace = await fabricRestApi.GetWorkspace(new Guid(tenant.WorkspaceId));
        }
        else {
          await appLogger.LogSolution($"Onboarding new customer tenant [{Customer}]");
          tenant = await graphApi.CreateEntraIdApp(Customer);
          await appLogger.LogSubstep($"Writing tenant metadata for Entra Id App to FabricTenantManagerDb");
          await dbService.CreateTenant(tenant);
          await appLogger.LogSubstep($"Waiting for Entra app provisioning to complete");
          await Task.Delay(15000);
          tenant = await dbService.GetTenant(Customer);
          await appLogger.LogStep($"Setting identity for API calling context to customer [{Customer}]");
          await fabricRestApi.SetFabricClientIdentity(tenant.ClientId);
          await powerBiRestApi.SetPowerBiClientIdentity(tenant.ClientId);
          await fabricRestApi.EnsureAccessToFabricApis();
        }

        string workspaceDisplayName = $"Tenant-{Customer}";
        workspace = await fabricRestApi.CreateWorkspace(workspaceDisplayName);

        await dbService.UpdateTenantWorkspaceInfo(Customer, workspace.Id.ToString(), workspace.DisplayName);

        var environment = TargetEnvironments.GetEnvironmentByName(Customer);
        string dataFileUrl = environment.AdlsServerPath + environment.AdlsPath;

        await DeploySolution(tenant, workspace, SolutionName, environment);
     
        await appLogger.LogStep("Tenant workspace provisioning process complete");
        await appLogger.SendClientRedirect($"/Home/Tenant/?TenantName={Customer}");
      }
      catch (Exception ex) {
        // unhandled exeption occured on bckground thread
        await appLogger.LogException(ex);
      }

    }

    public async Task DeleteTenant(string TenantName) {
      FabricTenant tenant = await dbService.GetTenant(TenantName);
      if (tenant.WorkspaceId != null) {
        await fabricRestApi.SetFabricClientIdentity(tenant.ClientId);
        await fabricRestApi.DeleteWorkspace(new Guid(tenant.WorkspaceId));
        await fabricRestApi.DeleteAllTenantResources();
      }
      await graphApi.DeleteApplication(tenant.ApplicationObjectId);
      await dbService.DeleteTenant(tenant);
    }

    public Dictionary<string, string> AvailableSolutions {
      get {
        return new Dictionary<string, string> {
          {"PowerBiSolution","Power BI Solution" },
          {"MedallionSolution","Medallion Solution" },
        };
      }
    }

    public List<TargetEnvironment> AvailableCustomers {
      get {
        return TargetEnvironments.CustomerTenants.AllTenants.ToList();
      }
    }

    public async Task CreateAndBindSemanticModelConnecton(FabricModels.Workspace Workspace, Guid SemanticModelId, FabricModels.Item Lakehouse = null) {

      await appLogger.LogSubstep($"Querying semantic model datasources to create and bind connection");
      var datasources = (await powerBiRestApi.GetDatasourcesForSemanticModel(Workspace.Id, SemanticModelId)).Value;

      foreach (var datasource in datasources) {

        await appLogger.LogSubstep($"Datasources type is [{datasource.DatasourceType}]");

        if (datasource.DatasourceType.ToLower() == "sql") {

          await appLogger.LogSubstep($"Querying semantic model datasources to create and bind connection");

          string sqlEndPointServer = datasource.ConnectionDetails.Server;
          string sqlEndPointDatabase = datasource.ConnectionDetails.Database;

          // you cannot create the connection until your configure a service principal
          await appLogger.LogSubstep($"Creating connection for semantic model");
          var sqlConnection = await fabricRestApi.CreateSqlConnectionWithWorkspaceIdentity(sqlEndPointServer, sqlEndPointDatabase, Workspace, Lakehouse);
          await appLogger.LogSubstep($"Binding connection to semantic model");
          await powerBiRestApi.BindSemanticModelToConnection(Workspace.Id, SemanticModelId, sqlConnection.Id);
        }
      }
    }

    public async Task DeploySolution(FabricTenant tenant, Workspace TargetWorkspace, string SolutionName, TargetEnvironment TargetEnvironment) {
      string dataFileUrl = TargetEnvironment.AdlsServerPath + TargetEnvironment.AdlsPath;
      switch (SolutionName) {
        case "PowerBiSolution":
          await DeployPowerBiSolution(TargetWorkspace, TargetEnvironment);
          await dbService.UpdateTenantSolutionDeploymentInfo(tenant.Name, "Power BI Solution", dataFileUrl);
          break;
        case "MedallionSolution":
          await DeployMedallionSolution(TargetWorkspace, TargetEnvironment);
          await dbService.UpdateTenantSolutionDeploymentInfo(tenant.Name, "Medallion Solution", dataFileUrl);
          break;
        default:
          await appLogger.LogStep($"Deploy command has unknown solution name [{SolutionName}]");
          break;
      }
    }

    public async Task<Item> CreateAdlsVariableLibrary(Guid workspaceId, string DisplayName, TargetEnvironment TargetEnvironment, Guid? FolderId = null) {

      var variableLibraryFactory = new VariableLibraryFactory(DisplayName);

      // add variables and defaults values for dev environment
      foreach (var pameter in TargetEnvironment.Parameters) {
        variableLibraryFactory.AddVariable(pameter.Key, pameter.Value);
      }

      var connection = await fabricRestApi.CreateAzureStorageConnectionWithSasToken(TargetEnvironment.AdlsServerPath, TargetEnvironment.AdlsPath);
      var connectionId = connection.Id.ToString();
      variableLibraryFactory.AddVariable(EnvironmentConstants.adlsConnectionIdParameter, connectionId);
      variableLibraryFactory.AddConnectionVariable(EnvironmentConstants.adlsConnectionParameter, connectionId);

      // if target environment is dev then add value sets for test and prod
      if (TargetEnvironment == TargetEnvironments.Dev) {


        // add valueset with overrides for test environment
        TargetEnvironment envTest = TargetEnvironments.Test;
        Valueset valuesetTest = new Valueset(envTest.Name);

        foreach (var pameter in envTest.Parameters) {
          valuesetTest.AddVariableOverride(pameter.Key, pameter.Value);
        }

        var testConnection = await fabricRestApi.CreateAzureStorageConnectionWithSasToken(envTest.AdlsServerPath, envTest.AdlsPath);
        var testConnectionId = testConnection.Id.ToString();
        valuesetTest.AddVariableOverride(EnvironmentConstants.adlsConnectionIdParameter, testConnectionId);
        valuesetTest.AddConnectionVariableOverride(EnvironmentConstants.adlsConnectionParameter, testConnectionId);

        variableLibraryFactory.AddValueset(valuesetTest);

        // add valueset with overrides for prod environment
        TargetEnvironment envProd = TargetEnvironments.Prod;
        Valueset valuesetProd = new Valueset(envProd.Name);

        foreach (var pameter in envProd.Parameters) {
          valuesetProd.AddVariableOverride(pameter.Key, pameter.Value);
        }

        var prodConnection = await fabricRestApi.CreateAzureStorageConnectionWithSasToken(envProd.AdlsServerPath, envProd.AdlsPath);
        var prodConnectionId = prodConnection.Id.ToString();
        valuesetProd.AddVariableOverride(EnvironmentConstants.adlsConnectionIdParameter, prodConnectionId);
        valuesetProd.AddConnectionVariableOverride(EnvironmentConstants.adlsConnectionParameter, prodConnectionId);

        variableLibraryFactory.AddValueset(valuesetProd);

      }


      // create variable library

      var variableLibraryCreateRequest = itemDefinitionFactory.GetVariableLibraryCreateRequest(variableLibraryFactory, FolderId);
      return await fabricRestApi.CreateItem(workspaceId, variableLibraryCreateRequest);

    }

    public async Task<Workspace> DeployPowerBiSolution(Workspace TargetWorkspace, TargetEnvironment CurrentTargetEnvironment) {

      string solutionFolder = "Power BI Solution";
      string semanticModelDefinition = $"{solutionFolder}\\Product Sales Imported Model.SemanticModel";
      string reportDefinition = $"{solutionFolder}\\Product Sales Summary.Report";

      await appLogger.LogStep("Deploy Power BI Solution with Imported Semantic Model and Report");
      await appLogger.LogSubstep($"Deployment using target environment [{CurrentTargetEnvironment.Name}]");
      await appLogger.LogSubstep($"Data files: {CurrentTargetEnvironment.AdlsDataFilesRoot}");

      await fabricRestApi.UpdateWorkspaceDescription(TargetWorkspace.Id, "Power BI Solution");

      var modelCreateRequest =
          itemDefinitionFactory.GetCreateImportedModelRequestFromFolder(
                semanticModelDefinition,
                CurrentTargetEnvironment.AdlsDataFilesRoot);

      var model = await fabricRestApi.CreateItem(TargetWorkspace.Id, modelCreateRequest);

      var azureStorageDatasource = await powerBiRestApi.GetAzureStorageDatasource(TargetWorkspace.Id, model.Id.Value);

      var connection = await fabricRestApi.CreateAzureStorageConnectionWithSasToken(
        azureStorageDatasource.Server,
        azureStorageDatasource.Path);

      await appLogger.LogSubstep($"Binding connection to semantic model");
      await powerBiRestApi.BindSemanticModelToConnection(TargetWorkspace.Id, model.Id.Value, connection.Id);

      await appLogger.LogSubOperationStart($"Refreshing semantic model");
      await powerBiRestApi.RefreshDataset(TargetWorkspace.Id, model.Id.Value);
      await appLogger.LogOperationComplete();

      var reportCreateRequest = itemDefinitionFactory.GetCreateReportRequestFromFolder(reportDefinition, model.Id.Value.ToString());

      var report = await fabricRestApi.CreateItem(TargetWorkspace.Id, reportCreateRequest);

      return TargetWorkspace;

    }

    public async Task<Workspace> DeployMedallionSolution(Workspace TargetWorkspace, TargetEnvironment CurrentTargetEnvironment) {

      // provision workspace identity for semantic model to connect to lakehouse
      await fabricRestApi.ProvisionWorkspaceIdentity(TargetWorkspace.Id);

      string solutionFolder = "Medallion Solution";
      string stagingFolderName = "staging";
      string bronzeLakehouseDefinition = $"{solutionFolder}\\{stagingFolderName}\\sales_bronze.Lakehouse";
      string silverLakehouseDefinition = $"{solutionFolder}\\{stagingFolderName}\\sales_silver.Lakehouse";
      string silverNotebook1Definition = $"{solutionFolder}\\{stagingFolderName}\\Build 01 Silver Tables.Notebook";
      string connectionModelNotebookDefinition = $"{solutionFolder}\\{stagingFolderName}\\Connect Model to Lakehouse.Notebook";

      string goldLakehouseDefinition = $"{solutionFolder}\\sales.Lakehouse";
      string goldNotebookDefinition = $"{solutionFolder}\\{stagingFolderName}\\Build 02 Gold Tables.Notebook";

      string semanticModelDefinition = $"{solutionFolder}\\Product Sales DirectLake Model.SemanticModel";
      string[] reportDefinitions = {
      $"{solutionFolder}\\Product Sales Summary.Report",
      $"{solutionFolder}\\Product Sales Time Intelligence.Report",
      $"{solutionFolder}\\Product Sales Top 10 Cities.Report"
    };

      await appLogger.LogStep("Deploy Medallion Solution with 3 lakehouses and DirectLake semantic model");
      await appLogger.LogSubstep($"Deployment using target environment [{CurrentTargetEnvironment.Name}]");
      await appLogger.LogSubstep($"Adls server: {CurrentTargetEnvironment.AdlsServerPath}");
      await appLogger.LogSubstep($"Adls path: {CurrentTargetEnvironment.AdlsPath}");

      await fabricRestApi.UpdateWorkspaceDescription(TargetWorkspace.Id, "Medallion Solution");

      var stagingFolder = await fabricRestApi.CreateFolder(TargetWorkspace.Id, stagingFolderName);
      var stagingFolderId = stagingFolder.Id.Value;

      Item variableLibrary = await CreateAdlsVariableLibrary(TargetWorkspace.Id, "environment_settings", CurrentTargetEnvironment, stagingFolderId);

      // create bronze lakehouse with ADLS Gen2 shortcut which uses variables from the variable library
      var bronzeLakehouseCreateRequest = itemDefinitionFactory.GetCreateItemRequestFromFolder(bronzeLakehouseDefinition, stagingFolderId);
      var bronzeLakehouse = await fabricRestApi.CreateItem(TargetWorkspace.Id, bronzeLakehouseCreateRequest);

      // create silver lakehouse and run notebook to build silver tables
      var silverLakehouseCreateRequest = itemDefinitionFactory.GetCreateItemRequestFromFolder(silverLakehouseDefinition, stagingFolderId);
      var silverLakehouse = await fabricRestApi.CreateItem(TargetWorkspace.Id, silverLakehouseCreateRequest);

      var silverNotebookCreateRequest = itemDefinitionFactory.GetCreateNotebookRequestFromFolder(
        silverNotebook1Definition,
        TargetWorkspace.Id.ToString(),
        silverLakehouse.Id.Value.ToString(),
        stagingFolderId);

      var silverNotebook = await fabricRestApi.CreateItem(TargetWorkspace.Id, silverNotebookCreateRequest);
      await fabricRestApi.RunNotebook(TargetWorkspace.Id, silverNotebook);

      // refresh silver metadata
      await appLogger.LogStep("Querying silver lakehouse properties to get SQL endpoint connection info");
      var silverSqlEndpoint = await fabricRestApi.GetSqlEndpointForLakehouse(TargetWorkspace.Id, silverLakehouse.Id.Value);
      await appLogger.LogSubstep($"Server: {silverSqlEndpoint.ConnectionString}");
      await appLogger.LogSubstep($"Database: " + silverSqlEndpoint.Id);

      await appLogger.LogStep("Refreshing silver lakehouse table schema");
      await fabricRestApi.RefreshLakehouseTableSchema(TargetWorkspace.Id, new Guid(silverSqlEndpoint.Id));
      await appLogger.LogSubstep("Refreshing operation complete");

      // create gold lakehouse and run notebook to build gold tables
      var goldLakehouseCreateRequest = itemDefinitionFactory.GetCreateItemRequestFromFolder(goldLakehouseDefinition);
      var goldLakehouse = await fabricRestApi.CreateItem(TargetWorkspace.Id, goldLakehouseCreateRequest);

      var goldNotebookCreateRequest = itemDefinitionFactory.GetCreateNotebookRequestFromFolder(
        goldNotebookDefinition,
        TargetWorkspace.Id.ToString(),
        goldLakehouse.Id.Value.ToString(),
        stagingFolderId);

      var goldNotebook = await fabricRestApi.CreateItem(TargetWorkspace.Id, goldNotebookCreateRequest);
      await fabricRestApi.RunNotebook(TargetWorkspace.Id, goldNotebook);

      await appLogger.LogStep("Querying gold lakehouse properties to get SQL endpoint connection info");
      var goldSqlEndpoint = await fabricRestApi.GetSqlEndpointForLakehouse(TargetWorkspace.Id, goldLakehouse.Id.Value);
      await appLogger.LogSubstep($"Server: {goldSqlEndpoint.ConnectionString}");
      await appLogger.LogSubstep($"Database: " + goldSqlEndpoint.Id);

      await appLogger.LogStep("Refreshing gold lakehouse table schema");
      await fabricRestApi.RefreshLakehouseTableSchema(TargetWorkspace.Id, new Guid(goldSqlEndpoint.Id));
      await appLogger.LogSubstep("Refreshing operation complete");

      var modelCreateRequest = itemDefinitionFactory.GetCreateItemRequestFromFolder(semanticModelDefinition);
      modelCreateRequest.Definition =
        itemDefinitionFactory.UpdateItemDefinitionPart(
          modelCreateRequest.Definition,
          "definition/expressions.tmdl",
          new Dictionary<string, string>() {
            {"abc-xyz.datawarehouse.fabric.microsoft.com", goldSqlEndpoint.ConnectionString }
          });
      var model = await fabricRestApi.CreateItem(TargetWorkspace.Id, modelCreateRequest);

      var connection = await fabricRestApi.CreateSqlConnectionWithWorkspaceIdentity(
        goldSqlEndpoint.ConnectionString,
        goldSqlEndpoint.Id,
        TargetWorkspace,
        goldLakehouse);

      await appLogger.LogSubstep($"Binding connection to semantic model");
      await powerBiRestApi.BindSemanticModelToConnection(TargetWorkspace.Id, model.Id.Value, connection.Id);

      await appLogger.LogSubOperationStart($"Refreshing semantic model");
      await powerBiRestApi.RefreshDataset(TargetWorkspace.Id, model.Id.Value);
      await appLogger.LogOperationComplete();

      foreach (string reportDefinition in reportDefinitions) {
        var reportCreateRequest = itemDefinitionFactory.GetCreateReportRequestFromFolder(reportDefinition, model.Id.Value.ToString());
        var report = await fabricRestApi.CreateItem(TargetWorkspace.Id, reportCreateRequest);
      }

      // create bronze lakehouse and add shortcut to data files in ADLS Gen2
      var connectionModelNotebookCreateRequest = itemDefinitionFactory.GetCreateNotebookRequestFromFolder(
        connectionModelNotebookDefinition,
        TargetWorkspace.Id.ToString(),
        goldLakehouse.Id.Value.ToString(),
        stagingFolderId);

      var connectionModelNotebook = await fabricRestApi.CreateItem(TargetWorkspace.Id, connectionModelNotebookCreateRequest);

      return TargetWorkspace;

    }

    public async Task<EmbeddedViewModel> GetEmbeddedViewModel(string Tenant) {

      var tenant = await dbService.GetTenant(Tenant);
      await appLogger.LogStep($"Setting identity for API calling context to customer [{tenant.Name}]");
      await fabricRestApi.SetFabricClientIdentity(tenant.ClientId);
      await powerBiRestApi.SetPowerBiClientIdentity(tenant.ClientId);
      var workspace = await fabricRestApi.GetWorkspace(new Guid(tenant.WorkspaceId));

      var vm = await powerBiRestApi.GetEmbeddedViewModel(workspace.Id);
      return vm;
    }

  }
}
