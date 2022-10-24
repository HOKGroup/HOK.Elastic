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
                return new OkObjectResult(jobs.ToList());
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
                return new OkObjectResult(job);
            }
            catch(Exception ex)
            {
                return NotFound(ex.Message);
            }
        }
        [HttpPut]

        /// <summary>
        /// pass any of the crawler jobs config definitions (full,incrmeental,event) possibly missing content and query crawl too.
        /// </summary>
        /// <param name="settingsJobArgs"></param>
        /// <returns></returns>
        
        public ActionResult Post(ISettingsJobArgs settingsJobArgs)
        {
            var index = new DAL.Index(settingsJobArgs.ElasticIndexURI.First(), new Elastic.Logger.Log4NetLogger("index"));
            var discovery = new DAL.Discovery(settingsJobArgs.ElasticDiscoveryURI.First(), new Elastic.Logger.Log4NetLogger("discovery"));
            SecurityHelper sh = new SecurityHelper(new Elastic.Logger.Log4NetLogger("test"));
            DocumentHelper dh = new DocumentHelper(true, sh, index, new Elastic.Logger.Log4NetLogger("dh"));
            WorkerEventStream ws = new WorkerEventStream(index, discovery, sh, dh, new Elastic.Logger.Log4NetLogger("ws"));
            // var taskID = _hostedJobScheduler.Enqueue(System.Threading.Tasks.Task<JobDetails>.Run(async () => { return new JobDetails(55, async () => { await ws.RunAsync(settingsJobArgs);}).Hello);
            //return new OkObjectResult(taskID);//use this Identifier to get status of running jobs(s) again later.;
            return new OkObjectResult("hmm");
        }
    }
}
