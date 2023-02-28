using HOK.Elastic.FileSystemCrawler.WebAPI.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Nest;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;

namespace HOK.Elastic.FileSystemCrawler.WebAPI.Controllers
{
    [Authorize(Policy = AccessPolicy.PolicyNames.Default)]
    public class HomeController : Controller
    {
        private readonly ILogger<HomeController> _logger;
        private readonly IHostedJobQueue _hostedJobScheduler;

        public HomeController(ILogger<HomeController> logger,IHostedJobQueue hostedJobQueue)
        {
            _logger = logger;
            _hostedJobScheduler= hostedJobQueue;
        }

        public IActionResult Index()
        {
            return View(_hostedJobScheduler);
        }

        public IActionResult Privacy()
        {
            return View();
        }

        [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
        public IActionResult Error()
        {
            return View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
        }
    }
}
