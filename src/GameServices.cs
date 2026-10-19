using System;
using System.Collections.Generic;
using Core.Services;
using UnityEngine;

namespace HandyBeaconTracker
{
    // ServiceLocator.GetService scans every object tagged ServiceObject, so lookups are cached until the next scene.
    static class GameServices
    {
        static readonly Dictionary<Type, object> Found = new Dictionary<Type, object>();
        static readonly Dictionary<Type, float> RetryAt = new Dictionary<Type, float>();

        public static T Get<T>() where T : class
        {
            Type type = typeof(T);
            if (Found.TryGetValue(type, out object cached) && Alive(cached))
                return (T)cached;
            if (RetryAt.TryGetValue(type, out float at) && Time.unscaledTime < at)
                return null;

            T service = null;
            try
            {
                service = ServiceLocator.GetService<T>();
            }
            catch (Exception)
            {
            }
            if (service == null)
            {
                Found.Remove(type);
                RetryAt[type] = Time.unscaledTime + 2f;
                return null;
            }
            Found[type] = service;
            return service;
        }

        public static void Reset()
        {
            Found.Clear();
            RetryAt.Clear();
        }

        static bool Alive(object service)
        {
            return !(service is UnityEngine.Object unity) || unity != null;
        }
    }
}
