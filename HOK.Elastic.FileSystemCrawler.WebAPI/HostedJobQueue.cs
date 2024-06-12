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
using HOK.Elastic.FileSystemCrawler.WebAPI.Models;
using HOK.Elastic.DAL;
using System.IO;
using NLog.Extensions.Logging;
using NLog.Config;
using System.Diagnostics;

namespace HOK.Elastic.FileSystemCrawler.WebAPI
{
    public partial class HostedJobQueue : IHostedService, IHostedJobQueue
    {
        private ConcurrentDictionary<int, HostedJobInfo> _jobs = new ConcurrentDictionary<int, HostedJobInfo>();
        CancellationTokenSource _cts = new CancellationTokenSource();
        private Task _taskLoop = null;
        private ILogger _logger;
        private bool isDebug, isInfo,isWarn,isError;
        private BufferBlock<HostedJobInfo> buffer;
        private TransformBlock<HostedJobInfo, HostedJobInfo> action;
        private ActionBlock<HostedJobInfo> completed;
        public event EventHandler<int> ProcessCompleted;
        private string _persistFile = null;
        private string _jobsFolder = null;
        private string _logsFolder = null; 
        private readonly DateTime _startTimeUTC;
        private IEmailService _emailService;

        private int _jobsCompleted;
        public TimeSpan UpTime => DateTime.UtcNow - _startTimeUTC;
        public int JobsCompleted { get => _jobsCompleted; }

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
            var appFolder = System.IO.Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location);
            _logsFolder = System.IO.Path.Combine(appFolder, "logs");
            Directory.CreateDirectory(_logsFolder);
            _persistFile = Path.Combine(_logsFolder, "jobs.json");
            _jobsFolder = Path.Combine(appFolder,"webapijobs");
            Directory.CreateDirectory(_jobsFolder);
        }

        protected virtual void OnTaskCompleted(int Id)
        {
            ProcessCompleted?.Invoke(this, Id);
        }

        public Task StartAsync(CancellationToken cancellationToken)
        {
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
            //if (Jobs.Count() < 20)
            //{
            //    LoadSomeRandomTestJobs(3);
            //}

#endif
            DateTime trigger = DateTime.MinValue;
            while (!cancellationToken.IsCancellationRequested)
            {
                if (DateTime.Now.Subtract(trigger) > TimeSpan.FromMinutes(5))
                {
                    if (isDebug) _logger.LogDebug($"Of {_jobs.Count} jobs, {buffer.Count} are in the buffer and {_jobs.Values.Where(x => x.IsCompleted).Count()} are complete.");
                    trigger = DateTime.Now;
                    Save();
                    CleanupOldJobs();
                }
                
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
            int leaveSomeOldJobs =10;
            IEnumerable<HostedJobInfo> oldJobs;
            //remove completed jobs older than 7 days.
            oldJobs = _jobs.Values.Where(x => x.IsCompleted && x.WhenCompleted != null && x.WhenCompleted != DateTime.MinValue && x.WhenCompleted < DateTime.Now.Subtract(TimeSpan.FromDays(7))).ToList();
            RemoveAndCleanupJobs(oldJobs);  
            //if there's more than {leaveSomeOldJobs} remove some more jobs.
            if (_jobs.Count > leaveSomeOldJobs)
            {
                oldJobs = _jobs.Values.Where(x => x.Status >= HostedJobInfo.State.cancelled).OrderByDescending(x => x.WhenCreated).Skip(leaveSomeOldJobs).ToList();
                RemoveAndCleanupJobs(oldJobs);                
            }
        }

        public void RemoveAndCleanupJobs(IEnumerable<HostedJobInfo> oldJobs)
        {
            foreach (var job in oldJobs)
            {
                try
                {
                    CleanupOldFolder(job);
                    Remove(job.Id);
                }
                catch (Exception ex)
                {
                    if (isError) _logger.LogError(ex,"Error removing" + job.Id  +  "(" + job.SettingsJobArgsDTO.JobName + ")");
                }
            }
        }

        public void CleanupOldFolder(HostedJobInfo job)
        {
            var oldJobPath = SettingsJobArgsDTO.UnDTO(job.SettingsJobArgsDTO).InputPathLocation;
            if(oldJobPath != null && Directory.Exists(oldJobPath))
            {
                try
                {
                    if(Directory.Exists(oldJobPath))
                    {
                        Directory.Delete(oldJobPath, true);
                    }
                   
                }catch (Exception ex)
                {
                    if (isWarn) _logger.LogWarning(ex, "Couldn't delete old job at '{0}'",oldJobPath);
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
                Save();
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
                System.IO.File.WriteAllText(_persistFile, json, new UTF8Encoding(false));
            }
            catch (Exception ex)
            {
                if (isError) _logger.LogErr("Error saving job", null, null, ex);
            }
        }


        private void Load()
        {
            if (System.IO.File.Exists(_persistFile))
            {
                try
                {
                    string json = System.IO.File.ReadAllText(_persistFile);
                    if (isDebug) _logger.LogDebug(json);
                    var jobs = JsonConvert.DeserializeObject<HostedJobInfo[]>(json);
                    if (isDebug) _logger.LogDebug($"job count = {jobs.Count()}");

                    foreach (var job in jobs.OrderBy(x => x.Id))
                    {
                        if (job.Status < HostedJobInfo.State.cancelled) job.Status = HostedJobInfo.State.unstarted;//retry completing job that started but hadn't been previously marked as cancelled, completed or otherwise.
                        if (!_jobs.TryAdd(job.Id, job))
                        {
                            if (isWarn) _logger.LogWarn($"Couldn't add jobid={job.Id} from '{_persistFile}' as it already exists");
                        }
                    }
                }
                catch (Exception ex)
                {
                    if (isError) _logger.LogErr($"Error loading {_persistFile}", null, null, ex);
                }
            }
        }
        public async Task<HostedJobInfo> RunJobAsync(HostedJobInfo hostedJobInfo)
        {
            Tuple<NLog.Targets.Target, LoggingRule, ILogger> jobLogConfig = null;
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
                string safepath = workerargs.JobName + workerargs.JobNotes;
                System.IO.Path.GetInvalidFileNameChars().Select(x => safepath = safepath.Replace(x, ' '));
                workerargs.InputPathLocation = System.IO.Path.Combine(_jobsFolder, safepath + hostedJobInfo.GetHashCode());
                hostedJobInfo.SettingsJobArgsDTO.InputPathLocation = workerargs.InputPathLocation;//TODO refactor inputpathcrawls and events.
                Directory.CreateDirectory(workerargs.InputPathLocation);//CreateFolder if it doesn't exist.
                //end of unchecked requirements stuff that causes problems.
           
                var jobLoggerPath = Path.Combine(workerargs.InputPathLocation,"joblog.log");
                jobLogConfig = GetJobLogConfig("WebAPI" + hostedJobInfo.Id + workerargs.JobName,jobLoggerPath);
                var jobLogger = jobLogConfig.Item3;
                if (jobLogger.IsEnabled(LogLevel.Information))
                {
                    jobLogger.LogInformation("Constructing....");
                    jobLogger.LogInformation($"Joblocation={workerargs.InputPathLocation}");
                }
                IndexNameHelper indexNameHelper = new IndexNameHelper(workerargs.IndexNamePrefix);
                PipeLineNameHelper pipeLineNameHelper = new PipeLineNameHelper(workerargs.IndexNamePrefix);
                var index = new HOK.Elastic.DAL.Index(pipeLineNameHelper, indexNameHelper,workerargs.ElasticIndexURI.First(), jobLogger);
                var discovery = new HOK.Elastic.DAL.Discovery(pipeLineNameHelper, indexNameHelper,workerargs.ElasticDiscoveryURI.First(), jobLogger);
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

                hostedJobInfo.CompletionInfo = await iWorker.RunAsync(workerargs, hostedJobInfo.GetCancellationToken());//TODO we can pass IProgress<T> here later if we want to get progress.
              
                switch (hostedJobInfo.CompletionInfo.exitCode)
                {
                    case CompletionInfo.ExitCode.None:
                        hostedJobInfo.Status = HostedJobInfo.State.completedWithException;
                        hostedJobInfo.Exception = hostedJobInfo.CompletionInfo.LastException;
                        break;
                    case CompletionInfo.ExitCode.OK:
                        hostedJobInfo.Status = HostedJobInfo.State.complete;
                        break;
                    case CompletionInfo.ExitCode.Cancel:
                        hostedJobInfo.Status = HostedJobInfo.State.cancelled;
                        break;
                    case CompletionInfo.ExitCode.Fatal:
                        hostedJobInfo.Status = HostedJobInfo.State.completedWithException;
                        hostedJobInfo.Exception = hostedJobInfo.CompletionInfo.LastException;
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
            finally
            {
                if (jobLogConfig != null) RemoveNlogJobLogger(jobLogConfig);
            }
            string email= hostedJobInfo.SettingsJobArgsDTO.EmailNotification;
            try
            {
                //try and notify if set
                if (!string.IsNullOrEmpty(email))
                {
                    var completionInfo = hostedJobInfo.CompletionInfo.ToString();
                    var mail = EmailService.MakeMessage(_emailService.DefaultSender, email, $"CrawlJob Complete on {Environment.MachineName} {hostedJobInfo.SettingsJobArgsDTO.JobName}", $"{hostedJobInfo.Status}\r\n\r\n***\r\n\r\n{completionInfo}\r\n\r\nException: {(hostedJobInfo.HasException ? hostedJobInfo.GetException().ToString() : "No Fatal Exceptions...")}");
                    _emailService.Send(mail);
                }
            }
            catch (Exception ex)
            {
                if (isError) _logger.LogError(ex, $"Couldn't send email notification {hostedJobInfo.Id} to '{email??" "}'");
            }
            return hostedJobInfo;//do we make it here when there's been an exception.
        }
       private static SemaphoreSlim semaphore = new SemaphoreSlim(1, 1);
        private Tuple<NLog.Targets.Target, LoggingRule, ILogger> GetJobLogConfig(string jobName, string logPath)
        {
            var target = new NLog.Targets.FileTarget()
            {
                Name = jobName,
                FileName = logPath,
                FileNameKind = NLog.Targets.FilePathKind.Absolute,
                ArchiveAboveSize = 10 * 1024 ^ 2,
                ArchiveNumbering = NLog.Targets.ArchiveNumberingMode.Sequence,
            };          
            
            try
            {
                semaphore.Wait(_cts.Token);
                NLog.LogManager.Configuration.AddTarget(target);
                var rule = new LoggingRule(jobName + "rule") { LoggerNamePattern = jobName, Final = true };
                rule.EnableLoggingForLevels(NLog.LogLevel.Debug, NLog.LogLevel.Fatal);
                rule.Targets.Add(target);
                NLog.LogManager.Configuration.LoggingRules.Insert(0, rule);//add at beginning of ruleset so that rules in nlog.config file can supercede(filter for example)
                NLog.LogManager.ReconfigExistingLoggers();
                var logger = Program.LoggerFactory.CreateLogger(jobName);
                return new Tuple<NLog.Targets.Target, LoggingRule, ILogger>(target, rule, logger);
            }
            finally
            {
                semaphore.Release();
            }
        }

        private void RemoveNlogJobLogger(Tuple<NLog.Targets.Target, LoggingRule, ILogger> loggerSetup)
        {
            var target = loggerSetup.Item1;
            var rule = loggerSetup.Item2;
            //loggerSetup.Item3;//aparently it's not possible to remove loggers but only to disable them.
            NLog.LogManager.Configuration.RemoveTarget(target.Name);
            NLog.LogManager.Configuration.RemoveRuleByName(rule.RuleName);
            NLog.LogManager.ReconfigExistingLoggers();
        }

        private void Finish(HostedJobInfo jobInfo)
        {
            Interlocked.Increment(ref _jobsCompleted);
            if (isInfo) _logger.LogInformation("Completed {JobInfo}", jobInfo);
            var workerargs = SettingsJobArgsDTO.UnDTO(jobInfo.SettingsJobArgsDTO);
            var outputPath = workerargs.InputPathLocation;
            Save();

            var filename =Path.Combine(outputPath,MakeSafeFileName($"completed{jobInfo.SettingsJobArgsDTO.JobName}.json"));
            try
            {
                System.IO.File.AppendAllText(filename, jobInfo.ToString());
            }
            catch (Exception ex)
            {
                if (isError) _logger.LogError($"Couldn't save {filename} with {jobInfo.ToString()}", ex);
            }
            OnTaskCompleted(jobInfo.Id);
        }
        private char[] _badChars = System.IO.Path.GetInvalidFileNameChars();
        private string MakeSafeFileName(string filename)
        {
            return new string(filename.Where(x => !_badChars.Contains(x)).ToArray());
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
                    ElasticDiscoveryURI = new List<string> { "https://elasticnode:9200" },
                    ElasticIndexURI = new List<string> { "https://elasticnode:9200" },
                    DocInsertionThreads = 1,
                    JobName = "Sample" + rnd,
                    JobNotes = "Some notes about the sample job",
                    IgnoreExtensions = new List<string>() { ".dat", ".db" },
                    IndexNamePrefix = $"test{rnd}",
                    InputPathLocation = $"d:\\",
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
                    EmailNotification = ""// "james.blackadar@hok.com"
                };
                Enqueue(d);
                d = new SettingsJobArgsDTO()
                {
                    BulkUploadSize = rnd,
                    CrawlMode = HOK.Elastic.FileSystemCrawler.Models.CrawlMode.Incremental,
                    ElasticDiscoveryURI = new List<string> { "https://elasticnode:9200" },
                    ElasticIndexURI = new List<string> { "https://elasticnode:9200" },
                    DocInsertionThreads = 1,
                    JobName = "SampleIncremental" + rnd,
                    JobNotes = "Some notes about the sample job",
                    IgnoreExtensions = new List<string>() { ".dat", ".db" },
                    IndexNamePrefix = $"test{rnd}",
                    InputPathLocation = $"d:\\",
                    InputCrawls = new List<InputPathBase>() {new InputPathBase(){
                        Path = "c:\\archive"
                       ,Office = "tor"
                            }
                    },
                    PublishedPath = "c:\\",
                    PathForCrawling = "c:\\",
                    PathForCrawlingContent = "c:\\",
                    PathInclusionRegex = ".*",
                    FileNameExclusionRegex = ".*",
                    OfficeSiteExtractRegex = "[a-z]{2,5}",
                    ProjectExtractRegex = "\\d\\\\(([\\d|\\-|\\.]*\\d\\d)\\s?([\\+|\\s\\|\\-|_]+)([\\w]+[^\\\\|$|\\r|\\n]*))",
                    EmailNotification = ""// "james.blackadar@hok.com"
                };
                Enqueue(d);
            }
        }
#endif

    }
}
