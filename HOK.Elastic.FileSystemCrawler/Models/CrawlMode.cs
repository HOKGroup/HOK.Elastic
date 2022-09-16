namespace HOK.Elastic.FileSystemCrawler
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
