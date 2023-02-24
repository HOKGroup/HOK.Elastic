using HOK.Elastic.FileSystemCrawler.Models;
using HOK.Elastic.FileSystemCrawler.WebAPI;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Nest;
using SixLabors.ImageSharp.Memory;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Threading.Tasks.Dataflow;

namespace ConsoleTest
{
    //internal class HostedJobQueue:IHostedService
    //{

    //    //https://learn.microsoft.com/en-us/aspnet/core/fundamentals/host/hosted-services?view=aspnetcore-6.0&tabs=visual-studio
    //    public class JobInfo
    //    {
    //        private static object _lock = new object();
    //        private static int _uid = 0;
    //        private int _id;
    //        private SettingsJob _job;
    //        internal JobInfo(SettingsJob settingsJob)
    //        {
    //            Status = State.unstarted;
    //            lock (_lock)
    //            {
    //               _id = _uid++;
    //            }
    //            _job = settingsJob;
    //        }
    //        public bool IsComplete => Status == State.complete;
    //        public DateTime? WhenCompleted { get; set; }
    //        public SettingsJob SettingsJob => _job;
    //        public int Id => _id;
    //        public CompletionInfo CompletionInfo { get; set; }
    //        public State Status { get; set; }           
    //        public enum State
    //        {
    //            unstarted,
    //            started,
    //            complete
    //        }
    //    }

    //    private ConcurrentDictionary<int, JobInfo> _tasks = new ConcurrentDictionary<int, JobInfo>();
    //    CancellationTokenSource _cts = new CancellationTokenSource();
    //    private Task _taskLoop;
    //    private ILogger _logger;
    //    private BufferBlock<JobInfo> buffer ;
    //    private TransformBlock<JobInfo, JobInfo> action;
    //    private ActionBlock<JobInfo> completed ;

    //    public HostedJobQueue(ILogger logger,CancellationTokenSource cancellationTokenSource)
    //    {
    //        _logger = logger;
    //    }
    //    public void LoadSomeStuff(int itemsToCreate)
    //    {
    //        //load up some random tasks
    //        Random random = new Random();
    //        for (int i = 0; i < itemsToCreate; i++)
    //        {
    //            var ii = random.Next(500, 16000);
    //            var d =new JobInfo(new HOK.Elastic.FileSystemCrawler.Models.SettingsJob()
    //            {
    //                BulkUploadSize = ii,
    //                CrawlMode = HOK.Elastic.FileSystemCrawler.CrawlMode.QueryBasedReIndex
    //            });
    //            Console.WriteLine($">>>>>>>Inserting {d.Id} bulk {d.SettingsJob.BulkUploadSize}" );
    //            _tasks[d.Id] = d;
    //        }
    //    }

    //    public Task StartAsync(CancellationToken cancellationToken)
    //    {
    //        // Store the worker task
    //        _taskLoop = ExecuteAsync(cancellationToken);

    //        // If the task is completedId then return it,
    //        // this will bubble cancellation and failure to the caller
    //        if (_taskLoop.IsCompleted)
    //        {
    //            return _taskLoop;
    //        }
    //        // Otherwise it's running
    //        return Task.CompletedTask;
    //    }
    //    protected async Task ExecuteAsync(CancellationToken cancellationToken)
    //    {
    //        buffer = new BufferBlock<JobInfo>(new DataflowBlockOptions() { CancellationToken = cancellationToken });
    //        action = new TransformBlock<JobInfo, JobInfo>(async x => await RunJobAsync(x), new ExecutionDataflowBlockOptions() { BoundedCapacity = 4, MaxDegreeOfParallelism = 3 });
    //        completed = new ActionBlock<JobInfo>(x => Finish(x));
    //        buffer.LinkTo(action,new DataflowLinkOptions() { PropagateCompletion=true});
    //        action.LinkTo(completed,new DataflowLinkOptions() { PropagateCompletion = true});
    //        await Task.WhenAny(MonitorAsync(cancellationToken), completed.Completion);
    //    }

