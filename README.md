# fabric-tenant-manager
This developer sample is an ASP.NET Core MVC web application built using .NET 10. It demonstrates developing a multitenant application for app-owns-data embedding in Microsoft Fabric using Azure workload identity federation.

## How authentication works

The sample uses three separate identities:

- A **user-assigned managed identity**, named **FabricTenantManager**, is attached to the Azure web app. It calls Microsoft Graph and supplies the assertion used for federated token exchange.
- A **web app registration** authenticates users signing in to the application. Its settings are under `AzureAd`. The current implementation uses a client secret for this sign-in registration; managed identity does not replace that secret.
- A **customer app registration and service principal** are created automatically during onboarding. Each customer app trusts the managed identity through a federated identity credential and calls Fabric and Power BI APIs in that customer's context.

Customer tenants in this sample are logical customer environments within one Microsoft Entra tenant, not separate Entra directories. Keep the web app registration, managed identity, customer registrations, and Fabric resources in the same Entra tenant.

## High-level setup

### 1. Prepare the prerequisites

- An Azure subscription with permission to create a user-assigned managed identity, Azure App Service resources, and an Azure SQL database.
- Microsoft Entra administrative access to grant Microsoft Graph application permissions and create a security group.
- Fabric administrator access to configure tenant settings, and access to manage permissions on an active Fabric capacity. Use a capacity that supports the selected solution, including Spark notebooks and lakehouses for the Medallion Solution, and app-owns-data embedding.
- The .NET 10 SDK on the development machine. Use Windows for this sample: the template-loading code constructs Windows filesystem paths.
- Access to an Azure Data Lake Storage Gen2 account containing the sample product-sales data and a SAS token that can read and list that data.

### 2. Create the managed identity

In the Azure portal, create a **user-assigned managed identity** named **FabricTenantManager**.

Record these values from its overview:

| Azure value | Application configuration |
| --- | --- |
| Tenant ID | `ManagedIdentity:TenantId` |
| Client ID | `ManagedIdentity:ClientId` |
| Object (principal) ID | `ManagedIdentity:PrincipalId` |

The client ID selects the managed identity when requesting tokens. The principal ID is the subject of the customer apps' federated identity credentials; these values are not interchangeable.

### 3. Create the Azure web app and attach the identity

1. Create an Azure App Service plan and a **Windows Web App** using the **.NET 10** runtime.
2. Open the web app's **Identity > User assigned** page, select **Add**, and attach the **FabricTenantManager** managed identity.
3. Enable **HTTPS Only**. Record the web app URL, for example `https://<web-app-name>.azurewebsites.net`.
4. Leave App Service Authentication (Easy Auth) out of this setup; the application already handles sign-in using Microsoft Identity Web.

Attaching the identity enables managed-identity token acquisition in App Service. Setting the client ID in configuration alone does not attach the identity.

### 4. Register the application used for interactive sign-in

In **Microsoft Entra ID > App registrations**, create a separate registration for the web application's sign-in:

1. Select **Accounts in this organizational directory only**.
2. Add a **Web** redirect URI of `https://<web-app-name>.azurewebsites.net/signin-oidc`.
3. For local development, also add `https://localhost:7085/signin-oidc`, matching the HTTPS profile in [launchSettings.json](FabricTenantManager/Properties/launchSettings.json).
4. Create a client secret under **Certificates & secrets** and record its **value**, not its secret ID. Plan to rotate it before expiration.
5. Record the directory's tenant ID and domain, and the registration's application (client) ID, for the `AzureAd` settings below.

Do not give this sign-in registration the provisioning permissions described in the next step. Provisioning runs under the managed identity, not the signed-in user's delegated identity.

### 5. Grant Microsoft Graph permissions to the managed identity

Grant these Microsoft Graph **application permissions** to the managed identity's service principal:

| Permission | Used for |
| --- | --- |
| `Application.ReadWrite.All` | Reading, creating, and deleting customer app registrations; creating service principals; adding federated identity credentials. |
| `GroupMember.ReadWrite.All` | Adding customer service principals to the security group that is allowed to call Fabric APIs. |

These are privileged, tenant-wide permissions. Have an appropriately authorized Entra administrator assign the Microsoft Graph app roles to the **managed identity service principal**, not to the sign-in registration. Azure subscription RBAC roles such as Contributor do not grant Microsoft Graph permissions.

For example, using Microsoft Graph PowerShell with an administrator authorized to grant Microsoft Graph application permissions:

