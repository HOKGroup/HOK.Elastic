using HOK.Elastic.DAL.Models;
using HOK.Elastic.FileSystemCrawler.Models;
using HOK.Elastic.Logger;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace HOK.Elastic.FileSystemCrawler.ConsoleProgram
{
    partial class Program
    {
        private static HOK.Elastic.Logger.Log4NetLogger _il;
        private static CancellationTokenSource _ct = new CancellationTokenSource();
        public static SettingsApp AppSettings { get; private set; }
        private static bool ildebug, ilinfo, ilwarn, ilerror, ilfatal;


        //Main Entry Point
        static async Task<int> Main(string[] args)
        {
            ///not sure if this is actually needed here or in the msgreader library but it's all working currently...
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            int exitcode = 1;
            var jobDirectoryInfo1 = new DirectoryInfo(args.First());
            bool runningInteractively = args.Length == 1;

            //only allow passing one argument for now which is the path to a job folder. The job folder will have the jobsettings.json file and the logs for that job.
            //something like c:\hok.elastic.filesystemcrawler\jobs\USFull
            //logs would be stored in:
            //c:\hok.elastic.filesystemcrawler\jobs\USFull\logs
            try
            {
                if (args.Length == 1 || args.Length == 2)
                {
                    //start building the workerarguments
                    //load appsettings.json file...
                    var jobDirectoryInfo = new DirectoryInfo(args.First());
                    var builder = new ConfigurationBuilder()
                       .SetBasePath(System.IO.Directory.GetCurrentDirectory())
                       .AddJsonFile("appsettings.json", optional: false)
                       .AddJsonFile(Path.Combine(jobDirectoryInfo.FullName, "jobSettings.json"));
                    ;
                    IConfigurationRoot configuration = builder.Build();
                    AppSettings = new SettingsApp();
                    configuration.Bind(AppSettings);
                    SettingsJob jobSettings = new SettingsJob();
                    configuration.Bind(jobSettings);

                    #region ManageLogFiles
                    var logfilepath = Path.Combine(jobDirectoryInfo.FullName, "logs");
                    var configFilePath = "log4net\\log4net.config";
                    ConfigFileHelper.ChangeLog4netOutputpaths(logfilepath, new FileInfo(configFilePath));
#if DEBUG
                    ConfigFileHelper.MakeJsonSchemaFileForAppSettings();
#endif
                    _il = new Logger.Log4NetLogger($"{jobDirectoryInfo.Name}.ConsoleProgram", Logger.Log4NetProvider.Parselog4NetConfigFile(configFilePath));
                    ildebug = _il != null && _il.IsEnabled(Microsoft.Extensions.Logging.LogLevel.Debug);
                    ilinfo = _il != null && _il.IsEnabled(Microsoft.Extensions.Logging.LogLevel.Information);
                    ilwarn = _il != null && _il.IsEnabled(Microsoft.Extensions.Logging.LogLevel.Warning);
                    ilerror = _il != null && _il.IsEnabled(Microsoft.Extensions.Logging.LogLevel.Error);
                    ilfatal = _il != null && _il.IsEnabled(Microsoft.Extensions.Logging.LogLevel.Critical);
                    using (var loggerLifecycle = new Logger.LifecycleManagement(_il))
                    {
                        loggerLifecycle.Purge(Path.Combine(logfilepath), DateTime.Now.Subtract(TimeSpan.FromDays(15)), 15);
                    }
                    #region PopulateWorkerArgs
                    var workerargs = new SettingsJobArgs()
                    {
                        DocInsertionThreads = 1,
                        CPUCoreThreadMultiplier = jobSettings.CPUCoreThreadMultiplier ?? AppSettings.CPUCoreThreadMultiplier,
                        ReadContentSizeLimitMB = jobSettings.ReadContentSizeLimitMB ?? AppSettings.ReadContentSizeLimitMB,//todo change these to external settings from jobsettings or maybe appsettings is better.
                        ElasticDiscoveryURI = jobSettings.ElasticDiscoveryURI ?? AppSettings.ElasticDiscoveryURI,//.Select(x => new Uri(x));
                        ElasticIndexURI = jobSettings.ElasticIndexURI ?? AppSettings.ElasticIndexURI,//.Select(x => new Uri(x));
                        BulkUploadSize = jobSettings.BulkUploadSize ?? AppSettings.BulkUploadSize,//todo change to 200 or other bigger number
                        IndexNamePrefix = jobSettings.IndexNamePrefix ?? AppSettings.IndexNamePrefix,
                        FileNameExclusionRegex = jobSettings.FileNameExclusionRegex ?? AppSettings.FileNameExclusionRegex,
                        PathInclusionRegex = jobSettings.PathInclusionRegex??AppSettings.PathInclusionRegex,
                        IgnoreExtensions = jobSettings.IgnoreExtensions??AppSettings.IgnoreExtensions,
                        OfficeSiteExtractRegex = jobSettings.OfficeSiteExtractRegex??AppSettings.OfficeSiteExtractRegex,
                        ProjectExtractRegex = jobSettings.ProjectExtractRegex??AppSettings.ProjectExtractRegex,
                        PipeCategorizationRegex = jobSettings.PipeCategorizationRegex??AppSettings.PipeCategorizationRegex,
                        CrawlMode = jobSettings.CrawlMode,
                        ReadFileContents = jobSettings.ReadFileContents,
                        InputPathLocation = jobDirectoryInfo.FullName,
                        PathForCrawling = jobSettings.PathForCrawling,
                        PathForCrawlingContent = jobSettings.PathForCrawlingContent,
                        PublishedPath = jobSettings.PublishedPath,
                        FileSystemEventsAPI = AppSettings.FileSystemEventsAPI,
                        //JsonQueryString = load from jsonloader.
                        ExceptionsPerTenMinuteIntervalLimit= AppSettings.ExceptionsPerTenMinuteIntervalLimit??jobSettings.ExceptionsPerTenMinuteIntervalLimit
                    };
                    
                    PathHelper.Set(workerargs.PublishedPath, workerargs.PathForCrawlingContent, workerargs.PathForCrawling);
                    HOK.Elastic.DAL.Models.PathHelper.SetPathInclusion(workerargs.PathInclusionRegex);
                    HOK.Elastic.DAL.Models.PathHelper.SetFileNameExclusion(workerargs.FileNameExclusionRegex);
                    HOK.Elastic.DAL.Models.PathHelper.SetOfficeExtractRgx(workerargs.OfficeSiteExtractRegex);
                    HOK.Elastic.DAL.Models.PathHelper.SetProjectExtractRgx(workerargs.ProjectExtractRegex);
                    HOK.Elastic.DAL.Models.PathHelper.IgnoreExtensions = workerargs.IgnoreExtensions?.Distinct().ToHashSet();
                    
                    if (workerargs.CrawlMode == CrawlMode.Incremental || workerargs.CrawlMode == CrawlMode.Full)
                    {
                        var inputPathWorkerCollection = new InputPathCollectionCrawl(jobSettings.InputPaths);
                        //var inputPathWorkerCollection = jobSettings.InputPaths as InputPathCollectionCrawl;// new InputPathCollectionCrawl(jobSettings.InputPaths);
                        if (InputPathLoader.HasUnfinishedPaths(jobDirectoryInfo.FullName))
                        {
                            InputPathLoader.LoadUnfinishedPaths(jobDirectoryInfo.FullName, ref inputPathWorkerCollection);
                        }
                        workerargs.InputPaths = inputPathWorkerCollection;
                    }
                    else
                    {
                        workerargs.InputPaths = jobSettings.InputPaths;
                    }

                    #endregion
                    //Interactive jobnotes
                    if (args.Length == 1)
                    {
                        workerargs.RunningInteractively = runningInteractively;
                        Console.WriteLine("Please input some job notes and press {Enter}");
                        Console.CancelKeyPress += new ConsoleCancelEventHandler(CancellationHandler);
                        workerargs.JobNotes = Console.ReadLine();
                    }
                    else
                    {
                        workerargs.RunningInteractively = runningInteractively;
                        workerargs.JobNotes = args.ElementAt(1);
                    }
                    #endregion
                    if (workerargs.CrawlMode == CrawlMode.Incremental || workerargs.CrawlMode == CrawlMode.Full) //for event based or find missing content...maybe we don't want to cancel if we are getting 1000's of errors (but we will want to be notified by log4net)
                    {                      
                        ExceptionRateLimiter.ThresholdCount = workerargs.ExceptionsPerTenMinuteIntervalLimit ??10;
                        ExceptionRateLimiter.ThresholdTime = TimeSpan.FromMinutes(10);
                        ExceptionRateLimiter.ThresholdReached += ExceptionThresholdReachedEventOccured;
                    }
                    else if (workerargs.CrawlMode == CrawlMode.QueryBasedReIndex)
                    {
                        try
                        {
                            workerargs.JsonQueryString = JsonQueryLoader.LoadMissingContentQuery(workerargs.InputPathLocation);
                        }
                        catch
                        {
                            throw new JsonException("MissingContentQuery.json is not valid JSON");
                        }
                    }

                    #region DoWork
                    exitcode = await Start(workerargs, configFilePath);
                    #endregion
                }
                else
                {
                    throw new ArgumentException($"Incorrect number of arguments passed to the application; instead, received {args.Length} arguments.\r\n\r\n " +
                        $"Argument format is {{Path to executable}} {{job folder}} {{notes}}\r\n" +
                        $"Example: {System.Reflection.Assembly.GetExecutingAssembly().CodeBase} \"d:\\elastic crawl job definitions\\europe\"\r\n" +
                        $"Example: {System.Reflection.Assembly.GetExecutingAssembly().CodeBase} .\\myjob\r\n" +
                        $"Example: {System.Reflection.Assembly.GetExecutingAssembly().CodeBase} myjob \"my notes about the job\""
                        );
                }
            }
            catch (Exception ex)
            {
                //todo we should ensure this gets written out somewhere in case the logger never got setup and this is running headless
                if (ilerror) _il.LogErr("program.main", null, null, ex);
                if (runningInteractively)
                {
                    Console.WriteLine(ex.ToString());
                    Console.ReadLine();
                }
            }
            return exitcode;
        }

        static async Task<int> Start(ISettingsJobArgs workerargs, string configFilePath)
        {
            CompletionInfo completionInfo = null;
            DAL.StaticIndexPrefix.Prefix = workerargs.IndexNamePrefix;
            if (ilinfo)
            {
                _il.LogInfo("Read config file from:", configFilePath);
                _il.LogInfo("Assembly Informational Version", "N/a", FileVersionInfo.GetVersionInfo(Assembly.GetExecutingAssembly().Location).ProductVersion);
                _il.LogInfo("Verify Starting Arguments", "N/a", workerargs);
            }
            if (workerargs.RunningInteractively)
            {
                Console.WriteLine(">\r\n>\r\n>");
                Console.WriteLine("Is this OK? click close to cancel if not and edit the defaults.json file");
                Console.ReadLine();
            }

            // TODO: Retry Discovery a few times if connection failed
            var discovery = new DAL.Discovery(workerargs.ElasticDiscoveryURI.First(), new Log4NetLogger($"{workerargs.JobName}.Discovery"));
            var index = new DAL.Index(workerargs.ElasticIndexURI.First(), new Log4NetLogger($"{workerargs.JobName}.Index"));


            var securityHelper = new SecurityHelper(new Log4NetLogger($"{workerargs.JobName}.SecurityHelper"));
            var documentHelper = new DocumentHelper(workerargs.ReadFileContents ?? false, securityHelper, index, new Log4NetLogger($"{workerargs.JobName}.DocumentHelper"));
            try
            {
                using (var initializationIndex = new DAL.InitializationIndex(workerargs.ElasticIndexURI.First(), new Logger.Log4NetLogger($"{workerargs.JobName}.Setup")))
                {
                    initializationIndex.Prefix = Program.AppSettings.IndexNamePrefix;
#if DEBUG
                    //initializationIndex.PromptToDelete();
#endif
                    if (initializationIndex.PreFlightFail())
                    {
                        if (!workerargs.RunningInteractively)
                        {
                            if (ilerror) _il.LogErr("Indicies Check failed...please relaunch this application interactively to setup indicies.");
                            return 1;
                        }
                        else
                        {
                            initializationIndex.Put(workerargs.RunningInteractively);
                        }
                    }
                }
                using (var initializationPipeline = new DAL.InitializationPipeline(workerargs.ElasticIndexURI.First(), new Logger.Log4NetLogger($"{workerargs.JobName}.Setup")))
                {
                    initializationPipeline.PIPECategorizationProjectExtractRgx = workerargs.PipeCategorizationRegex;
                    initializationPipeline.Prefix = Program.AppSettings.IndexNamePrefix;
                    if (!initializationPipeline.CheckForPipeLines())
                    {
                        initializationPipeline.Put(true);
                    }
                    else
                    {
#if DEBUG
                        //initializationPipeline.PromptToDelete();
#endif
                    }
                }

                if (workerargs.CrawlMode == CrawlMode.EventBased)
                {
                    //we will eventually need to check that all the indicies and pipelines are setup
                    WorkerEventStream worker = new WorkerEventStream(index, discovery, securityHelper, documentHelper, new Logger.Log4NetLogger($"{workerargs.JobName}.WorkerEvents"));
                    var fileSystemEventsAPI = Program.AppSettings.FileSystemEventsAPI;
                    EventStreamClient eventStreamClient = new EventStreamClient(fileSystemEventsAPI, new Logger.Log4NetLogger($"{workerargs.JobName}.EventStreamClient"));
                    completionInfo = await eventStreamClient.ProcessEventsAsync(workerargs, worker, _ct.Token);
                }
                else if (workerargs.CrawlMode == CrawlMode.Full || workerargs.CrawlMode == CrawlMode.Incremental)
                {
                    WorkerCrawler worker = new WorkerCrawler(index, discovery, securityHelper, documentHelper, new Logger.Log4NetLogger($"{workerargs.JobName}.WorkerCrawler"));
                    completionInfo = await worker.RunAsync(workerargs, _ct.Token);
                }
                else if (workerargs.CrawlMode == CrawlMode.FindMissingContent || workerargs.CrawlMode == CrawlMode.EmailOnlyMissingContent || workerargs.CrawlMode == CrawlMode.QueryBasedReIndex)
                {
                    if (workerargs.CrawlMode == CrawlMode.FindMissingContent || workerargs.CrawlMode == CrawlMode.EmailOnlyMissingContent)
                    {
                        if (!workerargs.ReadFileContents ?? false)
                        {
                            if (ilwarn) _il.LogWarn("CrawlMetadata was set to true but shouldn't be when crawling for missing content...Changing to false.");
                            workerargs.ReadFileContents = true;
                        }
                    }
                    WorkerByQuery worker = new WorkerByQuery(index, discovery, securityHelper, documentHelper, new Logger.Log4NetLogger($"{workerargs.JobName}.WorkerByQuery"));
                    completionInfo = await worker.RunAsync(workerargs, _ct.Token);
                }
            }
            catch (Exception ex)
            {
          
                if (_il.IsEnabled(LogLevel.Error)) _il.LogErr("Error in program.main", "", null, ex);
            }
            finally
            {
                index.Dispose();
                discovery.Dispose();
                if (ilinfo) _il.LogInfo("Complete!", null, completionInfo);
            }

            if (workerargs.RunningInteractively)
            {
                Console.WriteLine("Done crawl. Press {Enter} to exit.");
                Console.ReadLine();
            }
            return 0;
        }

        static void ExceptionThresholdReachedEventOccured(object sender, EventArgs ea)
        {
            if (!_ct.IsCancellationRequested)
            {
                _ct.Cancel();
                if (_il.IsEnabled(LogLevel.Critical))
                {
                    _il.LogFatal($"Error Threshold Event was triggered...cancelling limit reached  {AppSettings.ExceptionsPerTenMinuteIntervalLimit}/10 minutes", "", null);
                }
            }
            else if (ildebug)
            {
                _il.LogDebugInfo("Cancellation already requested.");
            }
        }

        protected static void CancellationHandler(object sender, ConsoleCancelEventArgs args)
        {
            args.Cancel = true;
            _ct.Cancel();
        }
    }
}