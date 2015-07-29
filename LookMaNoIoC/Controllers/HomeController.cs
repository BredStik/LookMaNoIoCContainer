using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Web;
using System.Web.Mvc;

namespace LookMaNoIoC.Controllers
{
    public class HomeController : Controller
    {
        private readonly IMediator _mediator;

        public HomeController(IMediator mediator)
        {
            _mediator = mediator;
        }
        
        public ActionResult Index()
        {
            return View();
        }

        public async Task<ActionResult> About()
        {
            var message = await _mediator.Handle(new GetAboutMessageCommand());

            ViewBag.Message = message;

            return View();
        }

        public ActionResult Contact()
        {
            ViewBag.Message = "Your contact page.";

            return View();
        }
    }
}