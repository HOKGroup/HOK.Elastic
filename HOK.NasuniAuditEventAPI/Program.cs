using System.Diagnostics;
using System.Runtime;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication.Negotiate;
using System.Net;
using Microsoft.Extensions.Caching.Memory;
using HOK.NasuniAuditEventAPI;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using System;
using Microsoft.Extensions.Logging;
using HOK.Elastic.FileSystemCrawler.Models;
using Microsoft.OData.Edm;
using System.IO;
using Microsoft.AspNetCore.Mvc;
using NLog.Extensions.Logging;
using Microsoft.AspNetCore.OData;
using Microsoft.Extensions.Hosting;
using System.Threading;
using System.Threading.Tasks;

ILogger? logger = null;
IConfiguration Configuration;
HOK.NasuniAuditEventAPI.NasuniEventReader nasuniEventStreamReader;
bool isDebug, isInfo, isWarn, isError;

var builder = WebApplication.CreateBuilder(args);
var tuple = AppSettings.LoadSettings();
Configuration = tuple.Item1;
var appSettings = tuple.Item2;
GlobalSettings.Settings = appSettings.Settings;

builder.Services.AddLogging(options => options
.ClearProviders()
.AddNLog("nlog.config")
);

builder.Services.AddSingleton<IConfiguration>(x => Configuration);

//old configure services
var services = builder.Services;
//services.AddMvc().SetCompatibilityVersion(CompatibilityVersion.Version_3_0);
//services.AddOData();
services.AddMvc().AddMvcOptions(x => x.EnableEndpointRouting = false);
// services.Configure<AppSettings>(Configuration);
//https://andrewlock.net/running-async-tasks-on-app-startup-in-asp-net-core-3/
//services.AddHostedService<DAL.AuditLogHostedService>()
var provider = services.BuildServiceProvider();
var ilogger = provider.GetService<ILogger<HOK.NasuniAuditEventAPI.NasuniEventReader>>();
nasuniEventStreamReader = new HOK.NasuniAuditEventAPI.NasuniEventReader(ilogger, GlobalSettings.Settings.NasuniLogFolderToTail, GlobalSettings.Settings.MaxItemsToReturn, GlobalSettings.Settings.MinimumAgeOfEventInMinutes);
services.AddSingleton<HOK.NasuniAuditEventAPI.NasuniEventReader>(nasuniEventStreamReader);

//authentication support
services.AddAuthentication(NegotiateDefaults.AuthenticationScheme).AddNegotiate();
services.AddControllers().AddOData(opt =>
{
    opt.AddRouteComponents("api", GetEdmModel()).Filter().Select().Expand().Count().SetMaxTop(1000);
}
);

var app = builder.Build();
var loggerFactory =  LoggerFactory.Create(x => x.AddNLog("nlog.config"));
logger = app.Logger;// lf.CreateLogger("HOK.Program");
isDebug = logger.IsEnabled(LogLevel.Debug);
isInfo = logger.IsEnabled(LogLevel.Information);
isWarn = logger.IsEnabled(LogLevel.Warning);
isError = logger.IsEnabled(LogLevel.Error);

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    // app.UseExceptionHandler("/Home/Error");
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
    app.UseDeveloperExceptionPage();
}

var lifetime = app.Services.GetRequiredService<IHostApplicationLifetime>();

app
.UseAuthentication()
.UseRouting()
.UseAuthorization()
.UseEndpoints(endpoints =>
{
    endpoints.MapControllers();
    //about
    endpoints.MapRazorPages();
    endpoints.MapControllerRoute(name: "default", pattern: "{controller=Home}/{action=Index}/{id?}");
    //
})
.UseHttpsRedirection()
.UseMvc()
;
var appTask = app.RunAsync();
var main = nasuniEventStreamReader.Start(lifetime.ApplicationStopping);
await Task.WhenAll(appTask, main);
//end of old startup

static IEdmModel GetEdmModel()
{
    var builder = new Microsoft.OData.ModelBuilder.ODataConventionModelBuilder();
    builder.EntitySet<InputPathEventStream>("AuditEvents");
    var function = builder.Function("Peek");
    function.IncludeInServiceDocument = true;
    function.ReturnsCollectionFromEntitySet<InputPathEventStream>("AuditEvents");
    return builder.GetEdmModel();
}