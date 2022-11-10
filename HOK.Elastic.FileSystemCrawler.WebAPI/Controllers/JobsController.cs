using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using System.Linq;
using HOK.Elastic.DAL.Models;
using System.Configuration;
using System.Management;
using HOK.Elastic.FileSystemCrawler.Models;
using Microsoft.AspNetCore.Http.Extensions;
using System;
using HOK.Elastic.FileSystemCrawler.WebAPI.Models;

namespace HOK.Elastic.FileSystemCrawler.WebAPI.Controllers
{
    [Authorize]
    [ApiController]
    [Route("[controller]")]
    public class JobsController : Controller
    {

        private ILogger _logger;
        private readonly IHostedJobQueue _hostedJobScheduler;
        private bool isDebug;
        private bool isInfo;
        public JobsController(ILogger<JobsController> logger, IHostedJobQueue hostedJobQueue)
        {
            _logger = logger;
            isDebug = _logger.IsEnabled(LogLevel.Debug);
            isInfo = _logger.IsEnabled(LogLevel.Information);
            _hostedJobScheduler = hostedJobQueue;
        }
       
        public ActionResult Get()
        {
            var jobs = _hostedJobScheduler.Jobs;
            if (jobs != null)
            {
                if (isInfo) _logger.LogInformation($"Getting{jobs.Count()} jobs");
                return Ok(jobs.ToList());
            }
            else
            {
                if (isInfo) _logger.LogInformation($"Null jobs");
                return NotFound();
            }
        }    


        [HttpGet("{id:int}")]
        public ActionResult Get(int Id)
        {
            try
            {
                var job = _hostedJobScheduler.Get(Id);
                return Ok(job);
            }
            catch(Exception ex)
            {
                return NotFound(ex.Message);
            }
        }
        [HttpPost, HttpPut]

        /// <summary>
        /// pass any of the crawler jobs config definitions (full,incrmeental,event) possibly missing content and query crawl too.
        /// </summary>
        /// <param name="settingsJobArgs"></param>
        /// <returns></returns>
        
        public ActionResult Post(SettingsJobArgsDTO settingsJobArgsdto)
        {             
            var settingsJobArgs = settingsJobArgsdto as SettingsJobArgs;
            if(settingsJobArgs.CrawlMode==CrawlMode.EventBased)//TODO hopefully we can refactor this.
            {
                var inputPaths = new InputPathCollectionEventStream() { };
                foreach(var i in settingsJobArgsdto.InputPaths.Events)
                {
                    inputPaths.Add(i);
                }
                settingsJobArgs.InputPaths = inputPaths;
            }
            else
            {
                var inputPaths = new InputPathCollectionBase() { };
                foreach (var i in settingsJobArgsdto.InputPaths.Crawls)
                {
                    inputPaths.Add(i);
                }
                settingsJobArgs.InputPaths = inputPaths;
            }
           
            var jobId = _hostedJobScheduler.Enqueue(settingsJobArgs);
            return Ok(jobId);
        }

        [HttpDelete("{id:int}")]
        public ActionResult Delete(int Id)
        {
            return Ok(_hostedJobScheduler.Remove(Id));
        }
        [HttpGet("FreeSlots")]
        public ActionResult FreeSlots()
        {
            return Ok(_hostedJobScheduler.FreeSlots);
        }
    }
}
