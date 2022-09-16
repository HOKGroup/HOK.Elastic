using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using System;
using System.Security.Cryptography.X509Certificates;


namespace HOK.NasuniAuditEventAPI
{
    public class Program
    {
        public static AppSettings Settings { get; set; }
        //https://docs.microsoft.com/en-us/aspnet/core/host-and-deploy/windows-service?view=aspnetcore-3.1&tabs=visual-studio
        public static void Main(string[] args)
        {
            //load appsettings.json file...
            var builder = new ConfigurationBuilder()
               .SetBasePath(System.IO.Directory.GetCurrentDirectory())
               .AddJsonFile("appsettings.json", optional: false)
               ;
            IConfigurationRoot configuration = builder.Build();
            Settings = new AppSettings();
            configuration.Bind(Settings);
            //build the webhost...
            CreateHostBuilder(args).Build().Run();
        }
        public static IHostBuilder CreateHostBuilder(string[] args) =>
            Host.CreateDefaultBuilder(args)
                .ConfigureWebHostDefaults(webBuilder =>
                {                    
                    webBuilder.UseIIS();
                    webBuilder.UseIISIntegration();
                    webBuilder.UseStartup<Startup>();
                    webBuilder.ConfigureAppConfiguration((hostingContext, config) =>
                    {
                        config.SetBasePath(System.IO.Directory.GetCurrentDirectory());
                        config.AddJsonFile("appsettings.json", optional: false, reloadOnChange: false);
                    });
                });

        public class AppSettings
        {
            public string NasuniLogFolderToTail { get; set; } = @"c:\NasuniLogs";
            public int MaxItemsToReturn { get; set; } = 500;
            public int MinimumAgeOfEventInMinutes { get; set; } = 120;
            public string CertificateTemplateName { get; set; }
        }
    }

}

