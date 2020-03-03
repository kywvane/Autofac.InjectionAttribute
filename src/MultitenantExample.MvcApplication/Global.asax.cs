using System.ServiceModel;
using System.Web;
using System.Web.Mvc;
using System.Web.Routing;
using Autofac;
using Autofac.Integration.Mvc;
using Autofac.Multitenant;
using Autofac.Multitenant.Wcf;
using MultitenantExample.MvcApplication.Controllers;
using MultitenantExample.MvcApplication.Dependencies;
using MultitenantExample.MvcApplication.WcfMetadataConsumer;
using MultitenantExample.MvcApplication.WcfService;

namespace MultitenantExample.MvcApplication
{
    /// <summary>
    /// Global application class for the multitenant MVC example application.
    /// </summary>
    public class MvcApplication : HttpApplication
    {
        /// <summary>
        /// Registers the application routes with a route collection.
        /// </summary>
        /// <param name="routes">
        /// The route collection with which to register routes.
        /// </param>
        /// <remarks>
        /// <para>
        /// This is part of standard MVC application setup.
        /// </para>
        /// </remarks>
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
        /// Handles the global application startup event.
        /// </summary>
        protected void Application_Start()
        {
            // Register application-level dependencies and controllers. Note that
            // we are manually registering controllers rather than all at the same
            // time because some of the controllers in this sample application
            // are for specific tenants.
            var builder = new ContainerBuilder();
            builder.RegisterType<HomeController>();
            builder.RegisterType<BaseDependency>().As<IDependency>();

            // Create the tenant ID strategy. Required for multitenant integration.
            var tenantIdStrategy = new RequestParameterStrategy();

            // Adding the tenant ID strategy into the container so controllers
            // can display output about the current tenant.
            builder.RegisterInstance(tenantIdStrategy).As<ITenantIdentificationStrategy>();

            // The next couple of registrations - for the channel factory and channel
            // to WCF services - show how to consume multitenant WCF services.

            // The service client is not different per tenant because
            // the service itself is multitenant - one client for all
            // the tenants and the service implementation switches.
            builder.Register(c => new ChannelFactory<IMultitenantService>(new BasicHttpBinding(), new EndpointAddress("http://localhost:63578/MultitenantService.svc"))).SingleInstance();
            builder.Register(c => new ChannelFactory<IMetadataConsumer>(new WSHttpBinding(), new EndpointAddress("http://localhost:63578/MetadataConsumer.svc"))).SingleInstance();

            // Register an endpoint behavior on the client channel factory that
            // will propagate the tenant ID across the wire in a message header.
            // On the service side, you'll need to read the header from incoming
            // message headers to reconstitute the incoming tenant ID.
            builder.Register(c =>
            {
                var factory = c.Resolve<ChannelFactory<IMultitenantService>>();
                factory.Opening += (sender, args) => factory.Endpoint.Behaviors.Add(new TenantPropagationBehavior<string>(tenantIdStrategy));
                return factory.CreateChannel();
            }).InstancePerRequest();
            builder.Register(c =>
            {
                var factory = c.Resolve<ChannelFactory<IMetadataConsumer>>();
                factory.Opening += (sender, args) => factory.Endpoint.Behaviors.Add(new TenantPropagationBehavior<string>(tenantIdStrategy));
                return factory.CreateChannel();
            }).InstancePerRequest();

            // Create the multitenant container based on the application
            // defaults - here's where the multitenant bits truly come into play.
            var mtc = new MultitenantContainer(tenantIdStrategy, builder.Build());

            // Notice we configure tenant IDs as strings below because the tenant
            // identification strategy retrieves string values from the request
            // context. To use strongly-typed tenant IDs, create a custom tenant
            // identification strategy that returns the appropriate type.

            // Configure overrides for tenant 1 - dependencies, controllers, etc.
            mtc.ConfigureTenant("1",
                b =>
                {
                    b.RegisterType<Tenant1Dependency>().As<IDependency>().InstancePerDependency();
                    b.RegisterType<Tenant1Controller>().As<HomeController>();
                });

            // Configure overrides for tenant 2 - dependencies, controllers, etc.
            mtc.ConfigureTenant("2",
                b =>
                {
                    b.RegisterType<Tenant2Dependency>().As<IDependency>().SingleInstance();
                    b.RegisterType<Tenant2Controller>().As<HomeController>();
                });

            // Configure overrides for the default tenant. That means the default
            // tenant will have some different dependencies than other unconfigured
            // tenants.
            mtc.ConfigureTenant(null, b => b.RegisterType<DefaultTenantDependency>().As<IDependency>().SingleInstance());

            // Create the dependency resolver using the
            // multitenant container instead of the application container.
            DependencyResolver.SetResolver(new AutofacDependencyResolver(mtc));

            // Perform the standard MVC setup requirements.
            AreaRegistration.RegisterAllAreas();
            RegisterRoutes(RouteTable.Routes);
        }
    }
}