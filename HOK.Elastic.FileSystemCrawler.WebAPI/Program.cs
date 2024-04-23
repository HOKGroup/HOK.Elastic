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
using System.Text;

namespace HOK.Elastic.FileSystemCrawler.WebAPI
{
//todo
//add 'remove' buttons for input paths (maybe a preview so you can see what's in there too)
//fatal exception in crawl didn't seem to populate the 'exceptions' section of the results, but the completion status was 'completed with exceptions'
//add 'logs' button to web page to view job logs...
    public class Program
    {
        public static string AppVersion
        {
            get
            {
                StringBuilder sb = new StringBuilder();
                sb.AppendLine("Runtime Version: " + System.Environment.Version.ToString());
                sb.AppendLine("Program Version: " + typeof(Program).Assembly
    .GetCustomAttribute<AssemblyFileVersionAttribute>().Version);
                var appdir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
                var di = new DirectoryInfo(appdir);
                foreach (var fi in di.EnumerateFiles("HOK*.dll"))
                {
                    sb.AppendLine($"{fi.LastWriteTime.ToString("yyyy-MM-dd HH:mm:ss")} {fi.Name} ");
                }
                return sb.ToString();
            }
        }
        public static IConfiguration Config { get; internal set; }
        public static AppSettings AppSettings { get; internal set; }
        public static void Main(string[] args)
        {
            System.Text.Encoding.RegisterProvider(System.Text.CodePagesEncodingProvider.Instance);//for msgreader
            var appSettingsPath = "appsettings.json";
            if (File.Exists(appSettingsPath))
            {
                var appSettings = JsonConvert.DeserializeObject<AppSettings>(File.ReadAllText(appSettingsPath));
                {
                    if (appSettings != null) { AppSettings = appSettings; }
                }
            }
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
                })
                .ConfigureServices(services =>
                {
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
