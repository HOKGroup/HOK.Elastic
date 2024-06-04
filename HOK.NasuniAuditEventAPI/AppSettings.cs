using Microsoft.Extensions.Configuration;
using System;

namespace HOK.NasuniAuditEventAPI
{

    public static class GlobalSettings
    {
        public static Settings Settings { get; set; } = new Settings();
    }
       
    public class Settings
    {
        public string NasuniLogFolderToTail { get; set; } = @"c:\NasuniLogs";
        public int MaxItemsToReturn { get; set; } = 500;
        public int MinimumAgeOfEventInMinutes { get; set; } = 120;
        public string CertificateTemplateName { get; set; }
    }

    public class AppSettings
    {
        public Settings Settings { get; set; }
        public static Tuple<IConfiguration, AppSettings> LoadSettings()
        {
            // Load settings
            IConfiguration config = new ConfigurationBuilder()
                // appsettings.json is required
                .AddJsonFile("appsettings.json", optional: false)
                // appsettings.Development.json" is optional, values override appsettings.json
                .AddJsonFile($"appsettings.Development.json", optional: true)
                // User secrets are optional, values override both JSON files
                .AddUserSecrets<Program>()
                .Build();
            // var azureAD = config.GetRequiredSection("AzureAd").Get<AzureAdSettings>() ??
            //  throw new Exception("Could not load app settings. See README for configuration instructions.");
            var settings = config.GetRequiredSection("Settings").Get<Settings>() ??
                throw new Exception("Could not load app settings. See README for configuration instructions.");
            return new Tuple<IConfiguration, AppSettings>(config, new AppSettings { Settings = settings });
        }
    }
}

