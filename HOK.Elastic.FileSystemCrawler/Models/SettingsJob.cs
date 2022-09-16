using System.Collections.Generic;

namespace HOK.Elastic.FileSystemCrawler.Models
{
    /// <summary>
    /// Basic Job Config including paths so we can see regex with path where it makes more sense for detailed, custom regex.
    /// </summary>
    public class SettingsJob :SettingsApp, ISettingsJob
    {       
        public CrawlMode CrawlMode { get; set; }
        public bool? ReadFileContents { get; set; }
        public ICollection<InputPathBase> InputPaths { get; set; }
        public int Count => InputPaths?.Count??0;
        public string PathForCrawling { get; set; }
        public string PathForCrawlingContent { get; set; }
        public string PublishedPath { get; set; }
    }
}