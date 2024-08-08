using HOK.Elastic.FileSystemCrawler.Models;
using HOK.Elastic.FileSystemCrawler.WebAPI.Models;
using Newtonsoft.Json;
using System;
using System.Linq;
using System.Collections.Generic;
using System.Net;
using System.Text.RegularExpressions;

namespace HOK.Elastic.FileSystemCrawler.WebAPI
{
    public static class JobHelper
    {
        public static int Post(IHostedJobQueue hostedJobQueue, SettingsJobArgsDTO settingsJobArgsdto, string remoteidentifier)
        {
            //var settingsJobArgs = settingsJobArgsdto as SettingsJobArgs;
            var settingsJobArgs = SettingsJobArgsDTO.UnDTO(settingsJobArgsdto);
            var match = Regex.Match(settingsJobArgs.JobNotes, "^\\d{1,3}(\\.\\d{1,3}){3}");
            if(match.Success)//maybe an old jobtemplate with old ip address in the notes. Let's replace it.
            {
                if (IPAddress.TryParse(match.Captures[0].Value, out IPAddress ipAddress))
                {
                    settingsJobArgs.JobNotes = settingsJobArgs.JobNotes.Substring(match.Captures[0].Value.Length);              
                    settingsJobArgs.JobNotes = remoteidentifier + "-" + settingsJobArgs.JobNotes.Trim('-');
                }
            }
            else
            {
                settingsJobArgs.JobNotes = remoteidentifier + "-" + settingsJobArgs.JobNotes;
            }
            var jobId = hostedJobQueue.Enqueue(settingsJobArgsdto);
            return jobId;
        }
    }
}
