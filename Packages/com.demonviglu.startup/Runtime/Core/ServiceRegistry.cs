using System;
using System.Collections.Generic;

namespace DemonViglu.Startup
{
    /// <summary>One instance per contract, per application. No reflection or implicit factories.</summary>
    public sealed class ServiceRegistry : IDisposable
    {
        private readonly Dictionary<Type, object> services = new Dictionary<Type, object>();
        private readonly List<IDisposable> owned = new List<IDisposable>();
        private bool frozen;
        private bool disposed;
        public int Count => services.Count;
        public bool IsDisposed => disposed;

        public void Add<T>(T instance, bool takeOwnership = false) where T : class
        {
            Check();
            if (frozen) throw new InvalidOperationException("Composition is complete; services are read-only.");
            if (instance == null) throw new ArgumentNullException(nameof(instance));
            if (services.ContainsKey(typeof(T))) throw new InvalidOperationException($"Duplicate service: {typeof(T)}");
            if (takeOwnership && !(instance is IDisposable))
                throw new ArgumentException("Owned services must implement IDisposable.", nameof(instance));
            services.Add(typeof(T), instance);
            if (takeOwnership)
            {
                var disposable = (IDisposable)instance;
                if (!owned.Exists(item => ReferenceEquals(item, disposable))) owned.Add(disposable);
            }
        }

        public T Get<T>() where T : class
        {
            if (TryGet<T>(out var result)) return result;
            throw new InvalidOperationException($"Missing service: {typeof(T)}");
        }

        public bool TryGet<T>(out T instance) where T : class
        {
            Check();
            bool found = services.TryGetValue(typeof(T), out var value);
            instance = found ? (T)value : null;
            return found;
        }

        internal void Freeze() { Check(); frozen = true; }
        private void Check() { if (disposed) throw new ObjectDisposedException(nameof(ServiceRegistry)); }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            var errors = new List<Exception>();
            for (int i = owned.Count - 1; i >= 0; --i)
                try { owned[i].Dispose(); } catch (Exception e) { errors.Add(e); }
            owned.Clear();
            services.Clear();
            if (errors.Count > 0) throw new AggregateException("Service cleanup failed.", errors);
        }
    }
}
