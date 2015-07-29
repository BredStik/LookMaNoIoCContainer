using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Serialization.Formatters.Binary;
using System.Web;

namespace LookMaNoIoC
{
    public class Container : IDisposable
    {
        private readonly Container _parentContainer;

        public Container(Container parentContainer = null, IDictionary<Type, object> singletonServices = null, IDictionary<Type, Registration> registrations = null)
        {
            _parentContainer = parentContainer;

            if (singletonServices != null)
            {
                foreach (var service in singletonServices)
                {
                    var implementation = service.Value;

                    if(service.Value.GetType().IsGenericType && service.Value.GetType().GetGenericTypeDefinition() == typeof(Lazy<>))
                    {
                        var genericArguments = service.Value.GetType().GetGenericArguments();
                        var lazyType = typeof(Lazy<>).MakeGenericType(genericArguments);

                        var factory = lazyType.GetField("m_valueFactory", System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance).GetValue(service.Value);

                        implementation = Activator.CreateInstance(lazyType, factory);
                    }

                    _singletonServices.Add(service.Key, implementation);
                }
            }

            if (registrations != null)
            {
                foreach (var registration in registrations)
                {
                    _registrations.Add(registration);
                }
            }
        }
        
        private readonly IDictionary<Type, object> _singletonServices = new Dictionary<Type, object>();
        private readonly IDictionary<Type, object> _perWebRequestServices = new Dictionary<Type, object>();
        private readonly IDictionary<Type, Registration> _registrations = new Dictionary<Type, Registration>();

        public Registration Register<TService, TImplementation>(Lifestyle lifestyle = Lifestyle.Singleton) where TImplementation : TService
        {
            return Register(typeof(TService), typeof(TImplementation), lifestyle);
        }

        public Registration Register(Type serviceType, Type implementationType, Lifestyle lifestyle = Lifestyle.Singleton)
        {
            var registration = new Registration();
            _registrations.Add(serviceType, registration);

            switch (lifestyle)
            {
                case Lifestyle.Singleton:
                    _singletonServices.Add(serviceType, new Lazy<object>(
                    () =>
                    {
                        object[] parameters;

                        try
                        {
                            parameters = ResolveDependencies(implementationType);
                        }
                        catch (InvalidOperationException ex)
                        {
                            throw new InvalidOperationException(string.Format("Could not resolve all dependency tree for type '{0}'", implementationType.FullName), ex);
                        }
                        return Activator.CreateInstance(implementationType, parameters);

                    }, true

                    ));
                    break;
                case Lifestyle.PerWebRequest:
                    _perWebRequestServices.Add(serviceType, new Lazy<object>(
                    () =>
                    {
                        object[] parameters;

                        try
                        {
                            parameters = ResolveDependencies(implementationType);
                        }
                        catch (InvalidOperationException ex)
                        {
                            throw new InvalidOperationException(string.Format("Could not resolve all dependency tree for type '{0}'", implementationType.FullName), ex);
                        }
                        return Activator.CreateInstance(implementationType, parameters);

                    }, true

                    ));
                    break;
            }

            return registration;
        }

        public Registration Register<TService>(Func<TService> factory, Lifestyle lifestyle = Lifestyle.Singleton)
        {
            return Register(typeof(TService), () => factory(), lifestyle);
        }

        public Registration Register<TService, TImplementation>(Type serviceType, Func<TService> factory, Lifestyle lifestyle = Lifestyle.Singleton) where TImplementation : TService
        {
            var registration = new Registration();
            _registrations.Add(typeof(TService), registration);


            switch (lifestyle)
            {
                case Lifestyle.Singleton:
                    _singletonServices.Add(typeof(TService), new Lazy<TService>(factory, true));
                    break;
                case Lifestyle.PerWebRequest:
                    _perWebRequestServices.Add(typeof(TService), new Lazy<TService>(factory, true));
                    break;
            }

            return registration;
        }

        public Registration Register(Type serviceType, Func<object> factory, Lifestyle lifestyle = Lifestyle.Singleton)
        {
            var registration = new Registration();
            _registrations.Add(serviceType, registration);

            switch (lifestyle)
            {
                case Lifestyle.Singleton:
                    _singletonServices.Add(serviceType, new Lazy<object>(factory, true));
                    break;
                case Lifestyle.PerWebRequest:
                    _perWebRequestServices.Add(serviceType, new Lazy<object>(factory, true));
                    break;
            }

            return registration;
        }

        private object[] ResolveDependencies(Type type, Dictionary<string, object> overrides = null)
        {
            var longestConstructor = type.GetConstructors().OrderByDescending(x => x.GetParameters().Length).First();

            if (longestConstructor.GetParameters().Length > 0)
            {
                var parameterTypes = longestConstructor.GetParameters().ToDictionary(x => x.Name, x => x.ParameterType);

                var deps = parameterTypes.ToDictionary(x => x.Key, x => overrides != null && overrides.ContainsKey(x.Key) ? overrides[x.Key] : Resolve(x.Value, false, true));

                return deps.Values.ToArray();
            }

