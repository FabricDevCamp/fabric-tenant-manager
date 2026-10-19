using System.Collections.Generic;
using Microsoft.EntityFrameworkCore;
using System.ComponentModel.DataAnnotations;

namespace FabricTenantManager.Models {

  
  public class FabricTenant {
    [Key]
    public string Name { get; set; }
    public string ApplicationDisplayName { get; set; }
    public string ApplicationObjectId { get; set; }
    public string ClientId { get; set; }
    public string ServicePrincipalId { get; set; }
    public string WorkspaceDisplayName { get; set; }
    public string WorkspaceId { get; set; }
    public string WorkspaceUrl { get; set; }
    public string SolutionName { get; set; }
    public string DataFileUrl { get; set; }
    public DateTime Created { get; set; }
    public DateTime SolutionDeployed { get; set; }
  }

  public class FabricTenantManagerDb : DbContext {

    public FabricTenantManagerDb(DbContextOptions<FabricTenantManagerDb> options) : base(options) { }

    public DbSet<FabricTenant> Tenants { get; set; }

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder) { }

  }
}
