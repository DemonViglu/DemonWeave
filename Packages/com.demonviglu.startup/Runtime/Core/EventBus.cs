using System;
using System.Collections.Generic;
using System.Threading;

namespace DemonViglu.Startup
{
    /// <summary>Publish immediately or Post for a later Flush, using the same typed subscriptions.</summary>
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
        private readonly int ownerThread = Thread.CurrentThread.ManagedThreadId;
        private readonly LinkedList<QueuedEvent> waiting = new LinkedList<QueuedEvent>();
        private readonly List<QueuedEvent> batch = new List<QueuedEvent>();
        private bool flushing;
        public int QueueCapacity { get; }
        public int PendingCount { get { CheckThread(); return waiting.Count; } }
        public int SubscriptionCount { get; private set; }

        private sealed class Pending : IDisposable
        {
            private Action cancel;
            public Pending(Action cancel) { this.cancel = cancel; }
            public void Complete() { cancel = null; }
            public void Dispose() { cancel?.Invoke(); cancel = null; }
        }
        private sealed class QueuedEvent
        {
            public Action Dispatch;
            public Pending Token;
        }
        public EventBus(int queueCapacity = 1024)
        {
            if (queueCapacity <= 0) throw new ArgumentOutOfRangeException(nameof(queueCapacity));
            QueueCapacity = queueCapacity;
        }
        public IDisposable Post<T>(T message)
        {
            if (!TryPost(message, out var cancellation))
                throw new InvalidOperationException($"Event queue is full (capacity {QueueCapacity}).");
            return cancellation;
        }
        public bool TryPost<T>(T message, out IDisposable cancellation)
        {
            Check();
            if (message == null) throw new ArgumentNullException(nameof(message));
            cancellation = null;
            if (waiting.Count == QueueCapacity) return false;
            var entry = new QueuedEvent { Dispatch = () => Publish(message) };
            var node = waiting.AddLast(entry);
            entry.Token = new Pending(() =>
            {
                CheckThread();
                if (node.List != null) waiting.Remove(node);
                entry.Dispatch = null;
            });
            cancellation = entry.Token;
            return true;
        }
        /// <summary>Dispatch only the batch present at entry. Newly posted messages wait for another Flush.</summary>
        public int Flush(int maxMessages = 128)
        {
            Check();
            if (maxMessages <= 0) throw new ArgumentOutOfRangeException(nameof(maxMessages));
            if (flushing || depth != 0) throw new InvalidOperationException("Cannot flush recursively or from an event listener.");
            flushing = true;
            int dispatched = 0;
            List<Exception> errors = null;
            try
            {
                int count = Math.Min(maxMessages, waiting.Count);
                for (int i = 0; i < count; i++)
                { batch.Add(waiting.First.Value); waiting.RemoveFirst(); }
                foreach (var entry in batch)
                {
                    var dispatch = entry.Dispatch;
                    entry.Dispatch = null;
                    entry.Token.Complete();
                    if (disposed || dispatch == null) continue;
                    dispatched++;
                    try { dispatch(); }
                    catch (Exception error) { (errors ??= new List<Exception>()).Add(error); }
                }
            }
            finally { batch.Clear(); flushing = false; }
            if (errors != null) throw new AggregateException("Queued events failed; messages were not retried.", errors);
            return dispatched;
        }

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
            CheckThread();
            if (!entry.Active) return;
            entry.Active = false;
            list.Remove(entry);
            SubscriptionCount--;
            if (list.Count == 0) listeners.Remove(type);
        }

        /// <summary>For explicit OnEnable/OnDisable pairs. Tokens remain valid and idempotent after removal.</summary>
        public bool Unsubscribe<T>(Action<T> handler)
        {
            CheckThread();
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

        private void CheckThread()
        {
            if (Thread.CurrentThread.ManagedThreadId != ownerThread)
                throw new InvalidOperationException("EventBus must be used on its owning thread.");
        }
        private void Check() { CheckThread(); if (disposed) throw new ObjectDisposedException(nameof(EventBus)); }
        public void Dispose()
        {
            CheckThread();
            if (disposed) return;
            disposed = true;
            foreach (var entry in waiting) { entry.Dispatch = null; entry.Token.Complete(); }
            waiting.Clear();
            foreach (var entry in batch) { entry.Dispatch = null; entry.Token.Complete(); }
            foreach (var list in listeners.Values)
                foreach (var entry in list) entry.Active = false;
            listeners.Clear();
            SubscriptionCount = 0;
        }
    }
}
