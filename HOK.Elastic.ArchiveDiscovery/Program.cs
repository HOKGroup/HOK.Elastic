// See https://aka.ms/new-console-template for more information
using HOK.Elastic.ArchiveDiscovery;
using HOK.Elastic.FileSystemCrawler.WebAPI.Models;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using NLog.Extensions.Logging;
using System.Text.RegularExpressions;

ILogger? logger = null;
ILoggerFactory _loggerFactory;
try
{
    var config = new ConfigurationBuilder()
                    .AddJsonFile("appsettings.json", false)
                    .Build();
    _loggerFactory = LoggerFactory.Create(x => x.AddNLog("nlog.config"));
    logger = _loggerFactory.CreateLogger<ILogger>();

    var webapiUrl = (string)config["webAPI"];
    var regexofficePattern = (string)config["officematchregex"];
    var dfsArchiveSuffix = (string)config["pathArchiveSuffix"];
    var dfsProdSuffix = (string)config["pathProdSuffix"];

    var settingsJobArgsDTO = System.Text.Json.JsonSerializer.Deserialize<SettingsJobArgsDTO>(File.ReadAllText("appsettings.json"));
    if (settingsJobArgsDTO == null) throw new ArgumentException("Couldn't deserialize appsettings.json into settingsjobargsdto");
    Regex officePattern = null;
    if (!string.IsNullOrWhiteSpace(regexofficePattern) )
    {
        officePattern = new Regex(regexofficePattern);
    }

    if(logger.IsEnabled(Microsoft.Extensions.Logging.LogLevel.Information))
    {       
        logger.LogInfo("Startup_webapi", "appsettings.json", webapiUrl);
        logger.LogInfo("Startup_SettingsJobConfig", "appsettings.json", settingsJobArgsDTO);
        logger.LogInfo($"Running with'{regexofficePattern}' pattern. Pathprefix = '{settingsJobArgsDTO.PublishedPath}' prod suffix = '{dfsProdSuffix}'  archive suffix = '{dfsArchiveSuffix}'");
    }

    Worker worker = new Worker(webapiUrl);
    await worker.RunAsync(
        settingsJobArgsDTO: settingsJobArgsDTO,
        pathPrefix: settingsJobArgsDTO.PublishedPath,
        pathProdSuffix: dfsProdSuffix,
        pathArchiveSuffix: dfsArchiveSuffix,
        officePattern);
}
catch (Exception ex)
{
    if(logger != null && logger.IsEnabled(LogLevel.Critical))
    {
        logger.LogErr("Fatal Exception", null, ex);
    }
    else
    {
        Console.WriteLine(ex.ToString());
    }
}
Console.WriteLine("done");
Console.ReadLine();
