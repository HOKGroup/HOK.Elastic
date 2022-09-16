using System.Collections.Generic;

namespace HOK.Elastic.FileSystemCrawler.Models
{
    /// <summary>
    /// Minimum requirements for knowing about what path to crawl.
    /// </summary>
    public interface IInputPathBase
    {
        string Office { get; set; }
        string Path { get; set; }
        PathStatus PathStatus { get; set; }
        bool Equals(InputPathBase other);
        bool Equals(object value);
        int GetHashCode();
        string ToString();
    }
}