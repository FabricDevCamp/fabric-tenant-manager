using FabricTenantManager.Models;
using FabricTenantManager.Services;
using Microsoft.Fabric.Api.Core.Models;
using Microsoft.Fabric.Api.Lakehouse.Models;
using System.Text;
using System.Text.Json;

public class ItemDefinitionFactory {

  private readonly IWebHostEnvironment hostingEnvironment;
  private SignalRLogger appLogger;
  private FabricRestApi fabricRestApi;

  public ItemDefinitionFactory(IWebHostEnvironment HostingEnvironment,
                               SignalRLogger AppLogger,
                               FabricRestApi FabricRestApi) {

    hostingEnvironment = HostingEnvironment;
    appLogger = AppLogger;
    fabricRestApi = FabricRestApi;
  }

  public ItemDefinition UpdateItemDefinitionPart(ItemDefinition ItemDefinition, string PartPath, Dictionary<string, string> SearchReplaceText) {
    var itemPart = ItemDefinition.Parts.Where(part => part.Path == PartPath).FirstOrDefault();
    if (itemPart != null) {
      ItemDefinition.Parts.Remove(itemPart);
      itemPart.Payload = SearchAndReplaceInPayload(itemPart.Payload, SearchReplaceText);
      ItemDefinition.Parts.Add(itemPart);
    }
    return ItemDefinition;
  }


  public string SearchAndReplaceInPayload(string Payload, Dictionary<string, string> SearchReplaceText) {
    byte[] PayloadBytes = Convert.FromBase64String(Payload);
    string PayloadContent = Encoding.UTF8.GetString(PayloadBytes, 0, PayloadBytes.Length);
    foreach (var entry in SearchReplaceText.Keys) {
      var sourceValue = entry;
      var targetValue = SearchReplaceText[entry];
      if (sourceValue != targetValue) {
        appLogger.LogSubstep($"Replacing [{sourceValue}]").Wait();
        appLogger.LogSubstep($"---- With [{targetValue}]").Wait();
        PayloadContent = PayloadContent.Replace(sourceValue, targetValue);
      }
    }
    return Convert.ToBase64String(Encoding.UTF8.GetBytes(PayloadContent));
  }

  public string GetTemplateFile(string Path) {
    string filePath = $@"{hostingEnvironment.WebRootPath}\templates\TemplateFiles\{Path}";
    return File.ReadAllText(filePath);
  }

  public List<string> GetAvailableSolutionImportFiles() {
    var solutionFiles = new List<string>();
    string solutionFilesPath = $@"{hostingEnvironment.WebRootPath}\templates\FabricSolutionImports\";
    foreach (var file in Directory.GetFiles(solutionFilesPath).ToList()) {
      solutionFiles.Add(file.Replace(solutionFilesPath, ""));
    }
    return solutionFiles;
  }

  public string GetSolutionImportFile(string Path) {
    string filePath = $@"{hostingEnvironment.WebRootPath}\templates\FabricSolutionImports\{Path}";
    return File.ReadAllText(filePath);
  }

  private string GetPartPath(string ItemFolderPath, string FilePath) {
    int ItemFolderPathOffset = ItemFolderPath.Length + 1;
    return FilePath.Substring(ItemFolderPathOffset).Replace("\\", "/");
  }

  public ItemDefinitionPart CreateInlineBase64Part(string Path, string Payload) {
    string base64Payload = Convert.ToBase64String(Encoding.UTF8.GetBytes(Payload));
    return new ItemDefinitionPart(Path, base64Payload, PayloadType.InlineBase64);
  }

  public CreateItemRequest GetCreateItemRequestFromFolder(string ItemFolder, Guid? FolderId = null) {

    appLogger.LogStep($"Generating CreateItemRequest from folder [{ItemFolder}]").Wait();

    string ItemFolderPath = $@"{hostingEnvironment.WebRootPath}\templates\FabricSolutions\{ItemFolder}";

    string metadataFilePath = ItemFolderPath + @"\.platform";
    string metadataFileContent = File.ReadAllText(metadataFilePath);
    PlatformFileMetadata item = JsonSerializer.Deserialize<FabricPlatformFile>(metadataFileContent).metadata;


    CreateItemRequest itemCreateRequest = new CreateItemRequest(item.displayName, item.type);

    var parts = new List<ItemDefinitionPart>();

    List<string> ItemDefinitionFiles = Directory.GetFiles(ItemFolderPath, "*", SearchOption.AllDirectories).ToList<string>();

    foreach (string ItemDefinitionFile in ItemDefinitionFiles) {
      string fileContentBase64 = Convert.ToBase64String(File.ReadAllBytes(ItemDefinitionFile));
      parts.Add(new ItemDefinitionPart(GetPartPath(ItemFolderPath, ItemDefinitionFile), fileContentBase64, "InlineBase64"));
    }

    appLogger.LogSubstep($"Create item request generated with [{parts.Count().ToString()}] item definition parts").Wait();

    itemCreateRequest.Definition = new ItemDefinition(parts);

    if (FolderId.HasValue) {
      itemCreateRequest.FolderId = FolderId.Value;
    }

    return itemCreateRequest;
  }

