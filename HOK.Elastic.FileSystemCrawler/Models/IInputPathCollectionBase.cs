using System.Collections.Generic;

namespace HOK.Elastic.FileSystemCrawler.Models
{
    public interface IInputPathCollectionBase: DAL.Models.IFSOPathBase
    {
        int Count { get; }
    }
}