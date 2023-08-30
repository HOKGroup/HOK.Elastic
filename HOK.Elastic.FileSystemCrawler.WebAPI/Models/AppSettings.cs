namespace HOK.Elastic.FileSystemCrawler.WebAPI.Models
{
    public class AppSettings
    {
        public string GrantGroup { get; set; }
        public int ConcurrentJobs { get; set; }
        public string EmailSMTPhost { get; set; }
        public int EmailSMTPport { get; set; }
        public string EmailDefaultSenderSuffix { get; set; }
    }
}
