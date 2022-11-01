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

namespace HOK.Elastic.FileSystemCrawler.WebAPI
{

    public partial class HostedJobQueue : IHostedService, IHostedJobQueue
    {
        private ConcurrentDictionary<int, HostedJobInfo> _tasks = new ConcurrentDictionary<int, HostedJobInfo>();
        CancellationTokenSource _cts = new CancellationTokenSource();
        private Task _taskLoop;
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
        public int FreeSlots => MaxJobs - buffer.Count - action.InputCount - action.OutputCount - completed.InputCount;

        public HostedJobQueue(ILogger<HostedJobQueue> logger, int maxJobs)
        {
            _logger = logger;
            isDebug = _logger.IsEnabled(LogLevel.Debug);
            isInfo =  _logger.IsEnabled(LogLevel.Information);
            isError = _logger.IsEnabled(LogLevel.Error);
            MaxJobs = maxJobs;
        }     

        protected virtual void OnTaskCompleted(int Id)
        {
            ProcessCompleted?.Invoke(this, Id);
        }

        public Task StartAsync(CancellationToken cancellationToken)
        {
            // Store the worker task
            if (isInfo) _logger.LogInfo("Starting...");
            _taskLoop = ExecuteAsync(cancellationToken);

            // If the task is completedId then return it,
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
            action = new TransformBlock<HostedJobInfo, HostedJobInfo>(async x => await RunTaskAsync(x), new ExecutionDataflowBlockOptions() { BoundedCapacity = MaxJobs, MaxDegreeOfParallelism = MaxJobs });
            completed = new ActionBlock<HostedJobInfo>(x => Finish(x));
            buffer.LinkTo(action, new DataflowLinkOptions() { PropagateCompletion = true });
            action.LinkTo(completed, new DataflowLinkOptions() { PropagateCompletion = true });
            await Task.WhenAny(MonitorAsync(cancellationToken), completed.Completion);
        }

        public async Task MonitorAsync(CancellationToken cancellationToken)
        {
            DateTime trigger = DateTime.Now;
            LoadSomeRandomTestStuff(1);
            while (!cancellationToken.IsCancellationRequested)
            {                
              if(isDebug)_logger.LogDebug($"Of {_tasks.Count} tasks, {buffer.Count} are in the buffer and {_tasks.Values.Where(x => x.IsCompleted).Count()} are complete.");
                if (DateTime.Now.Subtract(trigger).TotalSeconds > 30)
                {
                    trigger = DateTime.Now;
                    if (buffer.Count < 1)///for testing purposes just keep making more items.
                    {
                     //   LoadSomeRandomTestStuff(3);
                    }
                }
                //queue items to the bufferblock so they will be processed by the transformblock.
                if (buffer.Count < 4)
                {
                    var next = _tasks.Values.Where(x => x.Status == HostedJobInfo.State.unstarted).Take(4 - buffer.Count);
                    if (next.Any())
                    {
                        foreach (var x in next)
                        {
                            x.Status = HostedJobInfo.State.started;
                            await buffer.SendAsync(x);
                        }
                    }
                }
                await Task.Delay(TimeSpan.FromSeconds(15));
            }
        }

        public int Enqueue(SettingsJobArgs settingsJobArgs)
        {
            HostedJobInfo d = new HostedJobInfo(settingsJobArgs, _cts.Token);
            _tasks[d.Id] = d;
            return d.Id;
        }

        public HostedJobInfo Get(int id)
        {
            if (_tasks.TryGetValue(id, out HostedJobInfo d))
            {
                return d;
            }
            else
            {
                throw new IndexOutOfRangeException();
            }
        }

        public IEnumerable<HostedJobInfo> Jobs
        {
            get { return _tasks.Values; }
        }

        public HostedJobInfo Remove(int id)
        {
            if (_tasks.TryRemove(id, out HostedJobInfo d))
            {
                if (!d.IsCompleted) d.Cancel();
                return d;
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
                await Task.WhenAny(_taskLoop, Task.Delay(Timeout.Infinite,
                                                              cancellationToken));
            }
        }

