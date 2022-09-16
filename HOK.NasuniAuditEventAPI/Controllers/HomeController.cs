using Microsoft.AspNetCore.Mvc;

namespace HOK.NasuniAuditEventAPI.Controllers
{
    public class HomeController : Controller
    {
        public IActionResult Index()
        {
            return View();
        }
    }
}
