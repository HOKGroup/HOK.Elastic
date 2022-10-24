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

namespace HOK.Elastic.FileSystemCrawler.WebAPI
{
    public class Program
    {
        public static void Main(string[] args)
        {
            CreateHostBuilder(args).Build().Run();
        }

        public static IHostBuilder CreateHostBuilder(string[] args) =>
            Host.CreateDefaultBuilder(args)
                    .ConfigureLogging((hostingContext, logging) =>
                    {
                        var assembly = Assembly.GetAssembly(typeof(Program));
                        var pathToConfig = Path.Combine(
                                  hostingContext.HostingEnvironment.ContentRootPath
                                , "log4net", "log4net.config");
                        //var logManager = new AppLogManager(pathToConfig, assembly);
                        logging.AddLog4Net("log4net\\log4net.config");
                        //logging.AddLog4Net(new Log4NetProviderOptions
                        //{
                        //    ExternalConfigurationSetup = true
                        //});
                    })

                .ConfigureLogging(logging =>
                {
                    logging.ClearProviders();
                    logging.SetMinimumLevel(LogLevel.Trace);
                    logging.AddLog4Net("log4net\\log4net.config");
                    //var logRepository = LogManager.GetRepository(System.Reflection.Assembly.GetEntryAssembly());
                    //log4net.Config.XmlConfigurator.Configure(logRepository, new FileInfo("log4net\\log4net.config"));
                    //begin logger	
                    // logging.AddLog4Net("log4net\\log4net.config");
                    // var logRepository = logging. LogManager.GetRepository(System.Reflection.Assembly.GetEntryAssembly());
                    //XmlConfigurator.Configure(logRepository, new FileInfo("log4net\\log4net.config"));
                    //end logger
                    //
                    
                })

                .ConfigureWebHostDefaults(webBuilder =>
                {
                    webBuilder.UseStartup<Startup>();
                })
                .ConfigureServices(services => {
                    services.AddSingleton<IHostedJobQueue, HostedJobQueue>(x => new HostedJobQueue(x.GetService<ILogger<HostedJobQueue>>(), 5));
                    services.AddHostedService<IHostedJobQueue>(x => x.GetRequiredService<IHostedJobQueue>());
                });
         
    }
}
