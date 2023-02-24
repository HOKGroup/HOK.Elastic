using Microsoft.Extensions.Hosting;
using System.Collections.Generic;

namespace HOK.Elastic.FileSystemCrawler.WebAPI
{
    public interface IHostedJobQueue:IHostedService//could be a library project
    {
        HostedJobInfo Get(int Id);
        int Enqueue(Models.SettingsJobArgsDTO job);//insert the job onto a queue. 
  
        HostedJobInfo Remove(int Id);

        int MaxJobs { get; }

        int FreeSlots { get; }

        IEnumerable<HostedJobInfo> Jobs { get; }
    }

}
