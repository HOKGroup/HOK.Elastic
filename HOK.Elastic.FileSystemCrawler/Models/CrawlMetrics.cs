//using RtfPipe;

namespace HOK.Elastic.FileSystemCrawler.Models
{
    public class CrawlMetrics
    {
        public long DirCount { get; internal set; }
        public long FileCount { get; internal set; }
        public long FileSkipped { get; set; }
        public long FileNotFound { get; set; }
        public long Deleted { get; set; }
    }
}
