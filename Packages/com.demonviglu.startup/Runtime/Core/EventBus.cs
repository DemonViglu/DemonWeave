using System;
using System.Collections.Generic;

namespace DemonViglu.Startup
{
    /// <summary>Synchronous, exact-type dispatch on the owning thread. Each token owns one subscription.</summary>
    public sealed class EventBus : IEventBus, IDisposable
    {
        private sealed class Entry
        {
            public Delegate Handler;
            public bool Active = true;
        }
        private readonly Dictionary<Type, List<Entry>> listeners = new Dictionary<Type, List<Entry>>();
        private int depth;
        private bool disposed;
        public int SubscriptionCount { get; private set; }

        public IDisposable Subscribe<T>(Action<T> handler)
        {
            Check();
            if (handler == null) throw new ArgumentNullException(nameof(handler));
            if (!listeners.TryGetValue(typeof(T), out var list))
                listeners.Add(typeof(T), list = new List<Entry>());
            if (list.Exists(item => item.Handler.Equals(handler)))
                throw new InvalidOperationException($"Duplicate subscription: {typeof(T)}");
            var entry = new Entry { Handler = handler };
            list.Add(entry);
            SubscriptionCount++;
            return new Release(() => Remove(typeof(T), list, entry));
        }

        private void Remove(Type type, List<Entry> list, Entry entry)
        {
            if (!entry.Active) return;
            entry.Active = false;
            list.Remove(entry);
            SubscriptionCount--;
            if (list.Count == 0) listeners.Remove(type);
        }

        /// <summary>For explicit OnEnable/OnDisable pairs. Tokens remain valid and idempotent after removal.</summary>
        public bool Unsubscribe<T>(Action<T> handler)
        {
            if (handler == null) throw new ArgumentNullException(nameof(handler));
            if (disposed || !listeners.TryGetValue(typeof(T), out var list)) return false;
            var entry = list.Find(item => item.Handler.Equals(handler));
            if (entry == null) return false;
            Remove(typeof(T), list, entry);
            return true;
        }

        public void Publish<T>(T message)
        {
            Check();
            if (message == null) throw new ArgumentNullException(nameof(message));
            if (depth >= 32) throw new InvalidOperationException("Event recursion exceeded 32; check feedback loops.");
            if (!listeners.TryGetValue(typeof(T), out var list)) return;
            var snapshot = list.ToArray();
            List<Exception> errors = null;
            depth++;
            try
            {
                foreach (var entry in snapshot)
                {
                    if (!entry.Active) continue;
                    try { ((Action<T>)entry.Handler)(message); }
                    catch (Exception e) { (errors ??= new List<Exception>()).Add(e); }
                }
            }
            finally { depth--; }
            if (errors != null) throw new AggregateException($"Event {typeof(T)} failed.", errors);
        }

        private void Check() { if (disposed) throw new ObjectDisposedException(nameof(EventBus)); }
        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            foreach (var list in listeners.Values)
                foreach (var entry in list) entry.Active = false;
            listeners.Clear();
            SubscriptionCount = 0;
        }
    }
}
