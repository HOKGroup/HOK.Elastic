using HOK.Elastic.FileSystemCrawler;
using HOK.Elastic.FileSystemCrawler.Models;
using log4net.Repository.Hierarchy;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Threading.Tasks.Dataflow;
using HOK.Elastic.DAL;
using System.Collections.Generic;
using HOK.Elastic.DAL.Models;
using System.Runtime.InteropServices.WindowsRuntime;

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
        private bool isError;
        private BufferBlock<HostedJobInfo> buffer;
        private TransformBlock<HostedJobInfo, HostedJobInfo> action;
        private ActionBlock<HostedJobInfo> completed;
        private Random rnd = new Random();
        public event EventHandler<int> ProcessCompleted;
        


        public int MaxJobs { get; private set; }
        public int FreeSlots
        {
            get
            {
                var result = MaxJobs - (_jobs.Values.Where(x=>x.IsCompleted!=true)?.Take(MaxJobs).Count() ?? MaxJobs);
                if (result <= MaxJobs)
                {
                    return result;
                }
                else
                {
                    return MaxJobs;
                }
            }
        }

        public HostedJobQueue(ILogger<HostedJobQueue> logger, int maxJobs)
        {
            _logger = logger;
            isDebug = _logger.IsEnabled(LogLevel.Debug);
            isInfo = _logger.IsEnabled(LogLevel.Information);
            isError = _logger.IsEnabled(LogLevel.Error);
            MaxJobs = maxJobs;
        }

        protected virtual void OnTaskCompleted(int Id)
        {
            ProcessCompleted?.Invoke(this, Id);
        }

        public Task StartAsync(CancellationToken cancellationToken)
        {
            // Store the iWorker task
            if (isInfo) _logger.LogInfo("Starting...");
            if (_taskLoop == null)
            {
                _taskLoop = ExecuteAsync(cancellationToken);
            }
            // If the task is completed then return it,
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
            buffer = new BufferBlock<HostedJobInfo>(new DataflowBlockOptions() { CancellationToken = cancellationToken });
            action = new TransformBlock<HostedJobInfo, HostedJobInfo>(async x => await RunJobAsync(x), new ExecutionDataflowBlockOptions() { MaxDegreeOfParallelism = MaxJobs });//TODO maxdegreeofparallelism is 1 so we don't run into conflicts with the static PathHelper class....:(
            completed = new ActionBlock<HostedJobInfo>(x => Finish(x));
            buffer.LinkTo(action, new DataflowLinkOptions() { PropagateCompletion = true });
            action.LinkTo(completed, new DataflowLinkOptions() { PropagateCompletion = true });
            await Task.WhenAny(MonitorAsync(cancellationToken), completed.Completion);
            if (isDebug) _logger.LogDebug($"ExecuteAsync Monitor Complete");
        }

        public async Task MonitorAsync(CancellationToken cancellationToken)
        {
            //DateTime trigger = DateTime.Now;
#if DEBUG
            LoadSomeRandomJobs(1);
#endif
            while (!cancellationToken.IsCancellationRequested)
            {
                if (isDebug) _logger.LogDebug($"Of {_jobs.Count} jobs, {buffer.Count} are in the buffer and {_jobs.Values.Where(x => x.IsCompleted).Count()} are complete.");
                //if (DateTime.Now.Subtract(trigger).TotalSeconds > 30)
                //{
                //    trigger = DateTime.Now;
                //    if (buffer.Count < 1)///for testing purposes just keep making more items.
                //    {
                //        //   LoadSomeRandomJobs(3);
                //    }
                //}
                //queue items to the bufferblock so they will be processed by the transformblock.
                if (buffer.Count < 4)
                {
                    var next = _jobs.Values.Where(x => x.Status == HostedJobInfo.State.unstarted).Take(MaxJobs - buffer.Count);
                    if (next.Any())
                    {
                        foreach (var x in next)
                        {
                            await buffer.SendAsync(x);
                        }
                    }
                }
                await Task.Delay(TimeSpan.FromSeconds(30));//monitor Loop
            }
        }

        public int Enqueue(SettingsJobArgs settingsJobArgs)
        {
            HostedJobInfo job = new HostedJobInfo(settingsJobArgs, _cts.Token);
            _jobs[job.Id] = job;
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
                buffer?.Complete();
            }
            finally
            {
                // Wait until the task completes or the stop token triggers
                await Task.WhenAny(_taskLoop, Task.Delay(Timeout.Infinite, cancellationToken));
            }
        }

        private void Finish(HostedJobInfo jobInfo)
        {
            if (isInfo) _logger.LogInformation("Completed {JobInfo}", jobInfo);
            OnTaskCompleted(jobInfo.Id);
        }

