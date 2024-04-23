using log4net.Config;
using log4net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.HttpsPolicy;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Server.IIS;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Options;

namespace HOK.Elastic.FileSystemCrawler.WebAPI
{
    public class Startup
    {
        public Startup(IConfiguration configuration)
        {
            Configuration = configuration;
            Program.Config = configuration;
        }

        public IConfiguration Configuration { get; }

        // This method gets called by the runtime. Use this method to add services to the container.
        public void ConfigureServices(IServiceCollection services)
        {
            //services.AddControllersWithViews();
            services.AddAuthentication(IISServerDefaults.AuthenticationScheme);
            services.AddAuthorization(o =>
            {
                o.FallbackPolicy = new AuthorizationPolicyBuilder().RequireAssertion(x => AccessPolicy.GetFallBack(x)).Build();// By default, all incoming requests will be authorized according to the fallback policy if it hasn't been specified for an endpoint. We want this restrictive.
               
                foreach (var ourAccessPolicies in AccessPolicy.Policies)
                {
                    o.AddPolicy(ourAccessPolicies.Key, ourAccessPolicies.Value);
                }
            }
            );
            services.AddMvc();       
        }

        // This method gets called by the runtime. Use this method to configure the HTTP request pipeline.
        public void Configure(IApplicationBuilder app, IWebHostEnvironment env, ILoggerFactory loggerFactory)
        {
            if (env.IsDevelopment())
            {
                app.UseDeveloperExceptionPage();
            }
            else
            {
                app.UseExceptionHandler("/Home/Error");
                // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
                app.UseHsts();
            }

            app.UseForwardedHeaders(new ForwardedHeadersOptions
            {
                ForwardedHeaders = ForwardedHeaders.XForwardedFor |
    ForwardedHeaders.XForwardedProto
            });

            app.UseHttpsRedirection();
            app.UseStaticFiles();

            app.UseRouting();
            app.UseAuthorization();
            app.UseAuthentication();

            app.UseEndpoints(endpoints =>
            {
                endpoints.MapControllerRoute(
                    name: "default",
                    pattern: "{controller=Home}/{action=Index}/{id?}");
            });    
        }
    }
}
