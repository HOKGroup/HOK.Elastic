using HOK.Elastic.FileSystemCrawler.Models;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Threading.Tasks.Dataflow;
using System.Collections.Generic;
using Newtonsoft.Json;
using System.Text;
using HOK.Elastic.FileSystemCrawler.WebAPI.DAL.Models;
using HOK.Elastic.FileSystemCrawler.WebAPI.Models;

namespace HOK.Elastic.FileSystemCrawler.WebAPI
{
    public partial class HostedJobQueue : IHostedService, IHostedJobQueue
    {
        private ConcurrentDictionary<int, HostedJobInfo> _jobs = new ConcurrentDictionary<int, HostedJobInfo>();
        CancellationTokenSource _cts = new CancellationTokenSource();
        private Task _taskLoop = null;
        private ILogger _logger;
        private bool isDebug;
        private bool isInfo;
        private bool isWarn;
        private bool isError;
        private BufferBlock<HostedJobInfo> buffer;
        private TransformBlock<HostedJobInfo, HostedJobInfo> action;
        private ActionBlock<HostedJobInfo> completed;
        public event EventHandler<int> ProcessCompleted;
        private readonly string persistFile = @"logs\jobs.json";
        private readonly DateTime _startTimeUTC;
        private IEmailService _emailService;
        public TimeSpan UpTime => DateTime.UtcNow - _startTimeUTC;
        public int JobsCompleted { get => _jobsCompleted; }
        private int _jobsCompleted;

        public int MaxJobs { get; private set; }
        public int JobSlots => (int)(MaxJobs * 1.5);

        public int FreeSlots
        {
            get
            {
                var result = JobSlots - (_jobs.Values.Where(x => x.IsCompleted != true)?.Take(JobSlots).Count() ?? JobSlots);
                if (result <= JobSlots)
                {
                    return result;
                }
                else
                {
                    return JobSlots;
                }
            }
        }
        private int GetNextId()
        {
            return _jobs.Keys.Any() ? _jobs.Keys.Max() + 1 : 1;
        }

        public HostedJobQueue(ILogger<HostedJobQueue> logger, IEmailService emailService, int maxJobs)
        {
            _logger = logger;
            isDebug = _logger.IsEnabled(LogLevel.Debug);
            isInfo = _logger.IsEnabled(LogLevel.Information);
            isWarn = _logger.IsEnabled(LogLevel.Warning);
            isError = _logger.IsEnabled(LogLevel.Error);
            _emailService = emailService;
            MaxJobs = maxJobs;
            _startTimeUTC = DateTime.UtcNow;
        }

        protected virtual void OnTaskCompleted(int Id)
        {
            ProcessCompleted?.Invoke(this, Id);
        }

        public Task StartAsync(CancellationToken cancellationToken)
        {
            // Store the IHostedJobQueue task
            if (isInfo) _logger.LogInfo("Starting...");
            if (_taskLoop == null)
            {
                _taskLoop = ExecuteAsync(cancellationToken);
            }
            // If the task is completed, then return it,
            // this will bubble cancellation and failure to the caller
            if (_taskLoop.IsCompleted)
            {
                return _taskLoop;
            }
            // Otherwise it's running
            return Task.CompletedTask;
        }

        protected async Task ExecuteAsync(CancellationToken cancellationToken)
        {
            Load();
            buffer = new BufferBlock<HostedJobInfo>(new DataflowBlockOptions() { BoundedCapacity = -1, CancellationToken = cancellationToken });
            action = new TransformBlock<HostedJobInfo, HostedJobInfo>(async x => await RunJobAsync(x), new ExecutionDataflowBlockOptions() { BoundedCapacity = -1, MaxDegreeOfParallelism = MaxJobs });//TODO maxdegreeofparallelism is 1 so we don'customLogger run into conflicts with the static PathHelper class....:(
            completed = new ActionBlock<HostedJobInfo>(x => Finish(x),new ExecutionDataflowBlockOptions() {BoundedCapacity=JobSlots,MaxDegreeOfParallelism=MaxJobs});
            buffer.LinkTo(action, new DataflowLinkOptions() { PropagateCompletion = true });
            action.LinkTo(completed, new DataflowLinkOptions() { PropagateCompletion = true });
            if (isInfo) _logger.LogInfo($"ExecuteAsync Monitor Starting");
            await Task.WhenAny(MonitorAsync(cancellationToken), completed.Completion);
            if (isInfo) _logger.LogInfo($"ExecuteAsync Monitor Complete");
        }

