using System.Collections.Generic;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.Fabric.Api.Core.Models;
using Microsoft.TeamFoundation.Core.WebApi;

namespace FabricTenantManager.Models {

  public class WorkspaceRow {
    public Workspace Workspace { get; set; }
    public Capacity Capacity { get; set; }
  }

  public class WorkspaceDetails {
    public string DisplayName { get; set; }
    public string Description { get; set; }
    public string WorkspaceId { get; set; }
    public string WorkspaceUrl { get; set; }
    public Capacity Capacity { get; set; }
    public List<Item> WorkpaceItems { get; set; }
    public List<Connection> WorkpaceConnetions { get; set; }
    public IList<WorkspaceRoleAssignment> WorkspaceMembers { get; set; }
  }

  public class OnboardTenantModel {
    public List<SelectListItem> AvailableCustomers{ get; set; }
    public List<SelectListItem> AvailableSolutions{ get; set; }
    public string Customer{ get; set; }
    public string SolutionName { get; set; }
    public string TargetWorkspace { get; set; }
  }


  public class EmbeddedReportViewModel {
    public string ReportId;
    public string Name;
    public string EmbedUrl;
    public string Token;
    public string TenantName;
  }

  public class EmbeddedReport {
    public string id;
    public string name;
    public string datasetId;
    public string embedUrl;
    public string reportType;
  }

  public class EmbeddedDataset {
    public string id;
    public string name;
    public string createReportEmbedURL;
  }

  public class EmbeddedViewModel {
    public string tenantName { get; set; }
    public List<EmbeddedReport> reports { get; set; }
    public List<EmbeddedDataset> datasets { get; set; }
    public string embedToken { get; set; }
  }

}