```powershell
Connect-MgGraph -TenantId "<tenant-id>" -Scopes "Application.Read.All", "AppRoleAssignment.ReadWrite.All"

$identityPrincipalId = "<managed-identity-principal-id>"
$graph = Get-MgServicePrincipal -Filter "appId eq '00000003-0000-0000-c000-000000000000'"

foreach ($permission in @("Application.ReadWrite.All", "GroupMember.ReadWrite.All")) {
    $role = $graph.AppRoles | Where-Object {
        $_.Value -eq $permission -and $_.AllowedMemberTypes -contains "Application"
    }
    New-MgServicePrincipalAppRoleAssignment -ServicePrincipalId $identityPrincipalId -BodyParameter @{
        principalId = $identityPrincipalId
        resourceId = $graph.Id
        appRoleId = $role.Id
    }
}
```

Run the assignments once; existing assignments do not need to be recreated. Allow time for permission changes to propagate.

### 6. Configure Fabric access and capacity permissions

1. Create an Entra **security group** with assigned membership, for example **FabricTenantManager-ServicePrincipals**.
2. Copy its object ID and replace the existing hard-coded value of `entraGroupIdForFabricRESTApis` in [MicrosoftGraphApi.cs](FabricTenantManager/services/MicrosoftGraphApi.cs). There is currently no configuration setting for this group ID. The app adds each newly created customer service principal to this group.
3. In Fabric **Tenant settings**, enable the following developer settings for that group:
   - **Service principals can call Fabric public APIs**.
   - **Service principals can create workspaces, connections, and deployment pipelines**.
   - **Embed content in apps** for the intended embedding scenario.
4. Grant the group **Contributor** permission on the target Fabric capacity (or equivalent permission to assign workspaces to it). Each customer service principal creates a workspace directly on that capacity; Azure resource Contributor is not a substitute for Fabric capacity permission.
5. Record the Fabric capacity **GUID**, not its Azure resource ID or display name, as `Fabric:FabricCapacityId`.
6. Record an administrator user's Entra **object ID** as `Fabric:AdminUserId`. The application adds this user as an Admin of each provisioned workspace and an Owner of created connections.

Onboarding automatically configures each customer application's federated identity credential with:

- Issuer: `https://login.microsoftonline.com/<tenant-id>/v2.0`
- Subject: the managed identity's **principal ID**
- Audience: `api://AzureADTokenExchange`

Do not manually create a client secret for each customer app. The app exchanges a managed-identity token for the customer app's token. See [managed identity federation](https://learn.microsoft.com/en-us/entra/workload-id/workload-identity-federation-config-app-trust-managed-identity), [Fabric developer settings](https://learn.microsoft.com/en-us/fabric/admin/service-admin-portal-developer), and [workspace creation permissions](https://learn.microsoft.com/en-us/rest/api/fabric/core/workspaces/create-workspace).

### 7. Create and initialize the database

1. Create an Azure SQL database named **FabricTenantManagerDb**, configure a Microsoft Entra administrator on its logical SQL server, and allow network access from the web app and the machine applying migrations. Use appropriate firewall rules or private networking.
2. Connect to that database as its Entra administrator and create a database user for the managed identity. For a uniquely named identity named **FabricTenantManager**:

   ```sql
   CREATE USER [FabricTenantManager] FROM EXTERNAL PROVIDER;
   ALTER ROLE db_datareader ADD MEMBER [FabricTenantManager];
   ALTER ROLE db_datawriter ADD MEMBER [FabricTenantManager];
   ```

3. Set `FabricTenantManagerDb:ConnectString` to a complete connection string. For managed-identity authentication in App Service:

   ```text
   Server=tcp:<sql-server>.database.windows.net,1433;Initial Catalog=FabricTenantManagerDb;Authentication=Active Directory Managed Identity;User Id=<managed-identity-client-id>;Encrypt=True;TrustServerCertificate=False;
   ```

4. Generate a deployment script from the existing EF Core migrations. Run these commands from the repository root:

   ```powershell
   Set-Location .\FabricTenantManager
   dotnet tool restore --tool-manifest .\dotnet-tools.json
   dotnet ef migrations script --idempotent --output .\FabricTenantManagerDb.sql
   ```

5. Review and execute the generated script against **FabricTenantManagerDb** using an account with schema-change permissions. This creates the `Tenants` table and migration history. Keep the generated script out of source control unless intentionally tracking deployment scripts.

The application does **not** run migrations or create the database at startup. The runtime identity needs data access, not schema-owner permissions. The placeholder connection string in [appsettings.json](FabricTenantManager/appsettings.json) does not specify authentication and is not sufficient by itself.

### 8. Configure Azure Storage and customer data

The storage account, container, and customer paths are currently defined in [TargetEnvironment.cs](FabricTenantManager/Models/TargetEnvironment.cs), not in application settings:

- Default account: `fabricdevcampdemos`
- Endpoint: `https://fabricdevcampdemos.dfs.core.windows.net/`
- Container: `sampledata`
- Root path: `/productsales/`
- Customer folders: `customers/adventureworks/`, `customers/contoso/`, `customers/fabrikam/`, `customers/northwind/`, `customers/seamarkfarms/`, and `customers/wingtip/`.