  public CreateItemRequest GetCreateNotebookRequestFromFolder(string ItemFolder, string WorkspaceId, string LakehouseId, Guid? FolderId = null) {

    CreateItemRequest itemCreateRequest = GetCreateItemRequestFromFolder(ItemFolder, FolderId);
    appLogger.LogStep($"Updating notebook definition dependency on lakehouse with id of [{LakehouseId}]").Wait();
    itemCreateRequest.Definition = UpdateItemDefinitionPart(
      itemCreateRequest.Definition,
      "notebook-content.py",
       new Dictionary<string, string>() {
          {"00000000-0000-0000-0000-000000000000", WorkspaceId },
          {"22222222-2222-2222-2222-222222222222", LakehouseId }
       });


    // temporary fix for notebook settings.json file until bug is fixed
    itemCreateRequest.Definition = UpdateItemDefinitionPart(
      itemCreateRequest.Definition,
      "notebook-settings.json",
       new Dictionary<string, string>() { });


    if (FolderId.HasValue) {
      itemCreateRequest.FolderId = FolderId.Value;
    }

    return itemCreateRequest;
  }

  public CreateItemRequest GetCreateImportedModelRequestFromFolder(string ItemFolder, string AzureStorageUrl, Guid? FolderId = null) {

    CreateItemRequest createImportModelRequest = GetCreateItemRequestFromFolder(ItemFolder, FolderId);

    if (AzureStorageUrl != TargetEnvironments.Dev.AdlsDataFilesRoot) {
      appLogger.LogStep($"Updating item definition to redirect semantic model datasource").Wait();
      createImportModelRequest.Definition = UpdateItemDefinitionPart(
        createImportModelRequest.Definition,
        "definition/expressions.tmdl",
         new Dictionary<string, string>() {
              {TargetEnvironments.Dev.AdlsDataFilesRoot, AzureStorageUrl }
         });
    }
    return createImportModelRequest;
  }

  public CreateItemRequest GetCreateReportRequestFromFolder(string ItemFolder, string SemanticModelId) {
    CreateItemRequest itemCreateRequest = GetCreateItemRequestFromFolder(ItemFolder);
    appLogger.LogSubstep($"Redirecting report to to semantic model [{SemanticModelId}]").Wait();
    itemCreateRequest.Definition = UpdateReportDefinitionWithRedirection(itemCreateRequest.Definition, Guid.Empty, SemanticModelId);
    return itemCreateRequest;
  }

  public ItemDefinition UpdateReportDefinitionWithRedirection(ItemDefinition ReportDefinition, Guid WorkspaceId, string SemanticModelId) {

    var pbirDefinitionPart = ReportDefinition.Parts.Where(part => part.Path == "definition.pbir").First();
    ReportDefinition.Parts.Remove(pbirDefinitionPart);

    string reportDefinitionPartTemplate = GetTemplateFile(@"Reports\definition.pbir");
    string reportDefinitionPartContent = reportDefinitionPartTemplate.Replace("{SEMANTIC_MODEL_ID}", SemanticModelId);
    var reportDefinitionPart = CreateInlineBase64Part("definition.pbir", reportDefinitionPartContent);
    ReportDefinition.Parts.Add(reportDefinitionPart);
    return ReportDefinition;

  }

