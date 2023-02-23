using System.Collections.Generic;

namespace HOK.Elastic.FileSystemCrawler.Models
{
    public interface ISettingsJob : ISettingsApp, DAL.Models.IFSOPathBase
    {
        CrawlMode CrawlMode { get; set; }
        bool? ReadFileContents { get; set; }
        InputPathCollectionBase InputPaths { get; set; }
    }
}
