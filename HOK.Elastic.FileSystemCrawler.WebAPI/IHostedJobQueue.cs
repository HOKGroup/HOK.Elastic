using HOK.Elastic.FileSystemCrawler.Models;
using Microsoft.Extensions.Hosting;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using static HOK.Elastic.FileSystemCrawler.WebAPI.HostedJobQueue;

namespace HOK.Elastic.FileSystemCrawler.WebAPI
{
    public interface IHostedJobQueue:IHostedService//could be a library project
    {
        HostedJobInfo Get(int Id);
        int Enqueue(SettingsJobArgs job);//insert the job onto a queue. 

        HostedJobInfo Remove(int Id);

        int MaxJobs { get; }

        int FreeSlots { get; }

        IEnumerable<HostedJobInfo> Jobs { get; }
    }

}