    //    public async Task MonitorAsync(CancellationToken cancellationToken)
    //    {
    //        DateTime trigger = DateTime.Now;
    //        while (!cancellationToken.IsCancellationRequested)
    //        {
    //            Console.WriteLine($"Of {_tasks.Count} tasks, {buffer.Count} are in the buffer and {_tasks.Values.Where(x=>x.IsComplete).Count()} are complete.");
    //            if (DateTime.Now.Subtract(trigger).TotalSeconds > 15)
    //            {
    //                trigger = DateTime.Now;
    //                if (buffer.Count < 1)///for testing purposes just keep making more items.
    //                {
    //                    LoadSomeStuff(3);
    //                }
    //            }
    //            //queue items to the bufferblock so they will be processed by the transformblock.
    //            if (buffer.Count < 4)
    //            {
    //                var next = _tasks.Values.Where(x => x.Status==JobInfo.State.unstarted).Take(4 - buffer.Count);
    //                if (next.Any())
    //                {
    //                    foreach (var x in next)
    //                    {
    //                        x.Status = JobInfo.State.started;
    //                        await buffer.SendAsync(x);
    //                    }
    //                }
    //            }
    //            await Task.Delay(5000);
    //        }
    //    }

    //    //https://learn.microsoft.com/en-us/dotnet/architecture/microservices/multi-container-microservice-net-applications/background-slots-with-ihostedservice
    //    async Task IHostedService.StopAsync(CancellationToken cancellationToken)
    //    {
    //        // Stop called without start
    //        if (_taskLoop == null)
    //        {
    //            return;
    //        }
    //        try
    //        {
    //            // Signal cancellation to the executing method  
    //            _cts.Cancel();
    //            buffer?.Complete();
    //        }
    //        finally
    //        {
    //            // Wait until the task completes or the stop token triggers
    //            await Task.WhenAny(_taskLoop, Task.Delay(Timeout.Infinite,
    //                                                          cancellationToken));
    //        }
    //    }
     
        
    //    public int Enqueue(SettingsJob settingsJob)
    //    {
    //        JobInfo d = new JobInfo(settingsJob);
    //        _tasks[d.Id] = d;
    //        return d.Id;
    //    }

    //    public JobInfo Get(int id)
    //    {
    //        if (_tasks.TryGetValue(id, out JobInfo d))
    //        {
    //            return d;
    //        }
    //        else
    //        {
    //            throw new IndexOutOfRangeException();
    //        }
    //    }

    //    public JobInfo Remove(int id)
    //    {
    //        if (_tasks.TryRemove(id, out JobInfo d))
    //        {
    //            return d;
    //        }
    //        else
    //        {
    //            throw new IndexOutOfRangeException();
    //        }
    //    }

    //    private void Finish(JobInfo jobInfo)
    //    {         
    //        OnTaskCompleted(jobInfo.Id);
    //        jobInfo = _tasks[jobInfo.Id];//we could just use x fo course.
    //        Console.WriteLine($">> {jobInfo.IsComplete} ID:{jobInfo.Id} had {jobInfo.SettingsJob.BulkUploadSize.Value} bulk upload size and Finished in {jobInfo.CompletionInfo?.DurationSeconds}s and {jobInfo.SettingsJob.PublishedPath}");
    //    }

    //    // declaring an event using built-in EventHandler
    //    public event EventHandler<int> ProcessCompleted;

    //    protected virtual void OnTaskCompleted(int Id)
    //    {
    //        ProcessCompleted?.Invoke(this, Id);
    //    }

    //    public async Task<JobInfo> RunJobAsync(JobInfo t)
    //    {
    //        ///long running crawl task....
    //        Console.WriteLine("&&&&&Working on:" + t.Id + "with bulk size of:" + t.SettingsJob.BulkUploadSize);
    //        await Task.Delay(t.SettingsJob.BulkUploadSize.Value,_cts.Token);
    //        var c = new HOK.Elastic.FileSystemCrawler.Models.CompletionInfo("Hello");
    //        c.Deleted = t.SettingsJob.BulkUploadSize.Value;
    //        t.CompletionInfo = c;
    //        t.WhenCompleted = DateTime.Now;
    //        t.SettingsJob.PublishedPath = "this was updated" + DateTime.Now.ToString();
    //        t.Status = JobInfo.State.complete;
    //        return t;
    //    }
    //}
}
