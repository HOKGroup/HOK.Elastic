//using RtfPipe;

namespace HOK.Elastic.FileSystemCrawler.Models
{
    public class CrawlMetrics
    {
        public long DirCount { get; set; }
        public long FileCount { get; set; }
        public long FileSkipped { get; set; }
        public long FileNotFound { get; set; }
        public long Deleted { get; set; }
    }
}
