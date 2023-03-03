using Nest;
using HOK.Elastic.FileSystemCrawler.WebAPI.DAL.Models;

namespace HOK.Elastic.ArchiveDiscovery
{
    public class JobItem
    {
        public JobItem() { }
        public JobItem(string office, string projectNumber, string source, string target)
        {
            Office = office;
            ProjectNumber = projectNumber;
            Source = source;
            Target = target;
        }
        public string? Office { get; set; }
        public string? ProjectNumber { get; set; }
        public string? Source { get; set; }
        public string? Target { get; set; }
        public HostedJobInfo.State Status { get; set; }//not the correct status of course.
        public int TaskId { get; set; }
        public int Retries { get; internal set; }
    }
}
