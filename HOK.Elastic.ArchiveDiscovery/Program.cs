// See https://aka.ms/new-console-template for more information
using HOK.Elastic.ArchiveDiscovery;
using HOK.Elastic.FileSystemCrawler.WebAPI.Models;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

HOK.Elastic.Logger.Log4NetLogger logger=default;
try
{
    var xml = HOK.Elastic.Logger.Log4NetProvider.Parselog4NetConfigFile("log4net.config");
    log4net.Config.XmlConfigurator.Configure(xml);
    var config = new ConfigurationBuilder()
                    .AddJsonFile("appsettings.json", false)
                    .Build();
    logger = new HOK.Elastic.Logger.Log4NetLogger("main");

    var webapiUrl = (string)config.GetValue(typeof(string), "webAPI");

    SettingsJobArgsDTO settingsJobArgsDTO = new SettingsJobArgsDTO();
    config.Bind(settingsJobArgsDTO);

    if(logger.IsEnabled(Microsoft.Extensions.Logging.LogLevel.Information))
    {       
        logger.LogDebugInfo("Startup_webapi", "appsettings.json", webapiUrl);
        logger.LogDebugInfo("Startup_SettingsJobConfig", "appsettings.json", settingsJobArgsDTO);
    }

    Worker worker = new Worker(webapiUrl);
    await worker.RunAsync(settingsJobArgsDTO);
}
catch (Exception ex)
{
    if(logger!=default && logger.IsEnabled(LogLevel.Critical))
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