#if DEBUG
        public void LoadSomeRandomJobs(int itemsToCreate)
        {
            //load up some random tasks
            Random random = new Random();
            for (int i = 0; i < itemsToCreate; i++)
            {
                var ii = random.Next(500, 16000);
                var d = new HostedJobInfo(new HOK.Elastic.FileSystemCrawler.Models.SettingsJobArgs()
                {
                    BulkUploadSize = ii,
                    CrawlMode = HOK.Elastic.FileSystemCrawler.Models.CrawlMode.EventBased,
                    ElasticDiscoveryURI = new Uri[] { new Uri("https://elasitcnode:9200/") },
                    ElasticIndexURI = new Uri[] { new Uri("https://elasticnode:9200/") },
                    DocInsertionThreads = 1,
                    JobName = "Sample",
                    JobNotes = "Some notes about the sample job",
                    IgnoreExtensions = new List<string>() { ".dat", ".db" },
                    IndexNamePrefix = $"test{ii}",
                    InputPathLocation = $"Job{ii}",
                    InputPaths = new InputPathCollectionEventStream(),
                    PublishedPath = "c:\\",
                    PathForCrawling = "c:\\",
                    PathForCrawlingContent = "c:\\",
                    PathInclusionRegex = ".*",
                    FileNameExclusionRegex = ".*",
                    OfficeSiteExtractRegex = "[a-z]{2,5}",
                    ProjectExtractRegex = "\\d\\\\(([\\d|\\-|\\.]*\\d\\d)\\s?([\\+|\\s\\|\\-|_]+)([\\w]+[^\\\\|$|\\r|\\n]*))",
                }, _cts.Token);
                d.SettingsJobArgs.InputPaths.Add(new InputPathEventStream()
                {
                    IsDir = true,
                    Path = "c:\\archive",
                    PathFrom = "c:\\production",
                    PresenceAction = ActionPresence.Copy
                });
                if (isInfo) _logger.LogInformation($">>>>>>>Inserting {d.Id} bulk {d.SettingsJobArgs.BulkUploadSize}");
                _jobs[d.Id] = d;
            }
        }
#endif

        public async Task<HostedJobInfo> RunJobAsync(HostedJobInfo hostedJobInfo)
        {
            try
            {
                hostedJobInfo.Status = HostedJobInfo.State.started;
                hostedJobInfo.CompletionInfo = new CompletionInfo(hostedJobInfo.SettingsJobArgs);
                var workerargs = hostedJobInfo.SettingsJobArgs;

                //we should add pre-flight check in indexbase or crawlerbase or something to ensure these basics are set.or refactor so we don't have shared static..
                HOK.Elastic.DAL.Models.PathHelper.Set(workerargs.PublishedPath, workerargs.PathForCrawlingContent, workerargs.PathForCrawling);
                HOK.Elastic.DAL.Models.PathHelper.SetPathInclusion(workerargs.PathInclusionRegex);
                HOK.Elastic.DAL.Models.PathHelper.SetFileNameExclusion(workerargs.FileNameExclusionRegex);
                HOK.Elastic.DAL.Models.PathHelper.SetOfficeExtractRgx(workerargs.OfficeSiteExtractRegex);
                HOK.Elastic.DAL.Models.PathHelper.SetProjectExtractRgx(workerargs.ProjectExtractRegex);
                HOK.Elastic.DAL.Models.PathHelper.IgnoreExtensions = workerargs.IgnoreExtensions?.Distinct().ToHashSet();
                DAL.StaticIndexPrefix.Prefix = workerargs.IndexNamePrefix;
                string safepath = workerargs.JobName + workerargs.JobNotes;
                System.IO.Path.GetInvalidPathChars().Select(x => safepath = safepath.Replace(x, ' '));
                workerargs.InputPathLocation = System.IO.Path.Combine("webapijobs", safepath + hostedJobInfo.GetHashCode());
                //end of unchecked requirements stuff that causes problems.

                var index = new DAL.Index(workerargs.ElasticIndexURI.First(), new Elastic.Logger.Log4NetLogger("index"));
                var discovery = new DAL.Discovery(workerargs.ElasticDiscoveryURI.First(), new Elastic.Logger.Log4NetLogger("discovery"));
                var logger = new Elastic.Logger.Log4NetLogger($"Worker{hostedJobInfo.Id}");

                if (logger.IsEnabled(LogLevel.Information)) logger.LogInformation("Constructing....");
                SecurityHelper sh = new SecurityHelper(logger);
                DocumentHelper dh = new DocumentHelper(true, sh, index, logger);
                IWorkerBase iWorker;
                if (workerargs.CrawlMode == CrawlMode.EventBased)
                {
                    iWorker = new WorkerEventStream(index, discovery, sh, dh, logger);
                }
                else
                {
                    iWorker = new WorkerCrawler(index, discovery, sh, dh, logger);
                }

                if (logger.IsEnabled(LogLevel.Information)) logger.LogInfo("Starting....",null,hostedJobInfo.SettingsJobArgs.JobName);
               
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
                if (logger.IsEnabled(LogLevel.Information)) logger.LogInformation("Finisehd");
            }
            catch (Exception ex)
            {
                if (isError) _logger.LogError(ex, $"Running {hostedJobInfo.Id}");
                hostedJobInfo.Exception = ex;
                hostedJobInfo.Status = HostedJobInfo.State.completedWithException;
            }
            hostedJobInfo.WhenCompleted = DateTime.Now;
            return hostedJobInfo;
        }
    }
}
