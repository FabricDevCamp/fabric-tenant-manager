using FabricTenantManager.Models;
using FabricTenantManager.Services;
using FabricTenantManager.Services.AsyncProcessing;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Rewrite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Identity.Web;
using Microsoft.Identity.Web.UI;

namespace FabricTenantManager {
  public class Program {
    public static void Main(string[] args) {

      var builder = WebApplication.CreateBuilder(args);

      string connectString = builder.Configuration["FabricTenantManagerDb:ConnectString"];
      builder.Services.AddDbContext<FabricTenantManagerDb>(opt => opt.UseSqlServer(connectString));

      builder.Services.AddAuthentication(OpenIdConnectDefaults.AuthenticationScheme)
                      .AddMicrosoftIdentityWebApp(builder.Configuration, "AzureAd");

      builder.Services.AddScoped<EntraIdTokenManager>();
      builder.Services.AddScoped<SignalRLogger>();
      builder.Services.AddScoped<PowerBiRestApi>();
      builder.Services.AddScoped<ItemDefinitionFactory>();
      builder.Services.AddScoped<FabricRestApi>();
      builder.Services.AddScoped<MicrosoftGraphApi>();
      builder.Services.AddScoped<FabricTenantManagerDbService>();
      builder.Services.AddScoped<TenantManager>();
      builder.Services.AddSingleton<BackgroundTaskDispatcher>();

      builder.Services.AddHostedService<BackgroundProvisioningService>();
      builder.Services.AddSingleton<IBackgroundTaskQueue, BackgroundTaskQueue>();

      builder.Services.AddSignalR(config => config.EnableDetailedErrors = true);


      // Add services to the container.
      builder.Services.AddControllersWithViews().AddMicrosoftIdentityUI();

      builder.Services.AddRazorPages();

      var app = builder.Build();

      // Configure the HTTP request pipeline.
      if (!app.Environment.IsDevelopment()) {
        app.UseExceptionHandler("/Home/Error");
        // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
        app.UseHsts();
      }

      app.UseHttpsRedirection();
      app.UseStaticFiles();

      app.UseRouting();

      app.UseAuthentication();
      app.UseAuthorization();

      app.MapHub<ClientNotificaionHub>("/notificationHub");

      app.MapControllerRoute(
          name: "default",
          pattern: "{controller=Home}/{action=Index}/{id?}");

      app.MapRazorPages();

      app.UseRewriter(
          new RewriteOptions().Add(context =>
          {
            if (context.HttpContext.Request.Path == "/MicrosoftIdentity/Account/SignedOut") {
              context.HttpContext.Response.Redirect("/Home/Index");
            }
          })
      );

      app.Run();

    }
  }
}
