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
using Newtonsoft.Json;
using HOK.Elastic.FileSystemCrawler.WebAPI.Models;

namespace HOK.Elastic.FileSystemCrawler.WebAPI
{
    public class Program
    {
        public static IConfiguration Config { get; internal set; }
        public static AppSettings AppSettings { get; internal set; }
        public static void Main(string[] args)
        {
            var appSettingsPath = "appsettings.json";
            if (File.Exists(appSettingsPath))
            {
                var appSettings = JsonConvert.DeserializeObject<AppSettings>(File.ReadAllText(appSettingsPath));
                {
                    if(appSettings != null) { AppSettings = appSettings;}
                }
            }
           CreateHostBuilder(args).Build().Run();
        }

        public static IHostBuilder CreateHostBuilder(string[] args) =>
            Host.CreateDefaultBuilder(args)
                .ConfigureWebHostDefaults(webBuilder =>{
                    webBuilder.UseStartup<Startup>();
                })           
                .ConfigureLogging(logging =>{
                    //logging.ClearProviders();               
                    logging.AddLog4Net();
                    var xml = HOK.Elastic.Logger.Log4NetProvider.Parselog4NetConfigFile("log4net.config");                  
                    var c = log4net.Config.XmlConfigurator.Configure(xml);                  
                })

                .ConfigureServices(services => {
                    var maxJobs = AppSettings.ConcurrentJobs;// int.Parse(Program.Config["ConcurrentJobs"]);
                    services.AddSingleton<IEmailService, EmailService>(x => new EmailService(x.GetService<ILogger<HostedJobQueue>>(),
                       AppSettings.EmailSMTPhost,
                       AppSettings.EmailSMTPport,
                       AppSettings.EmailDefaultSenderSuffix
                       ));
                    services.AddSingleton<IHostedJobQueue, HostedJobQueue>(x => new HostedJobQueue(x.GetService<ILogger<HostedJobQueue>>(), x.GetService<IEmailService>(), maxJobs));
                    services.AddHostedService<IHostedJobQueue>(x => x.GetRequiredService<IHostedJobQueue>());
                    });
    }
}
