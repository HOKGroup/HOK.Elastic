
using HOK.NasuniAuditEventAPI.DAL;
using Microsoft.AspNetCore.Http.Extensions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace HOK.NasuniAuditEventAPI.Controllers
{
    public class AboutController : Controller
    {
        private readonly ILogger<AboutController> _logger;
        DAL.NasuniEventReader _eventReader;
        public AboutController(DAL.NasuniEventReader NasunieventStreamReader, ILogger<AboutController> logger)
        {
            _logger = logger;
            _eventReader = NasunieventStreamReader;
        }
        public IActionResult Index()
        {
            if (_logger.IsEnabled(LogLevel.Debug))
            {
                _logger.LogDebugInfo("About", Request.GetDisplayUrl(), _eventReader);
            }
            return View(_eventReader);
        }
    }
}