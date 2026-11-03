using System;
using System.Web.Mvc;
using MiniWeb.Data;
using MiniWeb.Models;

namespace MiniWeb.Controllers
{
    // Every page requires a signed-in user; Admin-only controllers set AdminOnly.
    public abstract class BaseController : Controller
    {
        public const string SessionKey = "User";

        protected virtual bool AdminOnly { get { return false; } }

        protected SessionUser CurrentUser { get { return Session[SessionKey] as SessionUser; } }

        protected override void OnActionExecuting(ActionExecutingContext context)
        {
            var user = CurrentUser;
            if (user == null)
            {
                context.Result = Request.IsAjaxRequest()
                    ? (ActionResult)new HttpStatusCodeResult(401)
                    : RedirectToAction("Login", "Account", new { returnUrl = Request.RawUrl });
                return;
            }
            if (AdminOnly && !user.IsAdmin)
            {
                context.Result = new HttpStatusCodeResult(403, "Khusus admin");
                return;
            }
            ViewBag.User = user;
            base.OnActionExecuting(context);
        }

        protected void Flash(string message, bool ok = true)
        {
            TempData["Flash"] = message;
            TempData["FlashOk"] = ok;
        }

        // Signed AI stream access for this page view; the browser talks to FastAPI directly.
        protected void SetAiAccess()
        {
            ViewBag.AiUrl = Config.AiPublicUrl;
            ViewBag.AiPort = Config.AiPublicPort;
            ViewBag.AiToken = StreamToken.Create(TimeSpan.FromHours(12));
        }

        protected static DateTime? ParseDate(string value)
        {
            DateTime d;
            return DateTime.TryParseExact(value ?? "", "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.None, out d) ? d : (DateTime?)null;
        }
    }
}