        public async Task MonitorAsync(CancellationToken cancellationToken)
        {
#if DEBUG
            // LoadSomeRandomTestJobs(16);
#endif
            DateTime trigger = DateTime.MinValue;
            while (!cancellationToken.IsCancellationRequested)
            {
                if (DateTime.Now.Subtract(trigger) > TimeSpan.FromMinutes(5))
                {
                    trigger = DateTime.Now;
                    Save();
                    CleanupOldJobs();
                }
                if (isInfo) _logger.LogInfo($"Of {_jobs.Count} jobs, {buffer.Count} are in the buffer and {_jobs.Values.Where(x => x.IsCompleted).Count()} are complete.");
                if (buffer.Count < MaxJobs)
                {
                    var next = _jobs.Values.Where(x => x.Status == HostedJobInfo.State.unstarted).Take(MaxJobs - buffer.Count);
                    if (next.Any())
                    {
                        foreach (var x in next)
                        {
                            x.Status = HostedJobInfo.State.queued;
                            await buffer.SendAsync(x);
                        }
                    }
                }
#if DEBUG
                await Task.Delay(TimeSpan.FromSeconds(5));//monitor Loop
#else
 await Task.Delay(TimeSpan.FromSeconds(30));//monitor Loop
#endif
            }
        }




        public void CleanupOldJobs()
        {
            IEnumerable<HostedJobInfo> oldJobs;
            //remove completed jobs older than 7 days.
            oldJobs = _jobs.Values.Where(x => x.IsCompleted && x.WhenCompleted != null && x.WhenCompleted != DateTime.MinValue && x.WhenCompleted < DateTime.Now.Subtract(TimeSpan.FromDays(7)));
            foreach (var x in oldJobs)
            {
                Remove(x.Id);
            }
            //if there's more than 50 remove some more jobs.
            if (_jobs.Count > 50)
            {
                oldJobs = _jobs.Values.Where(x => x.Status >= HostedJobInfo.State.cancelled).OrderByDescending(x => x.WhenCreated).Skip(25);
                foreach (var job in oldJobs)
                {
                    Remove(job.Id);
                }
            }
        }


        public int Enqueue(SettingsJobArgsDTO settingsJobArgsDTO)
        {
            HostedJobInfo job = new HostedJobInfo(settingsJobArgsDTO, _cts.Token);
            job.Id = GetNextId();
            _jobs[job.Id] = job;
            if (isInfo) _logger.LogInformation($">>>>>>>Inserting {job.Id} : {job}");
            return job.Id;
        }

        public HostedJobInfo Get(int id)
        {
            if (_jobs.TryGetValue(id, out HostedJobInfo job))
            {
                return job;
            }
            else
            {
                throw new IndexOutOfRangeException();
            }
        }

        public IEnumerable<HostedJobInfo> Jobs
        {
            get { return _jobs.Values; }
        }

        public HostedJobInfo Remove(int id)
        {
            if (_jobs.TryRemove(id, out HostedJobInfo job))
            {
                if (!job.IsCompleted) job.Cancel();
                return job;
            }
            else
            {
                throw new IndexOutOfRangeException();
            }
        }

        //https://learn.microsoft.com/en-us/dotnet/architecture/microservices/multi-container-microservice-net-applications/background-slots-with-ihostedservice
        async Task IHostedService.StopAsync(CancellationToken cancellationToken)
        {
            // Stop called without start
            if (isInfo) _logger.LogInfo("Stopping...");
            if (_taskLoop == null)
            {
                return;
            }
            try
            {
                // Signal cancellation to the executing method  
                _cts.Cancel();
                Save();
                buffer?.Complete();
            }
            finally
            {
                // Wait until the task completes or the stop token triggers
                await Task.WhenAny(_taskLoop, Task.Delay(Timeout.Infinite, cancellationToken));
            }
        }
        #region Private Methods
        private void Save()//just a basic,best-effort to persist existing jobs (perhaps jobs that have completed but haven'customLogger been looked at etc.)
        {
            try
            {
                var json = JsonConvert.SerializeObject(_jobs.Values, Formatting.Indented, new JsonSerializerSettings() { });
                System.IO.File.WriteAllText(persistFile, json, new UTF8Encoding(false));
            }
            catch (Exception ex)
            {
                if (isError) _logger.LogErr("Error saving job", null, null, ex);
            }
        }


