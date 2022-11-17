using HOK.Elastic.FileSystemCrawler.Models;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Nest;
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace HOK.Elastic.FileSystemCrawler.WebAPI.Models
{
    public class SettingsJobArgsDTO : FileSystemCrawler.Models.SettingsJobArgs
    {
        [Required]
        public override string JobName { get; set; }

        public List<InputPathEventStream> InputEvents { get; set; }
        public List<InputPathBase> InputCrawls { get; set; }

        public List<string> ElasticDiscoveryURI { get; set; }
        public List<string> ElasticIndexURI { get; set; }

        public new InputPathCollectionBase InputPaths()
        {
            if (this.CrawlMode == CrawlMode.EventBased)
            {
                var i = new InputPathCollectionEventStream();
                foreach (var item in this.InputEvents)
                {
                    i.Add(item);
                }
                return i;
            }
            else
            {
                var i = new InputPathCollectionCrawl(this.InputCrawls);
                return i;
            }
        }
    }
}
