// See https://aka.ms/new-console-template for more information
using HOK.Elastic.ArchiveDiscovery;
using HOK.Elastic.DAL.Models;
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

    var workerargs = System.Text.Json.JsonSerializer.Deserialize<SettingsJobArgsDTO>(File.ReadAllText("appsettings.json"));
    if (workerargs == null) throw new ArgumentException("Couldn't deserialize appsettings.json into settingsjobargsdto");
    Regex? officePattern = null;
    if (!string.IsNullOrWhiteSpace(regexofficePattern) )
    {
        officePattern = new Regex(regexofficePattern);
    }

    if(logger.IsEnabled(Microsoft.Extensions.Logging.LogLevel.Information))
    {       
        logger.LogInfo("Startup_webapi", "appsettings.json", webapiUrl);
        logger.LogInfo("Startup_SettingsJobConfig", "appsettings.json", workerargs);
        logger.LogInfo($"Running with'{regexofficePattern}' pattern. Pathprefix = '{workerargs.PublishedPath}' prod suffix = '{dfsProdSuffix}'  archive suffix = '{dfsArchiveSuffix}'");
    }

    PathHelper.Set(workerargs.PublishedPath, workerargs.PathForCrawlingContent, workerargs.PathForCrawling);
    HOK.Elastic.DAL.Models.PathHelper.SetPathInclusion(workerargs.PathInclusionRegex);
    HOK.Elastic.DAL.Models.PathHelper.SetFileNameExclusion(workerargs.FileNameExclusionRegex);
    HOK.Elastic.DAL.Models.PathHelper.SetOfficeExtractRgx(workerargs.OfficeSiteExtractRegex);
    HOK.Elastic.DAL.Models.PathHelper.SetProjectExtractRgx(workerargs.ProjectExtractRegex);
    HOK.Elastic.DAL.Models.PathHelper.IgnoreExtensions = workerargs.IgnoreExtensions?.Distinct().ToHashSet();

    Worker worker = new Worker(webapiUrl);
    await worker.RunAsync(
        settingsJobArgsDTO: workerargs,
        pathPrefix: workerargs.PublishedPath,
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
