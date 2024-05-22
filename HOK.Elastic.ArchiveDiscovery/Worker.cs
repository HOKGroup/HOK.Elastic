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
using HOK.Elastic.FileSystemCrawler.WebAPI.Models;
using Microsoft.Extensions.Logging;
using NLog;
using NLog.Extensions.Logging;


namespace HOK.Elastic.ArchiveDiscovery
{
    internal class Worker
    {
        private Repository<List<JobItem>> context = new Repository<List<JobItem>>();
        private static readonly NLog.Logger _il = NLog.LogManager.GetCurrentClassLogger();
        private ILoggerFactory loggerFactory;
        private bool ilDebug;
        private bool ilInfo;
        private bool ilWarn;
        private bool ilError;
        private APIClient api;
        public int ProjectCount { get; set; } = 0;
        public int ProjectCompletedCount { get; set; } = 0;
        public Worker(string apihost)
        {
            loggerFactory = LoggerFactory.Create(builder => builder.AddNLog());
            ilDebug = _il.IsDebugEnabled;
            ilInfo = _il.IsInfoEnabled;
            ilWarn = _il.IsWarnEnabled;
            ilError = _il.IsErrorEnabled;
            api = new APIClient(apihost, loggerFactory.CreateLogger("api"));
        }

        internal async Task RunAsync(SettingsJobArgsDTO settingsJobArgsDTO,string pathPrefix,string pathProdSuffix,string pathArchiveSuffix,Regex officeMatch=null)
        {
            IndexNameHelper indexNameHelper = new IndexNameHelper(settingsJobArgsDTO.IndexNamePrefix);
            PipeLineNameHelper pipeLineNameHelper = new PipeLineNameHelper(settingsJobArgsDTO.IndexNamePrefix);
            var discoveryuris = settingsJobArgsDTO.ElasticDiscoveryURI.Select(x => new Uri(x)).ToList();
            DiscoveryArchiveRecrawl discoveryArchive = new DiscoveryArchiveRecrawl(pipeLineNameHelper, indexNameHelper, discoveryuris, loggerFactory.CreateLogger(nameof(Worker)));
            var clientStatus = discoveryArchive.GetClientStatus();
            if (ilDebug) _il.Debug("Status", null, clientStatus);
            var offices = (await discoveryArchive.FindOffices());
            if(offices != null&&officeMatch!=null) { offices = offices.Where(x => officeMatch.IsMatch(x)); }
            if (offices != null && offices.Any())
            {
                
                foreach (var office in offices)
                {
                    if (ilInfo) _il.Info($">>>Searching: '{office}'", null, null);

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
                                if (ilInfo) _il.Info($">>>Found matching pair PROD>ARCHIVE WorkItem", null, workItem);
                                context.Value.Add(workItem);
                            }
                        }
                    }
                    else
                    {
                        if (ilDebug) _il.Debug($"No projects found for: '{office}'", null, null);
                    }
                }
            }
            else
            {
                if (ilInfo) _il.Info("No offices" + officeMatch !=null? " matched " + officeMatch.ToString():" found that matched");
            }
            try
            {
                await CopyToArchive(settingsJobArgsDTO);
            }
            catch (Exception e)
            {
                if (_il.IsFatalEnabled) _il.Fatal("Fatal", null, e);
            }
        }

        private async Task<bool> CopyToArchive(SettingsJobArgsDTO settingsJobArgsDTO)
        {
            DateTime timer = DateTime.MinValue;
            ProjectCount = context.Value.Count;
            while (context.Value.Any())
            {
                if (ilInfo) _il.Info($"Looping {ProjectCount} jobs in context...with {ProjectCompletedCount} completed.", null, null);
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
                            item.Status = HostedJobInfo.State.queued;
                            ProjectCompletedCount++;
                        }
                        else
                        {
                            //else job failed...we increment retries if we want to exit endless loop in case of failure.
                            if (ilDebug) _il.Debug("Unexpected JobId failure when posting", null, Id);
                            await Task.Delay(TimeSpan.FromSeconds(5));
                        }
                    }
                    else
                    {
                        if (ilInfo) _il.Info("no items waiting to be sent to API");
                        break;
                    }
                }
                #endregion
                #region MonitorJobsForCompletionAndRemove
                List<JobItem> jobsToBeRemoved = new List<JobItem>();
                foreach (var job in context.Value.Where(x => x.Status > HostedJobInfo.State.unstarted ))
                {
                    DateTime maxAge = DateTime.Now.Subtract(TimeSpan.FromHours(16));
                    try
                    {
                        var jobInfo = await api.GetJobInfo(job.TaskId);
                        if (jobInfo == null)
                        {
                            jobsToBeRemoved.Add(job);//remove jobs that aren't available(deleted) on the webapi anymore 
                        }
                        else
                        {
                            if (jobInfo.Status == HostedJobInfo.State.complete|| jobInfo.Status == HostedJobInfo.State.cancelled)
                            {
                                if (jobInfo.WhenCompleted!=null && jobInfo.WhenCompleted>DateTime.MinValue && jobInfo.WhenCompleted < maxAge)
                                {
                                    jobsToBeRemoved.Add(job);
                                    await api.DeleteAsync(job.TaskId);
                                }
                                else
                                {
                                    job.Status = jobInfo.Status;
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
                                    if (ilWarn) _il.Warn($"Failed {job.Retries + 1} times", job.Source, jobInfo);
                                }
                                else if (jobInfo.WhenCompleted == null|| jobInfo.WhenCompleted==DateTime.MinValue|| jobInfo.WhenCompleted < maxAge)
                                {
                                    if (ilError) _il.Error("Aborted", job.Source, job);
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
                        if (ilError) _il.Error("Error monitoringjobs and removing completed", null, ilDebug ? context.Value : null, ex);
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
 await Task.Delay(TimeSpan.FromSeconds(20));
#endif
                #endregion
            }
            context.Save();
            return true;
        }
    }
}
