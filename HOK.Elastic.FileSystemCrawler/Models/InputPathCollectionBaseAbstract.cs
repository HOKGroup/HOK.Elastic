using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text.Json.Serialization;

namespace HOK.Elastic.FileSystemCrawler.Models
{
    /// <summary>
    /// Common properties
    /// </summary>
    public abstract class InputPathCollectionBaseAbstract:DAL.Models.IFSOPathBase
    {
        public string PathForCrawling { get; set; }
        public string PathForCrawlingContent { get; set; }
        public string PublishedPath { get; set; }
        public abstract int Count { get; }
    }
}
