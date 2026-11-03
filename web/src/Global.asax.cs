using System.Web.Mvc;
using System.Web.Routing;

namespace MiniWeb
{
    public class MvcApplication : System.Web.HttpApplication
    {
        protected void Application_Start()
        {
            System.Net.ServicePointManager.DefaultConnectionLimit = 32;
            MvcHandler.DisableMvcResponseHeader = true;
            ViewEngines.Engines.Clear();
            ViewEngines.Engines.Add(new RazorViewEngine());

            var routes = RouteTable.Routes;
            routes.IgnoreRoute("Content/{*file}");
            // Endpoints called by the AI service (fastapi/).
            routes.MapRoute("ApiCameras", "api/cameras", new { controller = "Ingest", action = "Cameras" });
            routes.MapRoute("ApiBootstrap", "api/cameras/bootstrap", new { controller = "Ingest", action = "Bootstrap" });
            routes.MapRoute("ApiIngest", "api/ingest", new { controller = "Ingest", action = "Ingest" });
            routes.MapRoute("ApiHealth", "api/health", new { controller = "Ingest", action = "Health" });
            routes.MapRoute("Default", "{controller}/{action}/{id}",
                new { controller = "Home", action = "Index", id = UrlParameter.Optional });
        }
    }
}
