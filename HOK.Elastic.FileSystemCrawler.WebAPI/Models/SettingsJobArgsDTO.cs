using HOK.Elastic.FileSystemCrawler.Models;
using Microsoft.AspNetCore.Mvc;
//using Newtonsoft.Json;

//using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace HOK.Elastic.FileSystemCrawler.WebAPI.Models
{
    public class SettingsJobArgsDTO : FileSystemCrawler.Models.SettingsJobArgs
    {
        [Required]
        public override string JobName { get; set; }
        public string EmailNotification { get; set; }

        private List<InputPathEventStream> _inputPaths = new List<InputPathEventStream>();
        /// <summary>
        /// Note to clear the input paths the collection must be cleared.
        /// </summary>
        public new List<InputPathEventStream> InputPaths { get => _inputPaths; set { if (value != null && value.Any()) _inputPaths = value; } }
        /// <summary>
        /// deprecated. this property exists to allow deserialization of old json job definitions.
        /// </summary>
        public List<InputPathEventStream> InputEvents { set { if (value != null && value.Any()) _inputPaths = value; } }
        /// <summary>
        /// deprecated. this property exists to allow deserialization of old json job definitions.
        /// </summary>
        public List<InputPathEventStream> InputCrawls { set { if (value != null && value.Any()) _inputPaths = value; } }
        public new List<string> ElasticDiscoveryURI { get; set; }
        public new List<string> ElasticIndexURI { get; set; }
      
        public static SettingsJobArgsDTO MakeDTO(ISettingsJobArgs settingsJobArgs)
        {
           // SettingsJobArgsDTO settingsJobArgsDTO =  JsonConvert.DeserializeObject<SettingsJobArgsDTO>(JsonConvert.SerializeObject(settingsJobArgs));
           var settings = JsonSerializer.Serialize(settingsJobArgs);
            SettingsJobArgsDTO settingsJobArgsDTO = JsonSerializer.Deserialize<SettingsJobArgsDTO>(settings);
            settingsJobArgsDTO.InputPaths = settingsJobArgs.InputPaths.Select(x => x as InputPathEventStream).ToList();//.FirstOrDefault() as InputPathEventStream;
            return settingsJobArgsDTO;
        }

        public static SettingsJobArgs UnDTO(SettingsJobArgsDTO settingsJobArgsDTO)
        {


            //SettingsJobArgs settingsJobArgs = JsonConvert.DeserializeObject<SettingsJobArgs>(JsonConvert.SerializeObject(settingsJobArgsDTO));    
            var settingsDTO = JsonSerializer.Serialize(settingsJobArgsDTO);
            var settingsJobArgs = JsonSerializer.Deserialize<SettingsJobArgs>(settingsDTO);

            if (settingsJobArgsDTO.CrawlMode == CrawlMode.EventBased)
            {
                var i = new InputPathCollectionEventStream();
                foreach (var item in settingsJobArgsDTO.InputPaths)
                {
                    i.Add(item);
                }
                settingsJobArgs.InputPaths = i;
            }
            else
            {
                var inputPathBases = settingsJobArgsDTO.InputPaths?.Select(x => new InputPathBase() { Office = x.Office, Path = x.Path }).ToList()??new List<InputPathBase>();
                var i = new InputPathCollectionCrawl(inputPathBases);
                settingsJobArgs.InputPaths = i;
            }
            return settingsJobArgs;
        }
    }
}