        private void Load()
        {
            if (System.IO.File.Exists(persistFile))
            {
                try
                {
                    string json = System.IO.File.ReadAllText(persistFile);
                    if (isDebug) _logger.LogDebug(json);
                    var jobs = JsonConvert.DeserializeObject<HostedJobInfo[]>(json);
                    if (isDebug) _logger.LogDebug($"job count = {jobs.Count()}");

                    foreach (var job in jobs.OrderBy(x => x.Id))
                    {
                        if (job.Status < HostedJobInfo.State.cancelled) job.Status = HostedJobInfo.State.unstarted;//retry completing job that started but hadn't been previously marked as cancelled, completed or otherwise.
                        if (!_jobs.TryAdd(job.Id, job))
                        {
                            if (isWarn) _logger.LogWarn($"Couldn't add jobid={job.Id} from '{persistFile}' as it already exists");
                        }
                    }
                }
                catch (Exception ex)
                {
                    if (isError) _logger.LogErr($"Error loading {persistFile}", null, null, ex);
                }
            }
        }
        public async Task<HostedJobInfo> RunJobAsync(HostedJobInfo hostedJobInfo)
        {
            try
            {
                hostedJobInfo.Status = HostedJobInfo.State.started;  
                hostedJobInfo.CompletionInfo = new CompletionInfo(hostedJobInfo.SettingsJobArgsDTO);
                var workerargs = SettingsJobArgsDTO.UnDTO(hostedJobInfo.SettingsJobArgsDTO);
                //we should add pre-flight check in indexbase or crawlerbase or something to ensure these basics are set.or refactor so we don'customLogger have shared static..
                if (string.IsNullOrEmpty(workerargs.PublishedPath) || string.IsNullOrEmpty(workerargs.PathForCrawling) || string.IsNullOrEmpty(workerargs.PathForCrawlingContent))
                {
                    throw new ArgumentException("jobsettings published path,pathforcrawling or pathforcrawlingcontent was empty");
                }
                else
                {
#if DEBUG
                    // throw new ArgumentException("just a test.");
#endif
                }
                HOK.Elastic.DAL.Models.PathHelper.Set(workerargs.PublishedPath, workerargs.PathForCrawlingContent, workerargs.PathForCrawling);
                HOK.Elastic.DAL.Models.PathHelper.SetPathInclusion(workerargs.PathInclusionRegex);
                HOK.Elastic.DAL.Models.PathHelper.SetFileNameExclusion(workerargs.FileNameExclusionRegex);
                HOK.Elastic.DAL.Models.PathHelper.SetOfficeExtractRgx(workerargs.OfficeSiteExtractRegex);
                HOK.Elastic.DAL.Models.PathHelper.SetProjectExtractRgx(workerargs.ProjectExtractRegex);
                HOK.Elastic.DAL.Models.PathHelper.IgnoreExtensions = workerargs.IgnoreExtensions?.Distinct().ToHashSet();
                HOK.Elastic.DAL.StaticIndexPrefix.Prefix = workerargs.IndexNamePrefix;
                string safepath = workerargs.JobName + workerargs.JobNotes;
                System.IO.Path.GetInvalidPathChars().Select(x => safepath = safepath.Replace(x, ' '));
                workerargs.InputPathLocation = System.IO.Path.Combine("webapijobs", safepath + hostedJobInfo.GetHashCode());
                //end of unchecked requirements stuff that causes problems.

                var customLogger = GetPerProjectLogger(hostedJobInfo.Id + workerargs.JobName, "genericsinglelogger");
                var jobLogger = new Elastic.Logger.Log4NetLogger("hmm", customLogger.Item1, customLogger.Item2);
                var index = new HOK.Elastic.DAL.Index(workerargs.ElasticIndexURI.First(), jobLogger);
                var discovery = new HOK.Elastic.DAL.Discovery(workerargs.ElasticDiscoveryURI.First(), jobLogger);
                //var jobLogger = new Elastic.Logger.Log4NetLogger($"Worker{hostedJobInfo.Id}");

                if (jobLogger.IsEnabled(LogLevel.Information))
                {
                    jobLogger.LogInformation("Constructing....");
                    jobLogger.LogInformation($"Joblocation={workerargs.InputPathLocation}");
                }
                SecurityHelper sh = new SecurityHelper(jobLogger);
                DocumentHelper dh = new DocumentHelper(true, sh, index, jobLogger);
                IWorkerBase iWorker;
                if (workerargs.CrawlMode == CrawlMode.EventBased)
                {
                    iWorker = new WorkerEventStream(index, discovery, sh, dh, jobLogger);
                }else if(workerargs.CrawlMode==CrawlMode.QueryBasedReIndex)
                {
                    iWorker = new WorkerByQuery(index,discovery,sh, dh, jobLogger);
                }
                else
                {
                    iWorker = new WorkerCrawler(index, discovery, sh, dh, jobLogger);
                }

                if (_logger.IsEnabled(LogLevel.Information)) _logger.LogInfo("Starting....", null, hostedJobInfo.SettingsJobArgsDTO.JobName);

                hostedJobInfo.CompletionInfo = await iWorker.RunAsync(workerargs, hostedJobInfo.GetCancellationToken());//we can pass IProgress<T> here later if we want to get progress.
              
                switch (hostedJobInfo.CompletionInfo.exitCode)
                {
                    case CompletionInfo.ExitCode.None:
                        hostedJobInfo.Status = HostedJobInfo.State.completedWithException;
                        break;
                    case CompletionInfo.ExitCode.OK:
                        hostedJobInfo.Status = HostedJobInfo.State.complete;
                        break;
                    case CompletionInfo.ExitCode.Cancel:
                        hostedJobInfo.Status = HostedJobInfo.State.cancelled;
                        break;
                    case CompletionInfo.ExitCode.Fatal:
                        hostedJobInfo.Status = HostedJobInfo.State.completedWithException;
                        break;
                    default:
                        break;
                }
                if (_logger.IsEnabled(LogLevel.Information)) _logger.LogInfo("Finished", null, hostedJobInfo.SettingsJobArgsDTO.JobName);
                if (jobLogger.IsEnabled(LogLevel.Information))  jobLogger.LogInfo("Completed", hostedJobInfo.SettingsJobArgsDTO.JobName, hostedJobInfo.CompletionInfo.ToString());
            }
            catch (Exception ex)
            {
                if (isError) _logger.LogError(ex, $"Running {hostedJobInfo.Id}");
                hostedJobInfo.Exception = ex;
                hostedJobInfo.Status = HostedJobInfo.State.completedWithException;
            }
            try
            {
                //try and notify if set
                var email = hostedJobInfo.SettingsJobArgsDTO.EmailNotification;
                if (!string.IsNullOrEmpty(email))
                {
                    //todo we should validate the email address before it gets here.
                    var completionInfo = JsonConvert.SerializeObject(hostedJobInfo.CompletionInfo);
                    var mail = EmailService.MakeMessage(_emailService.DefaultSender, email, $"CrawlJob Complete on {Environment.MachineName} {hostedJobInfo.SettingsJobArgsDTO.JobName}", $"{hostedJobInfo.Status}\r\n\r\n***\r\n\r\n{completionInfo}\r\n\r\nException: {(hostedJobInfo.HasException ? hostedJobInfo.GetException().ToString() : "No Fatal Exceptions...")}");
                    _emailService.Send(mail);
                }
            }
            catch (Exception ex)
            {
                if (isError) _logger.LogError(ex, $"Couldn't send email notification {hostedJobInfo.Id}");
            }
            return hostedJobInfo;
        }

