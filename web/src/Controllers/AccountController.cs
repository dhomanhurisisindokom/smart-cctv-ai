using System.Web.Mvc;
using MiniWeb.Data;
using MiniWeb.Models;

namespace MiniWeb.Controllers
{
    public class AccountController : Controller
    {
        [HttpGet]
        public ActionResult Login(string returnUrl)
        {
            if (Session[BaseController.SessionKey] != null) return RedirectToAction("Index", "Home");
            ViewBag.ReturnUrl = returnUrl;
            return View();
        }

        [HttpPost, ValidateAntiForgeryToken]
        public ActionResult Login(string username, string password, string returnUrl)
        {
            var user = Users.ByUsername((username ?? "").Trim());
            if (user == null || !user.IsActive || !PasswordHasher.Verify(password, user.PasswordHash))
            {
                ViewBag.ReturnUrl = returnUrl;
                ViewBag.Username = username;
                ViewBag.Error = "Username atau kata sandi salah, atau akun nonaktif.";
                return View();
            }
            Users.TouchLogin(user.Id);
            Session.Clear();
            Session[BaseController.SessionKey] = new SessionUser { Id = user.Id, Username = user.Username, FullName = user.FullName, Role = user.Role };
            if (!string.IsNullOrEmpty(returnUrl) && Url.IsLocalUrl(returnUrl)) return Redirect(returnUrl);
            return RedirectToAction("Index", "Home");
        }

        [HttpPost, ValidateAntiForgeryToken]
        public ActionResult Logout()
        {
            Session.Clear();
            Session.Abandon();
            return RedirectToAction("Login");
        }
    }
}
