using FabricTenantManager.Models;
using Microsoft.EntityFrameworkCore;

namespace FabricTenantManager.Services {
  public class FabricTenantManagerDbService {

    private readonly FabricTenantManagerDb dbContext;
    private readonly SignalRLogger appLogger;

    public FabricTenantManagerDbService(FabricTenantManagerDb context, SignalRLogger logger) {
      dbContext = context;
      appLogger = logger;
    }

    public async Task<IList<FabricTenant>> GetTenants() {
      return await dbContext.Tenants.ToListAsync();
    }

    public async Task<FabricTenant> GetTenant(string TenantName) {
      return  await dbContext.Tenants.Where(tenant => tenant.Name == TenantName).FirstAsync();
    }

    public async Task<bool> DoesTenantExist(string TenantName) {
      return await dbContext.Tenants.AnyAsync(tenant => tenant.Name == TenantName);
    }

    public async Task UpdateTenantWorkspaceInfo(string TenantName, string WorkspaceId, string WorkspaceDisplayName) {
      FabricTenant tenant = await GetTenant(TenantName);
      tenant.WorkspaceDisplayName = WorkspaceDisplayName;
      tenant.WorkspaceId = WorkspaceId;
      tenant.WorkspaceUrl = $"https://app.powerbi.com/groups/{WorkspaceId}/list?experience=power-bi";
      dbContext.Tenants.Update(tenant);
      await dbContext.SaveChangesAsync();
    }

    public async Task UpdateTenantSolutionDeploymentInfo(string TenantName, string SolutionName, string DataFileUrl) {
      FabricTenant tenant = await GetTenant(TenantName);
      tenant.SolutionName = SolutionName;
      tenant.DataFileUrl = DataFileUrl;
      tenant.SolutionDeployed = DateTime.UtcNow.AddHours(-4);
      dbContext.Tenants.Update(tenant);
      await dbContext.SaveChangesAsync();
    }

    public async Task CreateTenant(FabricTenant tenant) {
      try {
        tenant.Created = DateTime.UtcNow.AddHours(-4);
        await dbContext.Tenants.AddAsync(tenant);
        await dbContext.SaveChangesAsync();
      }
      catch (Exception ex) {
        await appLogger.LogException(ex);
        throw;
      }

    }

    public async Task DeleteTenant(FabricTenant tenant) {
      dbContext.Tenants.Remove(tenant);
      await dbContext.SaveChangesAsync();
    }

  }
}
