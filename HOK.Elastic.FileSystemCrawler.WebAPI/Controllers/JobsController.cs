using HOK.Elastic.DAL.Models;
using HOK.Elastic.FileSystemCrawler.Models;
using HOK.Elastic.FileSystemCrawler.WebAPI.DAL.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Extensions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace HOK.Elastic.FileSystemCrawler.WebAPI.Controllers
{
    [Authorize(Policy =AccessPolicy.PolicyNames.Default)]
    public class JobsController : Controller
    {
        private ILogger _logger;
        private readonly IHostedJobQueue _hostedJobScheduler;
        private bool isDebug;
        private bool isInfo;
        private bool isErr;
        public JobsController(ILogger<JobsController> logger, IHostedJobQueue hostedJobQueue)
        {
            _logger = logger;
            isDebug = _logger.IsEnabled(LogLevel.Debug);
            isInfo = _logger.IsEnabled(LogLevel.Information);
            isErr = _logger.IsEnabled(LogLevel.Error);
            _hostedJobScheduler = hostedJobQueue;
        }
        // GET: JobsViewController
        public ActionResult Index()
        {
            if (isDebug)
            {
                _logger.LogDebugInfo("Index", Request.GetDisplayUrl(), _hostedJobScheduler.Jobs);
            }
            var jobs = _hostedJobScheduler.Jobs;
            if (jobs != null)
            {
                if (isInfo) _logger.LogInformation($"Getting{jobs.Count()} jobs");
                return View(jobs.OrderBy(x=>x.Id).ToList());
            }
            else
            {
                if (isInfo) _logger.LogInformation($"Null jobs");
                return View();
            }
        }

        // GET: JobsViewController/Details/5
        public ActionResult Details(int id)
        {
            try
            {
                var job = _hostedJobScheduler.Get(id);
                return View(job);
            }
            catch (Exception ex)
            {
                if (isErr) _logger.LogError(ex, $"{nameof(Details)} requested id={id}");
                return NotFound(ex.Message);
            }            
        }

        // GET: JobsViewController/Create
        public ActionResult Create()
        {
            //provide some default suggestions....
            var defaults = new SettingsJobArgsDTO()
            {   BulkUploadSize = 5,
                CrawlMode = CrawlMode.EmailOnlyMissingContent,
                PublishedPath = "\\\\server\\one\\two\\three",
                //InputEvents = new List<InputPathEventStream>() { new InputPathEventStream() {Path="c:\\",PathFrom="b:\\" }, new InputPathEventStream() { Path = "d:\\", PathFrom = "e:\\" } }
                //InputPaths = new InputPathList() { 
                //    Crawls=new List<InputPathBase>() { new InputPathBase() {Office="TEST",Path="c:\\temp" } } ,
                //    Events = new List<InputPathEventStream>() { new InputPathEventStream() {Path="c:\\temp",PathFrom="c:\\windows" }, new InputPathEventStream() { Path = "c:\\temp4", PathFrom = "c:\\windows" } }                
                //},
               // InputEvent = new InputPathEventStream() { Path = "c:\\temp", PathFrom = "c:\\windows" }
            };
            return View(defaults);
        }

        // POST: JobsViewController/Create
        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult Create(SettingsJobArgsDTO settingsJobArgsDTO,string command)
        {
            if (command == null) command = "";
            try
            {
                if (ModelState.IsValid)
                {
                    if (command.Equals("addevent"))
                    {
                        if (settingsJobArgsDTO.InputEvents == null) settingsJobArgsDTO.InputEvents = new List<InputPathEventStream>();
                        settingsJobArgsDTO.InputEvents.Add(new InputPathEventStream() {Path=settingsJobArgsDTO.PublishedPath??"c:\\" });
                    } 
                    else if(command.Equals("addcrawl"))
                    {
                        if (settingsJobArgsDTO.InputCrawls == null) settingsJobArgsDTO.InputCrawls = new List<InputPathBase>();
                        settingsJobArgsDTO.InputCrawls.Add(new InputPathBase() { Path = settingsJobArgsDTO.PublishedPath ?? "c:\\" });
                    }else if(command.Equals("download"))
                    {
                        return Download(settingsJobArgsDTO);
                    }
                    else if (command.Equals("addelasticindex"))
                    {
                        if (settingsJobArgsDTO.ElasticIndexURI == null) settingsJobArgsDTO.ElasticIndexURI = new List<string>();
                        settingsJobArgsDTO.ElasticIndexURI.Add("server:5200");
                    }
                    else if (command.Equals("addelasticdiscovery"))
                    {
                        if (settingsJobArgsDTO.ElasticDiscoveryURI == null) settingsJobArgsDTO.ElasticDiscoveryURI = new List<string>();
                        settingsJobArgsDTO.ElasticDiscoveryURI.Add("server:5200");
                    }
                    else if (command.Equals("addelasticcrawl"))
                    {
                        if (settingsJobArgsDTO.ElasticIndexURI == null) settingsJobArgsDTO.ElasticIndexURI = new List<string>();
                        settingsJobArgsDTO.ElasticIndexURI.Add("server:5200");
                    }
                    else
                    {
                        JobHelper.Post(_hostedJobScheduler, settingsJobArgsDTO, Request.HttpContext.Connection.RemoteIpAddress.MapToIPv4().ToString());
                        return RedirectToAction(nameof(Index));
                    }                   
                }               
            }
            catch(Exception ex)
            {
                if (isErr) _logger.LogError(ex, $"{nameof(Create)} attempted to create failed.");
                throw;
            }
            return View(settingsJobArgsDTO);
        }

        // GET: JobsViewController/Details/5
        public ActionResult Cancel(int id)
        {
            return View(_hostedJobScheduler.Get(id));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult Cancel(int id, HostedJobInfo hostedJobInfo)
        {
            try
            {
                hostedJobInfo = _hostedJobScheduler.Get(hostedJobInfo.Id);
                if (hostedJobInfo != null)
                {
                    if (!hostedJobInfo.IsCompleted)
                    {
                        hostedJobInfo.Cancel();
                        if (isInfo) _logger.LogInformation($"Canceled Id:{hostedJobInfo.Id} JobName:{hostedJobInfo.SettingsJobArgsDTO.JobName}");
                    }                   
                }
                return RedirectToAction(nameof(Index));
            }
            catch (Exception ex)
            {
                if (isErr) _logger.LogError(ex, "Error cancelling..");
                return View(hostedJobInfo);
            }
        }

        // GET: JobsViewController/Delete/5
        public ActionResult Delete(int id)
        {   
            return View(_hostedJobScheduler.Get(id));
        }

        // POST: JobsViewController/Delete/5
        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult Delete(int id, HostedJobInfo hostedJobInfo)
        {
            try
            {
                hostedJobInfo = _hostedJobScheduler.Get(hostedJobInfo.Id);
                if (hostedJobInfo != null)
                {
                    //if (!hostedJobInfo.IsCompleted)
                    //{
                        var removed = _hostedJobScheduler.Remove(id);
                        if (isInfo) _logger.LogInformation("Removed" + removed);
                    //}
                }
                return RedirectToAction(nameof(Index));
            }
            catch
            {                
                return View(hostedJobInfo);
            }
        }



        [HttpGet]
        public IActionResult Download(SettingsJobArgsDTO settingsJobArgsDTO=default, int? id=default)
        {
            //either download job by passing settingjobargsdto or the id of the job.
            if(id!=default)
            {
                var job = _hostedJobScheduler.Get(id.Value);
                settingsJobArgsDTO = job.SettingsJobArgsDTO;
            }
            var filename = settingsJobArgsDTO.JobName;
            if(string.IsNullOrEmpty(filename))
            {
                filename = "jobsettings";
            }
            HttpContext.Response.Headers.Add("Content-Disposition", new System.Net.Mime.ContentDisposition { FileName = $"{filename}.json", Inline = false }.ToString());
            return new JsonResult(settingsJobArgsDTO, new System.Text.Json.JsonSerializerOptions() { WriteIndented = true, IgnoreReadOnlyProperties = true, IgnoreReadOnlyFields = true });
        }

        [HttpPost]
        public async Task<ActionResult> Upload(IFormFile file)
        {
            try
            {
                if (file!=null && file.Length > 0 && file.Length < 999000)
                {
                    using (var reader = new StreamReader(file.OpenReadStream()))
                    {
                        var content = await reader.ReadToEndAsync();
                        var json = JsonConvert.DeserializeObject<SettingsJobArgsDTO>(content);
                        if (json != null)
                        {
                            return View("Create", json);
                        }
                    }
                }                  
            }
            catch (Exception ex)
            {
                throw new Exception("Upload Failed", ex);
            }
            return RedirectToAction("Create");
        }
    }
}
