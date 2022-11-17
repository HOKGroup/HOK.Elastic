namespace HOK.Elastic.FileSystemCrawler.Models
{
    public enum CrawlMode
    {
        EventBased = 0,
        FindMissingContent = 1,
        Incremental = 2,
        Full = 3,
        EmailOnlyMissingContent = 10,
        QueryBasedReIndex = 11
    }
}