        private void Finish(HostedJobInfo jobInfo)
        {
            Interlocked.Increment(ref _jobsCompleted);
            if (isInfo) _logger.LogInformation("Completed {JobInfo}", jobInfo);
            var filename = MakeSafeFileName($"completed{jobInfo.SettingsJobArgsDTO.JobName}.json");
            try
            {
                System.IO.File.AppendAllText($"logs\\{filename}", jobInfo.ToString());
            }
            catch (Exception ex)
            {
                if (isError) _logger.LogError($"Couldn't save logs\\{filename} with {jobInfo.ToString()}", ex);
            }
            OnTaskCompleted(jobInfo.Id);
        }
        private char[] _badChars = System.IO.Path.GetInvalidFileNameChars();
        private string MakeSafeFileName(string filename)
        {
            return new string(filename.Where(x => !_badChars.Contains(x)).ToArray());
        }


        private Tuple<log4net.Repository.ILoggerRepository, log4net.ILog> GetPerProjectLogger(string projectName, string name)
        {
            //temporary until we refactor logging...
            //https://stackoverflow.com/questions/21022467/log4net-different-logs-on-different-file-appenders-at-runtime
            var repositoryName = projectName;
            log4net.Repository.ILoggerRepository repository = null;
            var repositories = log4net.LogManager.GetAllRepositories();
            foreach (var loggerRepository in repositories)
            {
                if (loggerRepository.Name.Equals(repositoryName))
                {
                    repository = loggerRepository;
                    break;
                }
            }

            log4net.Repository.Hierarchy.Hierarchy hierarchy = null;
            if (repository == null)
            {
                //Create a new repository
                repository = log4net.LogManager.CreateRepository(repositoryName);

                hierarchy = (log4net.Repository.Hierarchy.Hierarchy)repository;
                hierarchy.Root.Additivity = false;

                var rollingFileAppenderLayout = new log4net.Layout.PatternLayout("%date{HH:mm:ss,fff}|T%2thread|%25.25logger|%5.5level| %message%newline");
                rollingFileAppenderLayout.ActivateOptions();
                var rollingFileAppender = new log4net.Appender.RollingFileAppender();
                rollingFileAppender.Name = $"job{projectName}";
                rollingFileAppender.Threshold = log4net.Core.Level.Info;
                rollingFileAppender.CountDirection = 0;
                rollingFileAppender.PreserveLogFileNameExtension = true;
                rollingFileAppender.AppendToFile = true;
                rollingFileAppender.LockingModel = new log4net.Appender.FileAppender.MinimalLock();
                rollingFileAppender.StaticLogFileName = true;
                rollingFileAppender.MaxFileSize = 10 * 1024 * 1024;
                rollingFileAppender.MaxSizeRollBackups = 20;
                rollingFileAppender.RollingStyle = log4net.Appender.RollingFileAppender.RollingMode.Composite;
                rollingFileAppender.DatePattern = ".yyyy-MM-dd'.log'";
                rollingFileAppender.Layout = rollingFileAppenderLayout;
                rollingFileAppender.File = string.Format("{0}{1}{2}", "logs\\job", projectName, ".log");
                rollingFileAppender.ActivateOptions();
                hierarchy.Root.AddAppender(rollingFileAppender);
                log4net.Config.BasicConfigurator.Configure(repository);
            }
            return new Tuple<log4net.Repository.ILoggerRepository, log4net.ILog>(repository, log4net.LogManager.GetLogger(repositoryName, name));
        }
        #endregion

#if DEBUG
        public void LoadSomeRandomTestJobs(int itemsToCreate)
        {
            //load up some random tasks
            Random random = new Random();
            for (int i = 0; i < itemsToCreate; i++)
            {
                var rnd = random.Next(50, 500);
                var d = new SettingsJobArgsDTO()
                {
                    BulkUploadSize = rnd,
                    CrawlMode = HOK.Elastic.FileSystemCrawler.Models.CrawlMode.EventBased,
                    ElasticDiscoveryURI = new List<string> { "https://elasitcnode:9200/" },
                    ElasticIndexURI = new List<string> { "https://elasticnode:9200/" },
                    DocInsertionThreads = 1,
                    JobName = "Sample" + rnd,
                    JobNotes = "Some notes about the sample job",
                    IgnoreExtensions = new List<string>() { ".dat", ".db" },
                    IndexNamePrefix = $"test{rnd}",
                    InputPathLocation = $"c:\\",
                    InputEvents = new List<InputPathEventStream>() {new InputPathEventStream(){
                        IsDir = true,
                        Path = "c:\\archive",
                        PathFrom = "c:\\production",
                        PresenceAction = ActionPresence.Copy
                            }
                    },
                    PublishedPath = "c:\\",
                    PathForCrawling = "c:\\",
                    PathForCrawlingContent = "c:\\",
                    PathInclusionRegex = ".*",
                    FileNameExclusionRegex = ".*",
                    OfficeSiteExtractRegex = "[a-z]{2,5}",
                    ProjectExtractRegex = "\\d\\\\(([\\d|\\-|\\.]*\\d\\d)\\s?([\\+|\\s\\|\\-|_]+)([\\w]+[^\\\\|$|\\r|\\n]*))",
                    EmailNotification = "james.blackadar@hok.com"
                };
                Enqueue(d);
            }
        }
#endif

    }
}