            return new object[0];
        }

        public TService Resolve<TService>()
        {
            return (TService)Resolve(typeof(TService));
        }

        public object Resolve(Type serviceType, bool fromParent = false, bool throwOnNotFound = false)
        {
            object implementation = null;

            if (HttpContext.Current != null && _parentContainer == null)
            {
                if (HttpContext.Current.Items["Container"] == null)
                {
                    HttpContext.Current.Items["Container"] = new Container(this, _perWebRequestServices, _registrations);
                }

                //try resolve with per web request container
                implementation = ((Container)HttpContext.Current.Items["Container"]).Resolve(serviceType, true);
                if(implementation != null)
                {
                    return implementation;
                }
            }

            if (implementation == null && _parentContainer != null && !fromParent)
            {
                implementation = _parentContainer.Resolve(serviceType);
            }

            if (implementation == null && _singletonServices.ContainsKey(serviceType))
            {
                var lazyHandler = _singletonServices[serviceType];
                implementation = lazyHandler.GetType().GetProperty("Value").GetValue(lazyHandler);
            }

            if(implementation == null && serviceType.IsGenericType && !serviceType.IsGenericTypeDefinition)
            {
                var implementationType = Resolve(serviceType.GetGenericTypeDefinition()) as Type;

                if (implementationType != null)
                {
                    var genericImplType = implementationType.MakeGenericType(serviceType.GetGenericArguments());

                    object[] parameters;

                    try
                    {
                        parameters = ResolveDependencies(genericImplType);
                    }
                    catch (InvalidOperationException ex)
                    {
                        throw new InvalidOperationException(string.Format("Could not resolve all dependency tree for type '{0}'", genericImplType.FullName), ex);
                    }
                    
                    var genericImpl = Activator.CreateInstance(genericImplType, parameters);

                    implementation = Decorate(serviceType.GetGenericTypeDefinition(), genericImpl);
                }
            }

            if (implementation == null)
            {
                if(!throwOnNotFound)
                    return null;

                throw new InvalidOperationException(string.Format("Could not resolve type '{0}'", serviceType.FullName));
            }

            implementation = Decorate(serviceType, implementation);

            return implementation;
        }

        private object Decorate(Type serviceType, object implementation)
        {
            if (implementation is Type)
            {
                return implementation;
            }

            if (_registrations.ContainsKey(serviceType) && _registrations[serviceType].Decorators.Count > 0)
            {
                Type[] genericArgs = null;
                if (serviceType.IsGenericType && !serviceType.IsGenericTypeDefinition)
                {
                    genericArgs = serviceType.GetGenericArguments();
                }
                else if (implementation.GetType().IsGenericType && !implementation.GetType().IsGenericTypeDefinition)
                {
                    genericArgs = implementation.GetType().GetGenericArguments();
                }


                foreach (var decoratorType in _registrations[serviceType].Decorators)
                {
                    var currentType = decoratorType;
                    if (genericArgs != null && currentType.IsGenericTypeDefinition)
                    {
                        currentType = currentType.MakeGenericType(genericArgs);
                    }


                    var decoreeName = currentType.GetConstructors().First().GetParameters().Single(x => x.ParameterType.IsAssignableFrom(currentType)).Name;

                    var deps = ResolveDependencies(currentType, new Dictionary<string, object> { { decoreeName, implementation } });

                    implementation = Activator.CreateInstance(currentType, deps);
                }
            }
            return implementation;
        }


        public void Dispose()
        {
            foreach (var item in _singletonServices.Values)
            {
                if ((bool)item.GetType().GetProperty("IsValueCreated").GetValue(item) == false)
                {
                    continue;
                }

                var actualImplementation = item.GetType().GetProperty("Value").GetValue(item); //Resolve(item.GetType());

                if (actualImplementation is IDisposable)
                {
                    ((IDisposable)actualImplementation).Dispose();
                }
            }
        }
    }

    public enum Lifestyle
    {
        Singleton, PerWebRequest
    }

    public class Registration
    {
        private IList<Type> _decorators = new List<Type>();
        public IReadOnlyList<Type> Decorators
        {
            get { return ((List<Type>)_decorators).AsReadOnly(); }
        }
        public Registration DecorateWith(Type decoratorType)
        {
            _decorators.Add(decoratorType);
            return this;
        }
    }

    public class ContainerHttpModule : IHttpModule
    {
        public void Dispose()
        {}

        public void Init(HttpApplication context)
        {
            context.EndRequest += context_EndRequest;

        }

        void context_EndRequest(object sender, EventArgs e)
        {
            var container = ((Container)HttpContext.Current.Items["Container"]);

            if (container != null)
            {
                container.Dispose();
            }
        }
    }

}