using System;

namespace DemonViglu.Startup
{
    /// <summary>A fixed registry entry whose borrowed scene/player provider has an explicit lease.</summary>
    public sealed class ServiceBinding<T> : IDisposable where T : class
    {
        private sealed class Lease { public T Value; }
        private Lease current;
        private bool disposed;
        public T Current => current?.Value;
        public T Require() => Current ?? throw new InvalidOperationException($"No active provider for {typeof(T)}.");

        public IDisposable Bind(T provider)
        {
            if (disposed) throw new ObjectDisposedException(nameof(ServiceBinding<T>));
            if (provider == null) throw new ArgumentNullException(nameof(provider));
            if (current != null) throw new InvalidOperationException($"Provider already bound for {typeof(T)}; release its lease first.");
            var lease = new Lease { Value = provider };
            current = lease;
            return new Release(() =>
            {
                if (ReferenceEquals(current, lease)) current = null;
                lease.Value = null;
            });
        }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            if (current != null) current.Value = null;
            current = null;
        }
    }
}