  public CreateItemRequest GetVariableLibraryCreateRequest(VariableLibraryFactory variableLibraryFactory, Guid? FolderId = null) {
    CreateItemRequest createRquest = new CreateItemRequest(variableLibraryFactory.Name, ItemType.VariableLibrary);

    createRquest.Definition = new ItemDefinition(new List<ItemDefinitionPart>() {
      CreateInlineBase64Part("variables.json", variableLibraryFactory.GetVariableJson())
    });

    foreach (var valueset in variableLibraryFactory.Valuesets) {
      createRquest.Definition.Parts.Add(CreateInlineBase64Part($"valueSets/{valueset.Name}.json", variableLibraryFactory.GetValuesetJson(valueset.Name)));
    }

    var settings_json = GetTemplateFile("VariableLibraries//settings.json");
    createRquest.Definition.Parts.Add(CreateInlineBase64Part($"settings.json", settings_json));

    if (FolderId.HasValue) {
      createRquest.FolderId = FolderId.Value;
    }

    return createRquest;
  }

  public void WriteFileToImportsFolder(string FilePath, string FileContent) {

    FilePath = FilePath.Replace("/", @"\");

    string importsFolder = $@"{hostingEnvironment.WebRootPath}\templates\FabricSolutionImports";

    Directory.CreateDirectory(importsFolder);

    string fullPath = importsFolder + @"\" + FilePath;

    Directory.CreateDirectory(Path.GetDirectoryName(fullPath));

    File.WriteAllText(fullPath, FileContent);

  }

  public void WriteFileToExportsFolder(string FilePath, string FileContent) {

    FilePath = FilePath.Replace("/", @"\");

    string exportsFolder = $@"{hostingEnvironment.WebRootPath}\templates\WorkspaceExports";

    Directory.CreateDirectory(exportsFolder);

    string fullPath = exportsFolder + @"\" + FilePath;

    Directory.CreateDirectory(Path.GetDirectoryName(fullPath));

    File.WriteAllText(fullPath, FileContent);

  }

  public void WriteFileToSolutionExportsFolder(string SolutionName, string ItemFolder, string FilePath, string FileContent, bool ConvertFromBase64 = true) {

    if (ConvertFromBase64) {
      byte[] bytes = Convert.FromBase64String(FileContent);
      FileContent = Encoding.UTF8.GetString(bytes, 0, bytes.Length);
    }

    FilePath = FilePath.Replace("/", @"\");

    string solutionsFolder = $@"{hostingEnvironment.WebRootPath}\templates\SolutionExports\{SolutionName}";
    string itemFolder = $@"{solutionsFolder}\{ItemFolder}";

    Directory.CreateDirectory(itemFolder);

    string fullPath = itemFolder + @"\" + FilePath;

    Directory.CreateDirectory(Path.GetDirectoryName(fullPath));

    File.WriteAllText(fullPath, FileContent);

  }

  public BulkImportItemDefinitionsRequest GetBulkImportRequestFromExportFile(string Path) {
    string exportFileJson = GetSolutionImportFile(Path);
    return GetBulkImportRequestFromExport(exportFileJson);
  }

  public BulkImportItemDefinitionsRequest GetBulkImportRequestFromExport(string exportJson) {
    appLogger.LogStep($"Generating BulkImportItemDefinitionsRequest").Wait();

    var export = JsonSerializer.Deserialize<Dictionary<string, Dictionary<string, string>[]>>(exportJson);
    var parts = export["definitionParts"];
    var definitionParts = new List<ItemDefinitionPart>();
    foreach (var part in parts) {
      string path = part["path"];
      string payload = part["payload"];
      string payloadType = part["payloadType"];
      definitionParts.Add(new ItemDefinitionPart(path, payload, payloadType));
    }
    appLogger.LogSubstep($"Request generated with {definitionParts.Count} definition parts").Wait();
    var request = new BulkImportItemDefinitionsRequest(definitionParts);
    request.Options = new BulkImportItemDefinitionsRequestOptions {
      AllowPairingByName = true

    };

    return request;
  }

  public async Task<BulkImportItemDefinitionsRequest> UpdateImportRequestParameters(BulkImportItemDefinitionsRequest request, Dictionary<string, Dictionary<string, string>> partUpdates) {

    foreach (var partUpdate in partUpdates) {
      var part = request.DefinitionParts.Where(part => part.Path == partUpdate.Key).FirstOrDefault();
      if (part != null) {
        await appLogger.LogStep($"Updating part [{part.Path}]"); ;
        request.DefinitionParts.Remove(part);
        part.Payload = SearchAndReplaceInPayload(part.Payload, partUpdate.Value);
        request.DefinitionParts.Add(part);
      }
    }

    return request;
  }

  public JsonSerializerOptions jsonSerializerOptions = new JsonSerializerOptions {
    WriteIndented = true,
    PropertyNamingPolicy = JsonNamingPolicy.CamelCase
  };

}