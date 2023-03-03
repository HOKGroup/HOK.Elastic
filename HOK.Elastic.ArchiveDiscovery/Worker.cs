using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http.Json;
using System.Security.Cryptography.X509Certificates;
using System.Security.Policy;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using HOK.Elastic.DAL;
using HOK.Elastic.DAL.Models;
using HOK.Elastic.FileSystemCrawler.Models;
using HOK.Elastic.FileSystemCrawler.WebAPI.DAL.Models;
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

        internal async Task RunAsync(SettingsJobArgsDTO settingsJobArgsDTO,string pathPrefix,string pathProdSuffix,string pathArchiveSuffix,Regex officeMatch=null)
        {
            StaticIndexPrefix.Prefix = settingsJobArgsDTO.IndexNamePrefix;
            var discoveryuris = settingsJobArgsDTO.ElasticDiscoveryURI.Select(x => new Uri(x)).ToList();
            DiscoveryArchiveRecrawl discoveryArchive = new DiscoveryArchiveRecrawl(discoveryuris, new Logger.Log4NetLogger(nameof(Worker)));
            var clientStatus = discoveryArchive.GetClientStatus();
            if (ilDebug) _il.LogDebugInfo("Status", null, clientStatus);
            var offices = await discoveryArchive.FindOffices();
            if(offices != null) { offices = offices.Where(x => officeMatch.IsMatch(x)); }
            if (offices != null && offices.Any())
            {
                foreach (var office in offices)
                {
                    if (ilInfo) _il.LogInfo($">>>Searching: '{office}'", null, null);

                    var projectRootsInArchive = discoveryArchive.FindProjectRootsInArchive(pathPrefix,  office,pathArchiveSuffix);
                    if (projectRootsInArchive.Any())
                    {
                        foreach (var archiveDocument in projectRootsInArchive.Where(x => x.Last_write_timeUTC >= DateTime.MinValue))
                        {
                            //if (ilDebug) _il.LogDebug($"searching '{office}' for '{archiveDocument.Project.FullName.ToString()}");
                            var productionDocument = await discoveryArchive.FindArchiveProjectsInProduction(pathPrefix, office,pathProdSuffix, archiveDocument.Project.Number, archiveDocument.Project.Name);
                            if (productionDocument != null)
                            {
                                var workItem = new JobItem(office, archiveDocument.Project.Number, productionDocument.Id, archiveDocument.Id);
                                if (ilInfo) _il.LogInfo($">>>Found matching pair PROD>ARCHIVE WorkItem", null, workItem);
                                context.Value.Add(workItem);
                            }
                        }
                    }
                    else
                    {
                        if (ilDebug) _il.LogDebugInfo($"No projects found for: '{office}'", null, null);
                    }
                }
            }
            else
            {
                if (ilInfo) _il.LogInfo("No offices" + officeMatch !=null? " matched " + officeMatch.ToString():" found that matched");
            }
            try
            {
                await CopyToArchive(settingsJobArgsDTO);
            }
            catch (Exception e)
            {
                if (_il.IsEnabled(LogLevel.Critical)) _il.LogErr("Fatal", null, e);
            }
        }

        private async Task<bool> CopyToArchive(SettingsJobArgsDTO settingsJobArgsDTO)
        {

            DateTime timer = DateTime.MinValue;
            while (context.Value.Any())
            {
                if (ilInfo) _il.LogInfo("Looping jobs in context...", null, context.Value.Count);
                #region PersistJobs
                if (DateTime.Now.Subtract(timer).TotalMinutes > 2)
                {
                    timer = DateTime.Now;
                    context.Save();
                }
                #endregion
                #region SendJobsToAPI               
                while (await api.HasFreeSlotsAsync())
                {
                    var item = context.Value.Where(x => x
                    .Status == HostedJobInfo.State.unstarted
                    ).FirstOrDefault();
                    if (item != null)
                    {
                        settingsJobArgsDTO.InputEvents = new List<InputPathEventStream>
                        {
                            new InputPathEventStream()
                            {
                                Path = item.Target,
                                PathFrom = item.Source,
                                IsDir = true,
                                TimeStampUtc = DateTime.Now,
                                PresenceAction = ActionPresence.Copy
                            }
                        };
                        settingsJobArgsDTO.JobName = $"ArchiveJob_{item.Office}_{item.ProjectNumber}";
                        settingsJobArgsDTO.JobNotes = $"ArchiveDiscovery{Environment.MachineName}{Environment.UserName}";
                        int Id = await api.PostAsync(settingsJobArgsDTO);
                        if (Id >= 0)
                        {
                            item.TaskId = Id;
                            item.Status = HostedJobInfo.State.started;
                        }
                        else
                        {
                            //else job failed...we increment retries if we want to exit endless loop in case of failure.
                            if (ilDebug) _il.LogDebugInfo("Unexpected JobId failure when posting", null, Id);
                            await Task.Delay(TimeSpan.FromSeconds(5));
                        }
                    }
                    else
                    {
                        if (ilInfo) _il.LogInfo("no items waiting to be sent to API");
                        break;
                    }
                }
                #endregion
                #region MonitorJobsForCompletionAndRemove
                List<JobItem> jobsToBeRemoved = new List<JobItem>();
                foreach (var job in context.Value.Where(x => x.Status == HostedJobInfo.State.started || x.Status== HostedJobInfo.State.completedWithException ))
                {
                    try
                    {
                        var jobInfo = await api.GetJobInfo(job.TaskId);
                        if (jobInfo == null)
                        {
                            jobsToBeRemoved.Add(job);
                        }
                        else
                        {
                            if (jobInfo.Status == HostedJobInfo.State.complete && jobInfo.WhenCompleted < DateTime.Now.Subtract(TimeSpan.FromDays(1)))
                            {
                                if (jobInfo.WhenCompleted < DateTime.Now.Subtract(TimeSpan.FromDays(1)))
                                {
                                    jobsToBeRemoved.Add(job);
                                    await api.DeleteAsync(job.TaskId);
                                }
                                else
                                {
                                    job.Status = HostedJobInfo.State.complete;
                                }
                            }
                            else if (jobInfo.Status == HostedJobInfo.State.completedWithException)
                            {
                                if (job.Retries < 3)
                                {
                                    //retry the job.
                                    await api.DeleteAsync(job.TaskId);
                                    job.Status = HostedJobInfo.State.unstarted;
                                    job.Retries++;
                                    //log warn that it's failing
                                    if (ilWarn) _il.LogWarn($"Failed {job.Retries + 1} times", job.Source, jobInfo);
                                }
                                else if (jobInfo.WhenCompleted < DateTime.Now.Subtract(TimeSpan.FromDays(1)))
                                {
                                    if (ilError) _il.LogErr("Aborted", job.Source, job);
                                    jobsToBeRemoved.Add(job);
                                    await api.DeleteAsync(job.TaskId);
                                }
                                else
                                {
                                    job.Status = HostedJobInfo.State.completedWithException;
                                }
                            }
                            else
                            {
                                //still working on the job....
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        if (ilError) _il.LogErr("Error monitoringjobs and removing completed", null, ilDebug ? context.Value : null, ex);
                    }
                }
                foreach (var job in jobsToBeRemoved)
                {
                    //log that this failed and not complete.                        
                    context.Value.Remove(job);
                }
              
#if DEBUG
                await Task.Delay(TimeSpan.FromSeconds(10));
#else
 await Task.Delay(TimeSpan.FromSeconds(90));
#endif
                #endregion
            }
            context.Save();
            return true;
        }
    }
}
