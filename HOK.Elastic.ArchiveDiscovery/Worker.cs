using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http.Json;
using System.Security.Cryptography.X509Certificates;
using System.Security.Policy;
using System.Text;
using System.Threading.Tasks;
using HOK.Elastic.DAL;
using HOK.Elastic.DAL.Models;
using HOK.Elastic.FileSystemCrawler.Models;
using HOK.Elastic.FileSystemCrawler.WebAPI.Models;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;


namespace HOK.Elastic.ArchiveDiscovery
{
    internal class Worker
    {
        private Repository<List<JobItem>> context = new Repository<List<JobItem>>();
        private HOK.Elastic.Logger.Log4NetLogger _il;
        private bool ilDebug;
        private bool ilInfo;
        private bool ilWarn;
        private bool ilError;
        private APIClient api;
        public Worker(string apihost)
        {
            _il = new HOK.Elastic.Logger.Log4NetLogger(nameof(Worker));
            ilDebug = _il.IsEnabled(Microsoft.Extensions.Logging.LogLevel.Debug);
            ilInfo = _il.IsEnabled(Microsoft.Extensions.Logging.LogLevel.Information);
            ilWarn = _il.IsEnabled(Microsoft.Extensions.Logging.LogLevel.Warning);
            ilError = _il.IsEnabled(Microsoft.Extensions.Logging.LogLevel.Error);
            api = new APIClient(apihost, new Logger.Log4NetLogger("API"));
        }
     
        internal async Task RunAsync(SettingsJobArgsDTO settingsJobArgsDTO)
        {
            //var settingsJobArgsDTO = (SettingsJobArgsDTO)settingsJobArgsDTO;
            StaticIndexPrefix.Prefix =settingsJobArgsDTO.IndexNamePrefix;
           var discoveryuris = settingsJobArgsDTO.ElasticDiscoveryURI.Select(x => new Uri(x)).ToList();
            DiscoveryArchiveRecrawl discoveryArchive = new DiscoveryArchiveRecrawl(discoveryuris, new Logger.Log4NetLogger("test"));
            var clientStatus = discoveryArchive.GetClientStatus();
            if(ilDebug)_il.LogDebugInfo("status",null,clientStatus);
            var offices = await discoveryArchive.FindOffices();
            foreach (var office in offices)
            {
                var projectRootsInArchive = await discoveryArchive.FindProjectRootsInArchive(office);
                if (projectRootsInArchive.Any())
                {
                    if (ilDebug) _il.LogDebugInfo(office + " " + String.Join(",", projectRootsInArchive.Select(x => x.Project.FullName)));
                    foreach (var archiveDocument in projectRootsInArchive.Where(x => x.Last_write_timeUTC >= DateTime.MinValue))
                    {
                        var productionDocument = await discoveryArchive.FindArchiveProjectsInProduction(office, archiveDocument.Project.Number);
                        if (productionDocument != null)
                        {
                            var workItem = new JobItem(office, archiveDocument.Project.Number, productionDocument.Id, archiveDocument.Id);
                            if (ilDebug) _il.LogDebugInfo($"Found matching pair PROD>ARCHIVE WorkItem", null, workItem);
                            context.Value.Add(workItem);
                        }
                    }
                }
                else
                {
                    if (ilDebug) _il.LogDebugInfo($"No projects found for: '{office}'", null,null);
                }
            }
            try
            {               
                await CopyToArchive(settingsJobArgsDTO);
            }catch(Exception e)
            {
                if (_il.IsEnabled(LogLevel.Critical)) _il.LogErr("Fatal",null, e);
            }
        }

        private async Task<bool> CopyToArchive(SettingsJobArgsDTO settingsJobArgsDTO)
        {
            DateTime timer = DateTime.MinValue;
            while (context.Value.Any())
            {
                #region PersistJobs
                if (DateTime.Now.Subtract(timer).TotalMinutes > 5)
                {
                    timer = DateTime.Now;
                    context.Save();
                }
                #endregion
                #region SendJobsToAPI               
                while (await api.HasFreeSlotsAsync())
                {
                    var item = context.Value.Where(x => x
                    .Status == FileSystemCrawler.WebAPI.HostedJobInfo.State.unstarted
                    ).FirstOrDefault();
                    if (item != null)
                    {
                        settingsJobArgsDTO.InputEvents = new List<InputPathEventStream>();

                        settingsJobArgsDTO.InputEvents.Add(new InputPathEventStream() { 
                            Path = item.Target, 
                            PathFrom = item.Source,
                            IsDir=true,
                            TimeStampUtc=DateTime.Now,
                            PresenceAction = ActionPresence.Copy 
                        }
                        );
                        settingsJobArgsDTO.JobName = $"ArchiveJob_{item.Office}_{item.ProjectNumber}";
                        settingsJobArgsDTO.JobNotes = $"ArchiveDiscovery{Environment.MachineName}{Environment.UserName}";
                        int Id = await api.PostAsync(settingsJobArgsDTO);
                        if (Id >= 0)
                        {
                            item.TaskId = Id;
                            item.Status = FileSystemCrawler.WebAPI.HostedJobInfo.State.started;
                        }else
                        {
                            //else job failed...we increment retries if we want to exit endless loop in case of failure.
                            await Task.Delay(TimeSpan.FromSeconds(5));
                        }
                    }
                    else
                    {
                        break;
                    }                    
                }
                #endregion
                #region MonitorJobsForCompletionAndRemove
                List<JobItem> jobsToBeRemoved = new List<JobItem>();
                    foreach (var job in context.Value.Where(x => x.Status >= FileSystemCrawler.WebAPI.HostedJobInfo.State.started))
                {
                    try
                    {
                        var jobInfo = await api.GetJobInfo(job.TaskId);
                        if (jobInfo == null) 
                        {
                            jobsToBeRemoved.Add(job);
                            
                        }
                        else if(jobInfo.Status == FileSystemCrawler.WebAPI.HostedJobInfo.State.complete)
                        {
                            jobsToBeRemoved.Add(job);
                            await api.DeleteAsync(job.TaskId);
                        }
                        else if (jobInfo.Status == FileSystemCrawler.WebAPI.HostedJobInfo.State.completedWithException)
                        {
                            if (job.Retries < 3)
                            {
                                //retry the job.
                                await api.DeleteAsync(job.TaskId);
                                job.Status = FileSystemCrawler.WebAPI.HostedJobInfo.State.unstarted;
                                job.Retries++;
                                //log warn that it's failing
                                if (ilWarn) _il.LogWarn($"Failed {job.Retries+1} times", job.Source, jobInfo);
                            }
                            else
                            {
                                if (ilError) _il.LogErr("Aborted", job.Source, job);
                                jobsToBeRemoved.Add(job);
                                await api.DeleteAsync(job.TaskId);
                            }
                        }
                        else
                        {
                            //still working on the job....
                        }
                    }catch(Exception ex)
                    {
                        if (ilError) _il.LogErr("Error monitoringjobs and removing completed", null,ilDebug? context.Value:null, ex);
                    }
                    }
                    foreach(var job in jobsToBeRemoved)
                    {
                        //log that this failed and not complete.                        
                        context.Value.Remove(job);
                    }
                    await Task.Delay(TimeSpan.FromSeconds(5));               
                #endregion
            }
            context.Save();
            return true;
        }


    }
}
