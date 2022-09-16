using System;
using System.Collections.Generic;
namespace HOK.Elastic.FileSystemCrawler.Models
{
    /// <summary>
    /// Basic config settings at application level.
    /// </summary>
    public class SettingsApp : ISettingsApp
    { 
        public IEnumerable<Uri> ElasticDiscoveryURI { get; set; }
        public IEnumerable<Uri> ElasticIndexURI { get; set; }
        public Uri FileSystemEventsAPI { get; set; }
        public string IndexNamePrefix { get; set; } = "Test-";
        public int? ExceptionsPerTenMinuteIntervalLimit { get; set; } = 1;
        public int? ReadContentSizeLimitMB { get; set; } = 100;
        public int? BulkUploadSize { get; set; } = 400;
        public string PathInclusionRegex { get; set; }
        public string FileNameExclusionRegex { get; set; }
        public List<string> IgnoreExtensions { get; set; }
        public decimal? CPUCoreThreadMultiplier { get; set; } = 1;
        public string OfficeSiteExtractRegex { get;set; }
        public string ProjectExtractRegex { get;set; }
        public string PipeCategorizationRegex { get; set; }
    }
}
