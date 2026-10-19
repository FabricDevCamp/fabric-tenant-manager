using FabricTenantManager.Models;
using FabricTenantManager.Services;
using FabricTenantManager.Services.AsyncProcessing;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.Rendering;
using System.Diagnostics;

namespace FabricTenantManager.Controllers {

  [Authorize]
  public class HomeController : Controller {

    private readonly ILogger<HomeController> logger;
    private TenantManager tenantManager;
    private readonly BackgroundTaskDispatcher taskDispatcher;

    public HomeController(ILogger<HomeController> Logger,
                          IConfiguration Configuration,                          
                          TenantManager TenantManager,
                          BackgroundTaskDispatcher TaskDispatcher) {
      logger = Logger;
      tenantManager = TenantManager;
      taskDispatcher = TaskDispatcher;
    }

    public override void OnActionExecuted(ActionExecutedContext context) {
      base.OnActionExecuted(context);
    }

    public async Task<IActionResult> Index() {
      var viewModel = await tenantManager.GetTenants();
      return View(viewModel);
    }

    public async Task<IActionResult> Tenant(string TenantName) {
      var viewModel = await tenantManager.GetTenant(TenantName);
      return View(viewModel);
    }

    public IActionResult OnboardTenant() {

      var exportsPickList = tenantManager.AvailableSolutions.Select(
            solution => new SelectListItem {
              Text = solution.Value,
              Value = solution.Key
            }).ToList();

      var customerPickList = TargetEnvironments.CustomerTenants.AllTenants.Select(
      customer => new SelectListItem {
        Text = customer.Name,
        Value = customer.Name
      }).ToList();

      return View(new OnboardTenantModel {
        AvailableSolutions = exportsPickList,
        AvailableCustomers = customerPickList
      });

    }

    [HttpPost]
    public async Task<IActionResult> OnboardTenant(string Customer, string SolutionName) {
      await taskDispatcher.OnboardTenant(Customer, SolutionName);
      return RedirectToAction("AsyncOperationsMonitor");
    }

    public async Task<IActionResult> DeleteTenant(string TenantName) {
      await this.tenantManager.DeleteTenant(TenantName);
      return RedirectToAction("Index");
    }

    public async Task<IActionResult> LaunchAsyncMonitor() {
      await Task.FromResult(0);
      return View();
    }

    public async Task<IActionResult> AsyncOperationsMonitor() {
      await Task.FromResult(0);
      return View();
    }

    public async Task<IActionResult> Embed(string TenantName) {
      var viewModel = await tenantManager.GetEmbeddedViewModel(TenantName);
      return View(viewModel);
    }

    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    public IActionResult Error() {
      return View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
    }

  }
}
