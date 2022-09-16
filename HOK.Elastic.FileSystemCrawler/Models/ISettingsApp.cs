using System;
using System.Collections.Generic;

namespace HOK.Elastic.FileSystemCrawler.Models
{
    public interface ISettingsApp
    {
        int? ReadContentSizeLimitMB { get; set; }
        IEnumerable<Uri> ElasticDiscoveryURI { get; set; }
        IEnumerable<Uri> ElasticIndexURI { get; set; }
        int? BulkUploadSize { get; set; }
        Uri FileSystemEventsAPI { get; set; }
        string IndexNamePrefix { get; set; }
        int? ExceptionsPerTenMinuteIntervalLimit { get; set; }
        string PathInclusionRegex { get; set; }
        string FileNameExclusionRegex { get; set; }
        List<string> IgnoreExtensions { get; set; }
        decimal? CPUCoreThreadMultiplier { get; set; }
        string OfficeSiteExtractRegex { get; set; }
        string ProjectExtractRegex { get; set; }
        string PipeCategorizationRegex { get; set; }
    }
}
