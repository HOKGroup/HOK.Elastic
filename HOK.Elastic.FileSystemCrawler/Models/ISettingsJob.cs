using System.Collections.Generic;

namespace HOK.Elastic.FileSystemCrawler.Models
{
    public interface ISettingsJob : ISettingsApp, IInputPathCollectionBase
    {
        CrawlMode CrawlMode { get; set; }
        bool? ReadFileContents { get; set; }
        ICollection<InputPathBase> InputPaths { get; set; }
    }
}
