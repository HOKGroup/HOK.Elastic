using HOK.Elastic.FileSystemCrawler.Models;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Linq;

namespace HOK.Elastic.FileSystemCrawler.WebAPI.DAL.Models
{
    public class SettingsJobArgsDTO : FileSystemCrawler.Models.SettingsJobArgs
    {
        [Required]
        public override string JobName { get; set; }
        public string EmailNotification { get; set; }
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

        public static SettingsJobArgsDTO MakeDTO(ISettingsJobArgs settingsJobArgs)
        {
            SettingsJobArgsDTO settingsJobArgsDTO = JsonConvert.DeserializeObject<SettingsJobArgsDTO>(JsonConvert.SerializeObject(settingsJobArgs));
            if (settingsJobArgs.CrawlMode == CrawlMode.EventBased)
            {
                settingsJobArgsDTO.InputEvents = settingsJobArgs.InputPaths.Select(x => x as InputPathEventStream).ToList();//.FirstOrDefault() as InputPathEventStream;
            }
            else
            {
                settingsJobArgsDTO.InputCrawls = settingsJobArgs.InputPaths.Select(x => x as InputPathBase).ToList();
            }
            return settingsJobArgsDTO;
        }

        public static SettingsJobArgs UnDTO(SettingsJobArgsDTO settingsJobArgsDTO)
        {

            SettingsJobArgs settingsJobArgs = JsonConvert.DeserializeObject<SettingsJobArgs>(JsonConvert.SerializeObject(settingsJobArgsDTO));
            if (settingsJobArgs.CrawlMode == CrawlMode.EventBased)
            {
                settingsJobArgs.InputPaths = new InputPathCollectionEventStream();
                if (settingsJobArgsDTO.InputEvents != null)
                {
                    foreach (var item in settingsJobArgsDTO.InputEvents)
                    {
                        settingsJobArgs.InputPaths.Add(item);
                    }
                }
            }
            else
            {
                settingsJobArgs.InputPaths = new InputPathCollectionBase();
                if (settingsJobArgsDTO.InputCrawls != null)
                {
                    foreach (var item in settingsJobArgsDTO.InputCrawls)
                    {
                        settingsJobArgs.InputPaths.Add(item);
                    }
                }
            }
            return settingsJobArgs;
        }
    }
}
