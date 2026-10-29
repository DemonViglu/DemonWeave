using System;
using System.Collections.Generic;

namespace DemonViglu.Startup
{
    /// <summary>Main-thread resource owner. Releases in reverse order, even if cleanup fails.</summary>
    public sealed class Lifetime : IDisposable
    {
        private readonly List<IDisposable> resources = new List<IDisposable>();
        public bool IsDisposed { get; private set; }

        public T Own<T>(T resource) where T : IDisposable
        {
            if (resource == null) throw new ArgumentNullException(nameof(resource));
            if (IsDisposed) resource.Dispose();
            else resources.Add(resource);
            return resource;
        }

        public void Dispose()
        {
            if (IsDisposed) return;
            IsDisposed = true;
            var errors = new List<Exception>();
            for (int i = resources.Count - 1; i >= 0; --i)
                try { resources[i].Dispose(); } catch (Exception e) { errors.Add(e); }
            resources.Clear();
            if (errors.Count > 0) throw new AggregateException("Resource cleanup failed.", errors);
        }
    }

    internal sealed class Release : IDisposable
    {
        private Action action;
        public Release(Action action) { this.action = action; }
        public void Dispose()
        {
            var release = action;
            action = null;
            release?.Invoke();
        }
    }
}