Obtain authorized access to that sample data, or use your own ADLS Gen2 account and update the constants and customer definitions. Populate each selected customer's folder with the CSV data expected by the supplied semantic models and notebooks; empty folders are not sufficient.

If changing the storage location, also update the development URL in the Power BI solution's [expressions.tmdl](<FabricTenantManager/wwwroot/templates/FabricSolutions/Power BI Solution/Product Sales Imported Model.SemanticModel/definition/expressions.tmdl>) to match `TargetEnvironments.Dev.AdlsDataFilesRoot`. The importer uses that development URL as its search-and-replace source when redirecting the model to a customer folder.

Create a SAS token scoped to the required data, with **Read** and **List** permissions and a suitable expiration. Set `AzureStorage:SasToken` to the SAS token, not the full storage URL. Fabric must be able to reach the storage endpoint through its network rules. Both solutions create SAS-backed storage connections, so managed identity does not eliminate this credential.

### 9. Supply application settings and deploy

Use the web app's **Settings > Environment variables > App settings** to override the placeholders in [appsettings.json](FabricTenantManager/appsettings.json). ASP.NET Core uses double underscores in environment variable names:

| App Service setting | Value |
| --- | --- |
| `AzureAd__Instance` | `https://login.microsoftonline.com/` |
| `AzureAd__Domain` | Your Entra domain, for example `contoso.onmicrosoft.com`. |
| `AzureAd__TenantId` | Your Entra tenant ID. |
| `AzureAd__ClientId` | The interactive sign-in registration's client ID. |
| `AzureAd__ClientSecret` | The sign-in registration's client secret value. |
| `AzureAd__CallbackPath` | `/signin-oidc` |
| `ManagedIdentity__TenantId` | The managed identity's tenant ID; use the same tenant as `AzureAd`. |
| `ManagedIdentity__ClientId` | The user-assigned managed identity's client ID. |
| `ManagedIdentity__PrincipalId` | The user-assigned managed identity's object (principal) ID. |
| `FabricTenantManagerDb__ConnectString` | The complete SQL connection string from step 7. Use this exact key, not the App Service connection-string collection. |
| `Fabric__FabricRestApiBaseUrl` | `https://api.fabric.microsoft.com` |
| `Fabric__FabricCapacityId` | The target Fabric capacity GUID. |
| `Fabric__AdminUserId` | The administrator user's Entra object ID. |
| `AzureStorage__SasToken` | The SAS token for the configured ADLS Gen2 data. |

Store secrets in protected App Service settings or use Key Vault references with the necessary access granted to the web app's identity. Do not commit client secrets, SAS tokens, database credentials, or publish-profile credentials.

Publish [FabricTenantManager.csproj](FabricTenantManager/FabricTenantManager.csproj) to the Windows web app using Visual Studio's **Publish** workflow or your deployment pipeline. Include the entire required [templates folder](FabricTenantManager/wwwroot/templates), including `.platform` metadata files. These files are read at runtime to create Fabric items. Restart the web app after saving its settings.

### 10. Verify the deployment

1. Open the web app URL and sign in with a user from the configured Entra tenant. The home page should load the tenant list from SQL without authentication or database errors.
2. Onboard one of the configured customers using **Power BI Solution** first. Follow the asynchronous operations monitor.
3. Confirm that onboarding creates a `Tenant-App-<customer>` app registration and service principal, adds it to the Fabric security group, and creates its federated credential.
4. Confirm that `Tenant-<customer>` is created on the configured capacity, the configured administrator can open it, and its metadata is recorded in the database.
5. Confirm that the semantic model refresh finishes and the report renders through the application's embedded view.
6. If required, test **Medallion Solution** with another customer. It additionally provisions a workspace identity, lakehouses, notebooks, a variable library, and a Direct Lake semantic model.

If onboarding keeps retrying its first Fabric API call, check security-group membership and tenant settings. For workspace-creation errors, check capacity permissions. For refresh or shortcut errors, check the SAS token, storage networking, and data paths.

## Local development limitation

The HTTPS launch profile uses `https://localhost:7085`. You can configure local settings and exercise sign-in, but the provisioning/token-exchange implementation explicitly uses `ManagedIdentityApplicationBuilder` and requires a supported Azure managed-identity endpoint. An `az login` session or local user credentials do **not** substitute for the managed identity in that flow, even though the Graph client separately uses `DefaultAzureCredential`.

For end-to-end testing without changing the authentication code, deploy to the configured Azure web app. Also note that this is a developer sample: controller access requires sign-in, but there is no separate administrator-role policy restricting provisioning actions. Restrict access to trusted operators before exposing it beyond a controlled development environment.
