using HOK.Elastic.FileSystemCrawler.Models;
using HOK.NasuniAuditEventAPI.DAL;
using log4net;
using log4net.Config;
//using Microsoft.AspNetCore.OData.Builder;
using Microsoft.AspNetCore.OData.Extensions;
//to support authentication:
using Microsoft.AspNetCore.Authentication.Negotiate;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.OData.Edm;
using System.IO;
using System.Linq;
using System.Threading;
using Microsoft.AspNetCore.OData;

namespace HOK.NasuniAuditEventAPI
{
    public class Startup
    {
        public Startup(IConfiguration configuration)
        {
            Configuration = configuration;
        }

        public IConfiguration Configuration { get; }
        private DAL.NasuniEventReader nasuniEventStreamReader;
        // This method gets called by the runtime. Use this method to add services to the container.
        public void ConfigureServices(IServiceCollection services)
        {
            services.AddMvc().SetCompatibilityVersion(CompatibilityVersion.Version_3_0);
           
            //services.AddOData();
            services.AddMvc().AddMvcOptions(x => x.EnableEndpointRouting = false);
            // services.Configure<AppSettings>(Configuration);
            //https://andrewlock.net/running-async-tasks-on-app-startup-in-asp-net-core-3/
            //services.AddHostedService<DAL.AuditLogHostedService>()
            var provider = services.BuildServiceProvider();
            var ilogger = provider.GetService<ILogger<DAL.NasuniEventReader>>();
            nasuniEventStreamReader = new DAL.NasuniEventReader(ilogger, Program.Settings.NasuniLogFolderToTail, Program.Settings.MaxItemsToReturn, Program.Settings.MinimumAgeOfEventInMinutes);
            services.AddSingleton<DAL.NasuniEventReader>(nasuniEventStreamReader);
            
            //authentication support
            services.AddAuthentication(NegotiateDefaults.AuthenticationScheme).AddNegotiate();
            services.AddControllers().AddOData(opt=>
            {
                opt.AddRouteComponents("api", GetEdmModel()).Filter().Select().Expand().Count().SetMaxTop(1000);
                }
            );
        }

        // This method gets called by the runtime. Use this method to configure the HTTP request pipeline.
        public void Configure(IApplicationBuilder app, IWebHostEnvironment env, ILoggerFactory loggerFactory,IHostApplicationLifetime lifetime)
        {
            if (env.IsDevelopment())
            {
                app.UseDeveloperExceptionPage();
            }

            //begin logger	
            loggerFactory.AddLog4Net("log4net\\log4net.config");
            var logRepository = LogManager.GetRepository(System.Reflection.Assembly.GetEntryAssembly());
            XmlConfigurator.Configure(logRepository, new FileInfo("log4net\\log4net.config"));
            //end logger
            nasuniEventStreamReader.Start(lifetime.ApplicationStopping);
            app.UseRouting();
            app.UseAuthentication();
            app.UseAuthorization();
            app.UseEndpoints(endpoints =>
            {
                endpoints.MapControllers();
                //about
                endpoints.MapRazorPages();
                endpoints.MapControllerRoute(name: "default", pattern: "{controller=Home}/{action=Index}/{id?}");
                //
            });

            app.UseHttpsRedirection();
            app.UseMvc();
            //app.UseMvc(routeBuilder =>
            //{
            //    routeBuilder.EnableDependencyInjection();
            //    routeBuilder.Select().Filter().OrderBy().Expand().Count().MaxTop(Program.Settings.MaxItemsToReturn);
            //    routeBuilder.MapODataServiceRoute("api", "api", GetEdmModel());
            //});
            
        }

        private static IEdmModel GetEdmModel()
        {
            var builder = new Microsoft.OData.ModelBuilder.ODataConventionModelBuilder();
            builder.EntitySet<InputPathEventStream>("AuditEvents");
            var function = builder.Function("Peek");
            function.IncludeInServiceDocument = true;
            function.ReturnsCollectionFromEntitySet<InputPathEventStream>("AuditEvents");
            return builder.GetEdmModel();
        }
    }
}
