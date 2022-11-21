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
using Microsoft.Extensions.Hosting;


namespace HOK.Elastic.FileSystemCrawler.WebAPI.Controllers
{
    [Authorize(Policy = AccessPolicy.PolicyNames.Default)]
    [ApiController]
    [Route("[controller]")]
    public class JobsAPIController : Controller
    {

        private ILogger _logger;
        private readonly IHostedJobQueue _hostedJobScheduler;
        private bool isDebug;
        private bool isInfo;
        public JobsAPIController(ILogger<JobsAPIController> logger, IHostedJobQueue hostedJobQueue)
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
                if (isInfo) _logger.LogInformation($"Null job collection");
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


        /// <summary>
        /// pass any of the crawler jobs config definitions (full,incrmeental,event) possibly missing content and query crawl too.
        /// </summary>
        /// <param name="settingsJobArgsDto"></param>
        /// <returns></returns>
        [HttpPost, HttpPut]
        public ActionResult Post(SettingsJobArgsDTO settingsJobArgsDto)
        {
            var id = JobHelper.Post(_hostedJobScheduler, settingsJobArgsDto, Request.HttpContext.Connection.RemoteIpAddress.MapToIPv4().ToString());
            return Ok(id);
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