        private void Finish(HostedJobInfo jobInfo)
        {
            jobInfo.WhenCompleted = DateTime.Now;
            jobInfo.Status = HostedJobInfo.State.complete;
            OnTaskCompleted(jobInfo.Id);
            jobInfo = _tasks[jobInfo.Id];
            if (isInfo) _logger.LogInformation("Completed {JobInfo}",jobInfo);
        }
  
        public void LoadSomeRandomTestStuff(int itemsToCreate)
        {
            //load up some random tasks
            Random random = new Random();
            for (int i = 0; i < itemsToCreate; i++)
            {
                var ii = random.Next(500, 16000);
                var d = new HostedJobInfo(new HOK.Elastic.FileSystemCrawler.Models.SettingsJobArgs()
                {
                    BulkUploadSize = ii,
                    CrawlMode = HOK.Elastic.FileSystemCrawler.CrawlMode.EventBased,
                    ElasticDiscoveryURI = new Uri[] { new Uri("https://hok-395vs:9200/"), new Uri("https://hok-395vs:9200/") },
                    ElasticIndexURI = new Uri[] { new Uri("https://hok-395vs:9200/"), new Uri("https://hok-395vs:9200/") },
                    DocInsertionThreads = 1,
                    IgnoreExtensions = new List<string>() { ".dat", ".db" },
                    IndexNamePrefix = "testindex-",
                    InputPathLocation = "hmmmmmmmmm",
                    InputPaths = new InputPathCollectionEventStream(),
                    PublishedPath = "\\\\test\\"
                }, _cts.Token) ;
                d.SettingsJobArgs.InputPaths.Add(new InputPathEventStream() { IsDir = true, Path = "d:\\tigerbridge", PathFrom = "c:\\production", PresenceAction = ActionPresence.Copy });
                if (isInfo) _logger.LogInformation($">>>>>>>Inserting {d.Id} bulk {d.SettingsJobArgs.BulkUploadSize}");
                _tasks[d.Id] = d;
            }
        }

       
        public async Task<HostedJobInfo> RunTaskAsync(HostedJobInfo hostedJobInfo)
        {
            try
            {
                hostedJobInfo.CompletionInfo = new CompletionInfo(hostedJobInfo.SettingsJobArgs);
                var settingsJobArgs = hostedJobInfo.SettingsJobArgs;
             
                var index = new DAL.Index(settingsJobArgs.ElasticIndexURI.First(), new Elastic.Logger.Log4NetLogger("index"));
                var discovery = new DAL.Discovery(settingsJobArgs.ElasticDiscoveryURI.First(), new Elastic.Logger.Log4NetLogger("discovery"));
                var logger = new Elastic.Logger.Log4NetLogger($"worker{hostedJobInfo.Id}");
                if (logger.IsEnabled(LogLevel.Information)) logger.LogInformation("Constructing....");
                SecurityHelper sh = new SecurityHelper(logger);
                DocumentHelper dh = new DocumentHelper(true, sh, index, logger);
                WorkerEventStream ws = new WorkerEventStream(index, discovery, sh, dh, logger);
                if (logger.IsEnabled(LogLevel.Information)) logger.LogInformation("Starting....");
                hostedJobInfo.CompletionInfo = await ws.RunAsync(settingsJobArgs, hostedJobInfo.GetCancellationToken());
                var token = hostedJobInfo.GetCancellationToken();
                var testdelay = rnd.Next(60000,120000);
                if (logger.IsEnabled(LogLevel.Information)) logger.LogInformation("Delay by:" + testdelay.ToString());
                settingsJobArgs.FileNameExclusionRegex = "Delay by:" + testdelay.ToString();
                await (Task.Delay(testdelay, token));
                if (logger.IsEnabled(LogLevel.Information)) logger.LogInformation("Finisehd");
            }
            catch(Exception ex)
            
            {
                if (isError) _logger.LogError(ex, $"Running {hostedJobInfo.Id}");
                hostedJobInfo.Exception = ex;
                hostedJobInfo.Status = HostedJobInfo.State.completedWithException;
            }            
            hostedJobInfo.CompletionInfo.FileSkipped = DateTime.Now.Ticks;
            return hostedJobInfo;
        }
    }
}
