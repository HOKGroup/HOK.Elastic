using HOK.Elastic.FileSystemCrawler.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Extensions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using System;
using System.Linq;

namespace HOK.Elastic.FileSystemCrawler.WebAPI.Controllers
{
    public class JobsViewController : Controller
    {
        private ILogger _logger;
        private readonly IHostedJobQueue _hostedJobScheduler;
        private bool isDebug;
        private bool isInfo;
        private bool isErr;
        public JobsViewController(ILogger<JobsController> logger, IHostedJobQueue hostedJobQueue)
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
                return View(jobs.ToList());
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

            var defaults = new SettingsJobArgs() 
            { BulkUploadSize = 5, 
                CrawlMode = CrawlMode.EmailOnlyMissingContent 
            };
            return View(defaults);
        }

        // POST: JobsViewController/Create
        [HttpPost]
        [ValidateAntiForgeryToken]
            public ActionResult Create(SettingsJobArgs settingsJobArgs)
        {
            try
            {
                if(ModelState.IsValid)
                {
                    var t = _hostedJobScheduler.Enqueue(settingsJobArgs);
                    return RedirectToAction(nameof(Index));
                }
                return View();
            }
            catch(Exception ex)
            {
                if (isErr) _logger.LogError(ex, $"{nameof(Create)} attempted to create failed.");
                return View();
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
                if(!hostedJobInfo.IsCompleted)
                {
                  var removed = _hostedJobScheduler.Remove(id);
                    if (isInfo) _logger.LogInformation("Removed" + removed);
                }
                return RedirectToAction(nameof(Index));
            }
            catch
            {                
                return View();
            }
        }


        [HttpGet]        
        public IActionResult Download(int id)
        {
            var job = _hostedJobScheduler.Get(id);
            var settingsJobArgs = job.SettingsJobArgs;
            HttpContext.Response.Headers.Add("Content-Disposition", new System.Net.Mime.ContentDisposition{FileName = $"job{job.Id}settings.json",Inline = false}.ToString());
            return new JsonResult(settingsJobArgs,new System.Text.Json.JsonSerializerOptions() { WriteIndented=true,IgnoreReadOnlyProperties=true,IgnoreReadOnlyFields=true});
        }
    }
}
