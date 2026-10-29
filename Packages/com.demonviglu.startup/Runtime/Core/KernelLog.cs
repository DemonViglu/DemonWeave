using System;

namespace DemonViglu.Startup
{
    public enum KernelLogLevel { Debug, Info, Warning, Error }

    public sealed class KernelLogEntry
    {
        public long Sequence { get; }
        public DateTimeOffset Timestamp { get; }
        public KernelLogLevel Level { get; }
        public string Owner { get; }
        public string Stage { get; }
        public string Message { get; }
        public Exception Exception { get; }
        internal KernelLogEntry(long sequence, KernelLogLevel level, string owner, string stage,
            string message, Exception exception)
        {
            Sequence = sequence;
            Timestamp = DateTimeOffset.UtcNow;
            Level = level;
            Owner = owner;
            Stage = stage;
            Message = message;
            Exception = exception;
        }
    }

    public interface IKernelLogSink { void Write(KernelLogEntry entry); }

    /// <summary>Owning-thread diagnostics, independent of the EventBus. Retained after Kernel shutdown.</summary>
    public sealed class KernelLog
    {
        private readonly KernelLogEntry[] entries;
        private readonly IKernelLogSink sink;
        private readonly KernelLogLevel minimum;
        private int next;
        private long sequence;
        private bool writingSink;
        public int Count { get; private set; }
        public int Capacity => entries.Length;
        public long SinkFailures { get; private set; }
        public long SuppressedSinkReentries { get; private set; }
        public Exception LastSinkError { get; private set; }
        public KernelLogEntry Latest => Count == 0 ? null : entries[(next + Capacity - 1) % Capacity];

        public KernelLog(int capacity = 128, IKernelLogSink sink = null, KernelLogLevel minimum = KernelLogLevel.Info)
        {
            if (capacity <= 0) throw new ArgumentOutOfRangeException(nameof(capacity));
            if ((uint)minimum > (uint)KernelLogLevel.Error) throw new ArgumentOutOfRangeException(nameof(minimum));
            entries = new KernelLogEntry[capacity];
            this.sink = sink;
            this.minimum = minimum;
        }

        public void Write(KernelLogLevel level, string owner, string stage, string message, Exception error = null)
        {
            if ((uint)level > (uint)KernelLogLevel.Error) throw new ArgumentOutOfRangeException(nameof(level));
            if (level < minimum) return;
            var entry = new KernelLogEntry(++sequence, level, owner ?? "", stage ?? "", message ?? "", error);
            entries[next] = entry;
            next = (next + 1) % Capacity;
            Count = Math.Min(Count + 1, Capacity);
            if (sink == null) return;
            if (writingSink) { SuppressedSinkReentries++; return; }
            writingSink = true;
            try { sink.Write(entry); }
            catch (Exception e) { SinkFailures++; LastSinkError = e; }
            finally { writingSink = false; }
        }

        public KernelLogEntry[] Snapshot()
        {
            var result = new KernelLogEntry[Count];
            int start = (next + Capacity - Count) % Capacity;
            for (int i = 0; i < Count; ++i) result[i] = entries[(start + i) % Capacity];
            return result;
        }
    }
}
