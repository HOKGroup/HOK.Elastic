using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Log4Net.AspNetCore;
using HOK.Elastic.Logger;
using log4net.Config;
using Microsoft.Extensions.Logging.Configuration;
using log4net.Repository.Hierarchy;
using log4net;

namespace HOK.Elastic.FileSystemCrawler.WebAPI
{
    public class Program
    {
        public static IConfiguration Config { get; internal set; }

        public static void Main(string[] args)
        {
            CreateHostBuilder(args).Build().Run();
        }

        public static IHostBuilder CreateHostBuilder(string[] args) =>
            Host.CreateDefaultBuilder(args)
                .ConfigureWebHostDefaults(webBuilder =>
                {
                    webBuilder.UseStartup<Startup>();
                })
           
                .ConfigureLogging(logging =>
                {
                    //logging.ClearProviders();
                    logging.AddLog4Net();
                    var xml = HOK.Elastic.Logger.Log4NetProvider.Parselog4NetConfigFile("log4net.config");
                    var c = log4net.Config.XmlConfigurator.Configure(xml);
                }

                )
                .ConfigureServices(services => {
                    services.AddSingleton<IHostedJobQueue, HostedJobQueue>(x => new HostedJobQueue(x.GetService<ILogger<HostedJobQueue>>(), 2));
                    services.AddHostedService<IHostedJobQueue>(x => x.GetRequiredService<IHostedJobQueue>());
                });
         
    }
}
