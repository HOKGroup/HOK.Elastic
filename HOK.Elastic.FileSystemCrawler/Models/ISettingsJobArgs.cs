namespace HOK.Elastic.FileSystemCrawler.Models
{
    public interface ISettingsJobArgs:ISettingsJob,ISettingsJobPathArgs
    {
        int CrawlThreads { get; }
        int DocInsertionThreads { get; set; }
        int DocReadingThreads { get; }
        string InputPathLocation { get; set; }
        bool RunningInteractively { get; set; }
        string JsonQueryString { get; set; }
        //InputPathCollectionBase InputPathCollection { get; set; }
    }
}