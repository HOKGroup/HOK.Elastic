﻿using HOK.Elastic.FileSystemCrawler.Models;
using HOK.Elastic.FileSystemCrawler.WebAPI.DAL.Models;
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
            if(!settingsJobArgs.JobNotes.StartsWith(remoteidentifier))
                {
                settingsJobArgs.JobNotes = remoteidentifier + "-" + settingsJobArgs.JobNotes;
            }            
            var jobId = hostedJobQueue.Enqueue(settingsJobArgsdto);
            return jobId;
        }
    }
}
