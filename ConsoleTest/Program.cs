using HOK.Elastic.DAL.Models;
using HOK.Elastic.FileSystemCrawler;
using HOK.Elastic.FileSystemCrawler.Models;
using HOK.Elastic.FileSystemCrawler.WebAPI;
using log4net.Core;
using log4net.Repository.Hierarchy;
//using log4net.Core;
using Microsoft.Extensions.Logging;
using Nest;
using RtfPipe;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography.X509Certificates;
using System.Threading;
using System.Threading.Tasks;
using static MsgReader.Outlook.Storage;
using ILogger = Microsoft.Extensions.Logging.ILogger;
using Task = System.Threading.Tasks.Task;

namespace ConsoleTest
{
    internal class Program
    {

        static Task Main() => TestAsync();

        static async Task TestAsync()
        {
            //try
            //{
            //    var t = new LoopTester();
            //    await t.TestAsync();
            //}
            //catch (Exception ex)
            //{
            //    Console.WriteLine(ex.ToString());
            //}
            //Console.WriteLine("done");
            //while (Console.ReadLine() != "X")
            //{
            //    Console.WriteLine("looping for no reason");
            //}
            //await Task.Delay(5);
        }

        class LoopTester
        {
            //private static ILogger _logger;
            //private static HostedJobQueue jobscheduler;
            //private Microsoft.Extensions.Logging.ILoggerFactory _logfactory;
            //public LoopTester()
            //{
            //    _logfactory = LoggerFactory.Create(builder =>
            //    {
            //        //builder
            //        //    //.SetMinimumLevel(Microsoft.Extensions.Logging.LogLevel.Debug)             
            //        //    //.AddConsole();
            //    });
            //    _logger = _logfactory.CreateLogger<LoopTester>();
            //}
            //public async Task TestAsync()
            //{
            //    var cts = new CancellationTokenSource();
            //    HostedJobQueue hhhh = new HostedJobQueue(_logger, cts);
            //    hhhh.LoadSomeStuff(5);
            //    await hhhh.StartAsync(cts.Token);
            //    return;

            //}
        }
    }
}
