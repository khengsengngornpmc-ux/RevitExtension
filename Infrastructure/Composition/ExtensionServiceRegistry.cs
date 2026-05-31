using System;
using System.Collections.Generic;

namespace CamboBIM.Revit2024.Addin
{
    internal static class ExtensionServiceRegistry
    {
        private static readonly object SyncRoot = new object();
        private static readonly Dictionary<Type, Func<object>> Factories = new Dictionary<Type, Func<object>>();
        private static readonly Dictionary<Type, object> Singletons = new Dictionary<Type, object>();

        public static int RegisteredServiceCount
        {
            get
            {
                lock (SyncRoot)
                {
                    return Factories.Count + Singletons.Count;
                }
            }
        }

        public static void Clear()
        {
            lock (SyncRoot)
            {
                Factories.Clear();
                Singletons.Clear();
            }
        }

        public static void RegisterSingleton<TService>(TService instance)
            where TService : class
        {
            if (instance == null)
            {
                throw new ArgumentNullException(nameof(instance));
            }

            lock (SyncRoot)
            {
                Singletons[typeof(TService)] = instance;
                Factories.Remove(typeof(TService));
            }
        }

        public static void RegisterFactory<TService>(Func<TService> factory)
            where TService : class
        {
            if (factory == null)
            {
                throw new ArgumentNullException(nameof(factory));
            }

            lock (SyncRoot)
            {
                Factories[typeof(TService)] = () => factory();
                Singletons.Remove(typeof(TService));
            }
        }

        public static TService Resolve<TService>()
            where TService : class
        {
            if (TryResolve(out TService service))
            {
                return service;
            }

            throw new InvalidOperationException("Service is not registered: " + typeof(TService).FullName);
        }

        public static bool TryResolve<TService>(out TService service)
            where TService : class
        {
            object value;
            Func<object> factory;

            lock (SyncRoot)
            {
                if (Singletons.TryGetValue(typeof(TService), out value))
                {
                    service = value as TService;
                    return service != null;
                }

                if (Factories.TryGetValue(typeof(TService), out factory))
                {
                    value = factory();
                    service = value as TService;
                    return service != null;
                }
            }

            service = null;
            return false;
        }
    }
}
