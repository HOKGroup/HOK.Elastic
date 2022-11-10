// See https://aka.ms/new-console-template for more information
using HOK.Elastic.ArchiveDiscovery;
using HOK.Elastic.FileSystemCrawler.WebAPI.Models;
using Microsoft.Extensions.Configuration;

Console.WriteLine("Hello, World!");
try
{
    var xml = HOK.Elastic.Logger.Log4NetProvider.Parselog4NetConfigFile("log4net.config");
    log4net.Config.XmlConfigurator.Configure(xml);

    var config = new ConfigurationBuilder()
                    .AddJsonFile("appsettings.json", false)
                    .Build();

    var webapi = (string)config.GetValue(typeof(string), "webAPI");

    SettingsJobArgsDTO settingsJobArgsDTO = new SettingsJobArgsDTO();
    config.Bind(settingsJobArgsDTO);
    //Worker worker = new Worker("https://localhost:44346");
    Worker worker = new Worker(webapi);
    await worker.WorkAsync(settingsJobArgsDTO);
}
catch (Exception ex)
{
    Console.WriteLine(ex.ToString());
}
Console.WriteLine("done");
Console.ReadLine();
