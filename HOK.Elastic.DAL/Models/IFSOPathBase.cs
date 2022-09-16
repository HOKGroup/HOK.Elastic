using System;
using System.Collections.Generic;
using System.Text;

namespace HOK.Elastic.DAL.Models
{
   public interface IFSOPathBase
    {
        /// <summary>
        /// For performance considerations, we may crawl a different, perhaps private, filepath then what is published to end-users.
        /// </summary>
        string PathForCrawling { get; }
        /// <summary>
        /// For performance considerations, we may crawl content (when extracting data for tika parsing for example) from a different, perhaps private, filepath then what is published to end-users.
        /// </summary>
        string PathForCrawlingContent { get; }
        /// <summary>
        /// The file path that end-users will be able to link to
        /// </summary>
        string PublishedPath { get; }   
    }
}
