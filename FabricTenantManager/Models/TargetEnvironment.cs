public class EnvironmentConstants {
  public const string AzureStorageAccountName = "fabricdevcampdemos";
  public const string AzureStorageServer = $"https://{AzureStorageAccountName}.dfs.core.windows.net/";
  public const string AzureStorageContainer = "sampledata";
  public const string AzureStorageContainerRootPath = "/productsales/";
  public const string AzureStoragePathRoot = AzureStorageContainer + AzureStorageContainerRootPath;
  public const string adlsServerPathParameter = "adls_server";
  public const string adlsContainerNameParameter = "adls_container_name";
  public const string adlsContainerPathParameter = "adls_container_path";
  public const string adlsPathParameter = "adls_path";
  public const string adlsConnectionIdParameter = "adls_connection_id";
  public const string adlsConnectionParameter = "adls_connection";
}

public class TargetEnvironment {
  public string Name { get; set; }
  public string EnvironmentPath { get; set; }
  public Dictionary<string, string> Parameters { get; set; }

  public TargetEnvironment(string Name, string EnvironmentPath) {
    this.Name = Name;
    this.EnvironmentPath = EnvironmentPath;
    Parameters = new Dictionary<string, string>();
    populateParameters();
  }

  public string TenantWorkspaceName {
    get {
      return $"Tenant-{Name}";
    }

  }

  public void AddParameter(string ParameterName, string Value) {
    Parameters.Add(ParameterName, Value);
  }

  public string AdlsServerPath = EnvironmentConstants.AzureStorageServer;
  public string AdlsContainerName = EnvironmentConstants.AzureStorageContainer;
  public string AdlsContainerPath { get { return EnvironmentConstants.AzureStorageContainerRootPath + EnvironmentPath; } }
  public string AdlsPath { get { return $"{AdlsContainerName}{AdlsContainerPath}"; } }
  public string AdlsDataFilesRoot { get { return $"{AdlsServerPath}{AdlsPath}"; } }

  private void populateParameters() {
    Parameters.Add(EnvironmentConstants.adlsServerPathParameter, AdlsServerPath);
    Parameters.Add(EnvironmentConstants.adlsContainerNameParameter, AdlsContainerName);
    Parameters.Add(EnvironmentConstants.adlsContainerPathParameter, AdlsContainerPath);
    Parameters.Add(EnvironmentConstants.adlsPathParameter, AdlsPath);
  }

}

public class TargetEnvironments {
  public static TargetEnvironment Dev = new TargetEnvironment("dev", "dev/");
  public static TargetEnvironment Test = new TargetEnvironment("test", "test/");
  public static TargetEnvironment Prod = new TargetEnvironment("prod", "prod/");
  public static TargetEnvironment[] AvailableEnvironments = new TargetEnvironment[] { Dev, Test, Prod };
  public static TargetEnvironment GetEnvironmentByName(string Name) {
    switch (Name) {
      case "dev":
        return Dev;
      case "test":
        return Test;
      case "prod":
        return Prod;
      default:
        return CustomerTenants.AllTenants.FirstOrDefault(e => e.Name == Name);
    }
  }
  public class CustomerTenants {
    public static TargetEnvironment AdventureWorks = new TargetEnvironment("AdventureWorks", "customers/adventureworks/");
    public static TargetEnvironment Contoso = new TargetEnvironment("Contoso", "customers/contoso/");
    public static TargetEnvironment Fabrikam = new TargetEnvironment("Fabrikam", "customers/fabrikam/");
    public static TargetEnvironment Northwind = new TargetEnvironment("Northwind", "customers/northwind/");
    public static TargetEnvironment SeamarkFarms = new TargetEnvironment("SeamarkFarms", "customers/seamarkfarms/");
    public static TargetEnvironment Wingtip = new TargetEnvironment("Wingtip", "customers/wingtip/");
    public static TargetEnvironment[] AllTenants = new TargetEnvironment[] { AdventureWorks, Contoso, Fabrikam, Northwind, SeamarkFarms, Wingtip };
  }
}
