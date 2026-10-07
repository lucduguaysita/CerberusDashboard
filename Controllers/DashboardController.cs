using Microsoft.AspNetCore.Mvc;
using CerberusDashboard.Services;
using CerberusDashboard.Hubs;
using Microsoft.AspNetCore.SignalR;
using System;
using System.Threading.Tasks;

namespace CerberusDashboard.Controllers
{
    public class DashboardController : Controller
    {
        private readonly EstateManager _estates;
        private readonly IHubContext<MonitorHub> _hub;

        public DashboardController(EstateManager estates, IHubContext<MonitorHub> hub)
        {
            _estates = estates;
            _hub = hub;
        }

        public IActionResult Index()
        {
            ViewBag.RefreshSeconds = _estates.Active.Monitor.RefreshSeconds.ToString();
            return View();
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> SelectEstate(string estateKey, long revision, string sessionId)
        {
            if (!ModelState.IsValid) return BadRequest();
            try
            {
                _estates.Select(estateKey, revision, sessionId);
            }
            catch (ArgumentException)
            {
                return BadRequest();
            }
            catch (InvalidOperationException)
            {
                return Conflict();
            }

            await _hub.Clients.All.SendAsync("receiveEstateState", _estates.GetState());
            return NoContent();
        }
    }
}
