using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace HOK.Elastic.FileSystemCrawler.WebAPI.Controllers
{
    public class SettingsJobArgsDTOController : Controller
    {
        // GET: SettingsArgsJobDTO
        public ActionResult Index()
        {
            return View();
        }

        // GET: SettingsArgsJobDTO/Details/5
        public ActionResult Details(int id)
        {
            return View();
        }

        // GET: SettingsArgsJobDTO/Create
        public ActionResult Create()
        {
            return View();
        }

        // POST: SettingsArgsJobDTO/Create
        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult Create(IFormCollection collection)
        {
            try
            {
                return RedirectToAction(nameof(Index));
            }
            catch
            {
                return View();
            }
        }

        // GET: SettingsArgsJobDTO/Edit/5
        public ActionResult Edit(int id)
        {
            return View();
        }

        // POST: SettingsArgsJobDTO/Edit/5
        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult Edit(int id, IFormCollection collection)
        {
            try
            {
                return RedirectToAction(nameof(Index));
            }
            catch
            {
                return View();
            }
        }

        // GET: SettingsArgsJobDTO/Delete/5
        public ActionResult Delete(int id)
        {
            return View();
        }

        // POST: SettingsArgsJobDTO/Delete/5
        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult Delete(int id, IFormCollection collection)
        {
            try
            {
                return RedirectToAction(nameof(Index));
            }
            catch
            {
                return View();
            }
        }
    }
}
