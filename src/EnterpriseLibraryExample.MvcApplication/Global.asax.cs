﻿using System;
using System.Diagnostics;
using System.Web;
using System.Web.Mvc;
using System.Web.Routing;
using Autofac;
using Autofac.Extras.CommonServiceLocator;
using Autofac.Extras.EnterpriseLibraryConfigurator;
using Autofac.Integration.Mvc;
using Microsoft.Practices.EnterpriseLibrary.Common.Configuration;
using Microsoft.Practices.EnterpriseLibrary.ExceptionHandling;

namespace EnterpriseLibraryExample.MvcApplication
{
    /*
     * You will notice a lot of pinned package versions in this app. Enterprise Library 5 is very finicky about
     * which versions of Unity and CommonServiceLocator are in place. Package versions are pinned to avoid upgrades
     * that will break EntLib 5.
     *
     * The EnterpriseLibraryConfigurator is not required for EntLib 6.
     */

    public class MvcApplication : HttpApplication
    {
        public static void RegisterGlobalFilters(GlobalFilterCollection filters)
        {
            filters.Add(new HandleErrorAttribute());
        }

        public static void RegisterRoutes(RouteCollection routes)
        {
            routes.IgnoreRoute("{resource}.axd/{*pathInfo}");

            routes.MapRoute(
                "Default", // Route name
                "{controller}/{action}/{id}", // URL with parameters
                new { controller = "Home", action = "Index", id = UrlParameter.Optional } // Parameter defaults
            );

        }

        /// <summary>
        /// Handles appliction-level errors by passing them through the Enterprise
        /// Library exception handling block.
        /// </summary>
        protected void Application_Error(object sender, EventArgs e)
        {
            var originalException = Server.GetLastError();
            var exceptionManager = (ExceptionManager)null;
            try
            {
                exceptionManager = DependencyResolver.Current.GetService<ExceptionManager>();
            }
            catch (Exception ex)
            {
                // If we hit this it usually means there is a configuration
                // issue with the EntLib stuff in web.config.
                Trace.TraceError("An error occurred in resolving the Enterprise Library exception manager: " + ex.Message);
                return;
            }
            if (exceptionManager == null)
            {
                // If we hit this it means the Autofac.Extras.EnterpriseLibraryConfigurator.AutofacContainerConfigurator
                // didn't do its job and EntLib isn't registered properly.
                Trace.TraceError("The Enterprise Library Exception Handling block is not registered with the current Dependency Resolver. Check your Dependency Resolver registrations.");
                return;
            }

            // We have an EntLib ExceptionManager, so run it through the exception
            // handling policy outlined in web.config.
            var exceptionToThrow = (Exception)null;
            if (!exceptionManager.HandleException(originalException, "Global Web Exception Policy", out exceptionToThrow))
            {
                // In this case, the exception was considered handled (or, at
                // least, the exception manager isn't indicating anything should
                // be rethrown). Clear the error and call it good.
                Server.ClearError();
            }
            else if (HttpContext.Current != null && exceptionToThrow != null && exceptionToThrow != originalException)
            {
                // In this case, the exception is handled but we've been told to
                // rethrow. The best we can do is add it to the current context
                // since you can't switch exceptions mid-stream. If this bubbles
                // up to the UI, you'll still see the original exception and not
                // any wrapped exceptions.
                //
                // The exceptionToThrow will always be null unless the
                // postHandlingAction on the exception policy is set to
                // ThrowNewException. If you want to support any sort of exception
                // "wrapping," use ThrowNewException.
                //
                // Note we can only do this if there's an HttpContext to which
                // we can add the error. If there's not, any wrapped exception
                // info will totally be lost.
                HttpContext.Current.AddError(exceptionToThrow);
            }
        }

        protected void Application_Start()
        {
            // Enterprise Library configuration documentation can be found here:
            // https://autofac.readthedocs.io/en/latest/integration/entlib.html
            //
            // Register MVC-related dependencies.
            var builder = new ContainerBuilder();
            builder.RegisterControllers(typeof(MvcApplication).Assembly);
            builder.RegisterModelBinders(typeof(MvcApplication).Assembly);
            builder.RegisterModelBinderProvider();

            // Register the EntLib classes.
            builder.RegisterEnterpriseLibrary();

            // Set the MVC dependency resolver to use Autofac.
            var container = builder.Build();
            DependencyResolver.SetResolver(new AutofacDependencyResolver(container));

            // Set the EntLib service locator to use Autofac.
            var autofacLocator = new AutofacServiceLocator(container);
            EnterpriseLibraryContainer.Current = autofacLocator;

            // Finish initialization of MVC-related items.
            AreaRegistration.RegisterAllAreas();
            RegisterGlobalFilters(GlobalFilters.Filters);
            RegisterRoutes(RouteTable.Routes);
        }
    }
}