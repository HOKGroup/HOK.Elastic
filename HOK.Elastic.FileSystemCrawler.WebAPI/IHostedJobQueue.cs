using Microsoft.Extensions.Hosting;
using System;
using System.Collections.Generic;
using HOK.Elastic.FileSystemCrawler.WebAPI.DAL.Models;

namespace HOK.Elastic.FileSystemCrawler.WebAPI
{
    public interface IHostedJobQueue:IHostedService//could be a library project
    {
        HostedJobInfo Get(int Id);
        int Enqueue(SettingsJobArgsDTO job);//insert the job onto a queue. 
  
        HostedJobInfo Remove(int Id);

        int MaxJobs { get; }

        int FreeSlots { get; }
        TimeSpan UpTime { get; }
        int JobCompleted { get; }
        IEnumerable<HostedJobInfo> Jobs { get; }
    }
}
