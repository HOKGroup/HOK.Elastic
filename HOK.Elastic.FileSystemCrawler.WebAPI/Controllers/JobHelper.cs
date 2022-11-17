using HOK.Elastic.FileSystemCrawler.Models;
using HOK.Elastic.FileSystemCrawler.WebAPI.Models;
using Newtonsoft.Json;
using System;
using System.Linq;
using System.Collections.Generic;

namespace HOK.Elastic.FileSystemCrawler.WebAPI.Controllers
{
    public static class JobHelper
    {
        public static int Post(IHostedJobQueue hostedJobQueue, SettingsJobArgsDTO settingsJobArgsdto, string remoteidentifier)
        {
            var settingsJobArgs = settingsJobArgsdto as SettingsJobArgs;
            settingsJobArgs.JobNotes = remoteidentifier + settingsJobArgs.JobNotes;
            settingsJobArgs = UnDTO(settingsJobArgsdto);
            var jobId = hostedJobQueue.Enqueue(settingsJobArgs);
            return jobId;
        }

        internal static SettingsJobArgsDTO MakeDTO(ISettingsJobArgs settingsJobArgs)
        {
            SettingsJobArgsDTO settingsJobArgsDTO = JsonConvert.DeserializeObject<SettingsJobArgsDTO>(JsonConvert.SerializeObject(settingsJobArgs));
            if(settingsJobArgs.CrawlMode==CrawlMode.EventBased)
            {
                settingsJobArgsDTO.InputEvents = settingsJobArgs.InputPaths.Select(x=>x as InputPathEventStream).ToList();//.FirstOrDefault() as InputPathEventStream;
            }
            else
            {  
                settingsJobArgsDTO.InputCrawls = settingsJobArgs.InputPaths.Select(x => x as InputPathBase).ToList();
            }
            return settingsJobArgsDTO;
        }

        internal static SettingsJobArgs UnDTO(SettingsJobArgsDTO settingsJobArgsDTO)
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
