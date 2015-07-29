using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Security;
using System.Security.Permissions;
using System.Threading.Tasks;
using System.Web;
using System.Web.Mvc;
using System.Web.Optimization;
using System.Web.Routing;
using System.Web.Security;

namespace LookMaNoIoC
{
    public class CustomControllerFactory: DefaultControllerFactory
    {
        private readonly Container _container;

        public CustomControllerFactory(Container container)
        {
            _container = container;
        }

        protected override IController GetControllerInstance(RequestContext requestContext, Type controllerType)
        {
            return _container.Resolve(controllerType) as IController;
        }
    }

    public class MvcApplication : System.Web.HttpApplication
    {
        private static Container _container = new Container();                

        protected void Application_Start()
        {
            AreaRegistration.RegisterAllAreas();
            FilterConfig.RegisterGlobalFilters(GlobalFilters.Filters);
            RouteConfig.RegisterRoutes(RouteTable.Routes);
            BundleConfig.RegisterBundles(BundleTable.Bundles);
            ControllerBuilder.Current.SetControllerFactory(new CustomControllerFactory(_container)); 

            

            //find all async handlers
            //RegisterAllAsyncHandlers();

            //Register<IService, Service>(true);
            //_container.Register<IAsyncRequestHandler<GetAboutMessageCommand, string>>(() => new GetAboutMessageCommandHandler())
            //    .DecorateWith(typeof(LoggerDecoratorHandler<,>));
                //.DecorateWith(typeof(AuthorizationDecoratorHandler<,>));
            BootstrapContainer();

            //_handlers.Add(typeof(GetAboutMessageCommand), c => CommandHandlers.GetAboutMessageHandler(new StringWriter(), c));
            
        }

        private static void BootstrapContainer()
        {
            //_container.Register(typeof(IAsyncRequestHandler<,>), () => typeof(GenericAsyncRequestHandler<,>)).DecorateWith(typeof(LoggerDecoratorHandler<,>));
            _container.Register<Container>(() => _container);
            _container.Register<IMediator>(() => new Mediator(t => _container.Resolve(t)), Lifestyle.Singleton);
            _container.Register<IService, Service>(Lifestyle.PerWebRequest);
            _container.Register<Action<string>>(() => (x => Debug.WriteLine(x)));
            RegisterAllAsyncHandlers();
            var allControllers = Assembly.GetExecutingAssembly().GetExportedTypes().Where(x => typeof(IController).IsAssignableFrom(x));

            foreach (var controller in allControllers)
            {
                _container.Register(controller, controller, Lifestyle.PerWebRequest);
            }
        }

        private static void RegisterAllAsyncHandlers()
        {
            var allAsyncRequestHandlers = Assembly.GetExecutingAssembly().GetExportedTypes()
                .Where(x => !x.IsInterface && !x.IsGenericTypeDefinition && x.GetInterfaces().Any(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IAsyncRequestHandler<,>)));

            var decoratorAttributeTypes = new[] { typeof(AuthorizeAttribute), typeof(LogAttribute) };


            foreach (var asyncRequestHandlerType in allAsyncRequestHandlers)
            {
                var genericArgs = asyncRequestHandlerType.GetInterfaces().First(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IAsyncRequestHandler<,>)).GetGenericArguments();
                var serviceType = typeof(IAsyncRequestHandler<,>).MakeGenericType(genericArgs);

                var registration = _container.Register(serviceType, asyncRequestHandlerType, Lifestyle.PerWebRequest);

                var decoratorAttributes = asyncRequestHandlerType.GetMethod("Handle").CustomAttributes.Where(x => decoratorAttributeTypes.Contains(x.AttributeType)).Select(x => x.AttributeType);

                foreach (var decoratorAttribute in decoratorAttributes)
                {
                    if (decoratorAttribute == typeof(AuthorizeAttribute))
                    {
                        registration.DecorateWith(typeof(AuthorizationDecoratorHandler<,>));
                        continue;
                    }

                    if (decoratorAttribute == typeof(LogAttribute))
                    {
                        registration.DecorateWith(typeof(LoggerDecoratorHandler<,>));
                        continue;
                    }
                }
            }
        }
    }

    public class LoggerDecoratorHandler<TRequest, TResult> : IAsyncDecoratorHandler<TRequest, TResult> where TRequest : IAsyncRequest<TResult>
    {
        private readonly IAsyncRequestHandler<TRequest, TResult> _innerHandler;
        private readonly Action<string> _logger;

        public LoggerDecoratorHandler(IAsyncRequestHandler<TRequest, TResult> innerHandler, Action<string> logger)
        {
            _innerHandler = innerHandler;
            _logger = logger;
        }

        public async Task<TResult> Handle(TRequest request)
        {
            _logger("Before handling request of type " + typeof(TRequest).Name);

            var result = await _innerHandler.Handle(request);

            _logger("After request was handled");

            return result;
        }
    }

    public class AuthorizationDecoratorHandler<TRequest, TResult> : IAsyncDecoratorHandler<TRequest, TResult> where TRequest : IAsyncRequest<TResult>
    {
        private readonly IAsyncRequestHandler<TRequest, TResult> _innerHandler;

        public AuthorizationDecoratorHandler(IAsyncRequestHandler<TRequest, TResult> innerHandler)
        {
            _innerHandler = innerHandler;
        }

        public async Task<TResult> Handle(TRequest request)
        {
            var innerMostHandler = GetInnerMostHandler();

            var authAttribute = innerMostHandler.GetType().GetMethod("Handle").GetCustomAttribute(typeof(AuthorizeAttribute)) as AuthorizeAttribute;

            if(!string.IsNullOrEmpty(authAttribute.Roles))
            {
                new PrincipalPermission(null, authAttribute.Roles).Demand();
            }
            
            return await _innerHandler.Handle(request);
        }
        
        private IAsyncRequestHandler<TRequest, TResult> GetInnerMostHandler()
        {
            var innerHandler = _innerHandler;

            while(innerHandler is IAsyncDecoratorHandler<TRequest, TResult>)
            {
                innerHandler = (IAsyncRequestHandler<TRequest, TResult>)innerHandler.GetType().GetField("_innerHandler", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(innerHandler);
            }

            return innerHandler;
        }
    }

    //marker interface
    public interface IAsyncDecoratorHandler<TRequest, TResult>: IAsyncRequestHandler<TRequest, TResult> where TRequest : IAsyncRequest<TResult>
    {}

    //public static class RequestHandlerExtensions
    //{
    //    public static IAsyncRequestHandler<TRequest, TResult> DecorateWith<TRequest, TResult>(this IAsyncRequestHandler<TRequest, TResult> handler, Type genericDecoratorType)
    //        where TRequest : IAsyncRequest<TResult>
    //    {
    //        var interfaceDefinition = handler.GetType().GetInterfaces().First(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IAsyncRequestHandler<,>));
    //        var genericArguments = interfaceDefinition.GetGenericArguments();

    //        return Activator.CreateInstance(genericDecoratorType.MakeGenericType(genericArguments), handler) as IAsyncRequestHandler<TRequest, TResult>;
    //    }
    //}
}