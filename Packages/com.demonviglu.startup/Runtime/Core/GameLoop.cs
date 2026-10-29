using System;
using System.Collections.Generic;

namespace DemonViglu.Startup
{
    public enum LoopPhase { Fixed, Update, Late, PostPhysics }

    public sealed class LoopCallbackException : Exception
    {
        public string OwnerId { get; }
        public LoopPhase Phase { get; }
        public LoopCallbackException(string ownerId, LoopPhase phase, Exception cause)
            : base($"Loop callback failed: owner={ownerId}, phase={phase}", cause)
        { OwnerId = ownerId; Phase = phase; }
    }

    public readonly struct FrameTime
    {
        public readonly double Delta;
        public readonly double UnscaledDelta;
        public FrameTime(double delta, double unscaledDelta)
        {
            if (!Valid(delta) || !Valid(unscaledDelta)) throw new ArgumentOutOfRangeException(nameof(delta));
            Delta = delta;
            UnscaledDelta = unscaledDelta;
        }
        internal static bool Valid(double value) => value >= 0 && !double.IsInfinity(value) && !double.IsNaN(value);
    }

    public sealed class GameLoop : IDisposable
    {
        private sealed class Entry
        {
            public LoopPhase Phase;
            public int Order;
            public long Sequence;
            public Action<FrameTime> Tick;
            public bool Active = true;
            public string Owner;
        }
        private readonly List<Entry> entries = new List<Entry>();
        private Entry[] snapshot = Array.Empty<Entry>();
        private bool dirty;
        private long sequence;
        private bool ticking;
        private bool disposed;
        public int Count => entries.Count;

        public IDisposable Register(LoopPhase phase, Action<FrameTime> tick, int order = 0, string ownerId = null)
        {
            if (disposed) throw new ObjectDisposedException(nameof(GameLoop));
            if ((uint)phase > (uint)LoopPhase.PostPhysics) throw new ArgumentOutOfRangeException(nameof(phase));
            if (tick == null) throw new ArgumentNullException(nameof(tick));
            var entry = new Entry { Phase = phase, Tick = tick, Order = order, Sequence = sequence++, Owner = ownerId ?? "unattributed" };
            entries.Add(entry);
            entries.Sort((a, b) => a.Order != b.Order ? a.Order.CompareTo(b.Order) : a.Sequence.CompareTo(b.Sequence));
            dirty = true;
            return new Release(() => { entry.Active = false; entries.Remove(entry); dirty = true; });
        }

        public void Tick(LoopPhase phase, FrameTime time)
        {
            if (disposed) throw new ObjectDisposedException(nameof(GameLoop));
            if ((uint)phase > (uint)LoopPhase.PostPhysics) throw new ArgumentOutOfRangeException(nameof(phase));
            if (ticking) throw new InvalidOperationException("GameLoop cannot be ticked recursively.");
            ticking = true;
            List<Exception> errors = null;
            if (dirty) { snapshot = entries.ToArray(); dirty = false; }
            try
            {
                foreach (var entry in snapshot)
                {
                    if (!entry.Active || entry.Phase != phase) continue;
                    try { entry.Tick(time); }
                    catch (Exception e) { (errors ??= new List<Exception>()).Add(new LoopCallbackException(entry.Owner, phase, e)); }
                }
            }
            finally { ticking = false; }
            if (errors != null) throw new AggregateException("GameLoop callbacks failed.", errors);
        }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            foreach (var entry in entries) entry.Active = false;
            entries.Clear();
            snapshot = Array.Empty<Entry>();
        }
    }
}
