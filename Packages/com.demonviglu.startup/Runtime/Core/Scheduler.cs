using System;
using System.Collections.Generic;

namespace DemonViglu.Startup
{
    /// <summary>Update-driven delays and budgeted periodic work. New jobs wait until a later Tick.</summary>
    public sealed class Scheduler : IDisposable
    {
        internal sealed class Job
        {
            public double Due;
            public bool Unscaled;
            public Action Callback;
            public bool Active = true;
            public double Interval;
            public double LastRun;
            public Action<double> PeriodicCallback;
            public string Owner;
        }
        public sealed class PeriodicTask : IDisposable
        {
            private Scheduler scheduler;
            private readonly Job job;
            internal PeriodicTask(Scheduler scheduler, Job job) { this.scheduler = scheduler; this.job = job; }
            public void ChangeInterval(double seconds)
            {
                if (scheduler == null || !job.Active) throw new ObjectDisposedException(nameof(PeriodicTask));
                ValidateInterval(seconds);
                job.Interval = seconds;
                job.Due = (job.Unscaled ? scheduler.unscaled : scheduler.scaled) + seconds;
            }
            public void Dispose()
            {
                if (scheduler == null) return;
                job.Active = false;
                job.PeriodicCallback = null;
                scheduler.jobs.Remove(job);
                scheduler.dirty = true;
                scheduler = null;
            }
        }
        private readonly List<Job> jobs = new List<Job>();
        private Job[] snapshot = Array.Empty<Job>();
        private bool dirty;
        private double scaled;
        private double unscaled;
        private bool disposed;
        private bool ticking;
        private int periodicCursor;
        public int Count => jobs.Count;
        public int PeriodicBudget { get; }

        public Scheduler(int periodicBudget = 128)
        {
            if (periodicBudget <= 0) throw new ArgumentOutOfRangeException(nameof(periodicBudget));
            PeriodicBudget = periodicBudget;
        }

        private static void ValidateInterval(double seconds)
        {
            if (!FrameTime.Valid(seconds) || seconds == 0) throw new ArgumentOutOfRangeException(nameof(seconds));
        }

        /// <summary>At most once per Tick; missed periods are skipped, while callback receives actual elapsed time.</summary>
        public PeriodicTask Repeat(double interval, Action<double> callback, double? initialDelay = null,
            bool useUnscaledTime = false, string ownerId = null)
        {
            if (disposed) throw new ObjectDisposedException(nameof(Scheduler));
            ValidateInterval(interval);
            double delay = initialDelay ?? interval;
            if (!FrameTime.Valid(delay)) throw new ArgumentOutOfRangeException(nameof(initialDelay));
            if (callback == null) throw new ArgumentNullException(nameof(callback));
            double now = useUnscaledTime ? unscaled : scaled;
            var job = new Job { Due = now + delay, LastRun = now, Interval = interval,
                Unscaled = useUnscaledTime, PeriodicCallback = callback, Owner = ownerId ?? "unattributed" };
            jobs.Add(job);
            dirty = true;
            return new PeriodicTask(this, job);
        }

        public IDisposable Delay(double seconds, Action callback, bool useUnscaledTime = false)
        {
            if (disposed) throw new ObjectDisposedException(nameof(Scheduler));
            if (!FrameTime.Valid(seconds)) throw new ArgumentOutOfRangeException(nameof(seconds));
            if (callback == null) throw new ArgumentNullException(nameof(callback));
            var job = new Job { Due = (useUnscaledTime ? unscaled : scaled) + seconds,
                Unscaled = useUnscaledTime, Callback = callback };
            jobs.Add(job);
            dirty = true;
            return new Release(() => { job.Active = false; jobs.Remove(job); dirty = true; });
        }

        public void Tick(FrameTime time)
        {
            if (disposed) throw new ObjectDisposedException(nameof(Scheduler));
            if (ticking) throw new InvalidOperationException("Scheduler cannot be ticked recursively.");
            ticking = true;
            scaled += time.Delta;
            unscaled += time.UnscaledDelta;
            List<Exception> errors = null;
            if (dirty) { snapshot = jobs.ToArray(); dirty = false; }
            try
            {
                foreach (var job in snapshot)
                {
                    if (!job.Active || job.Interval > 0 || job.Due > (job.Unscaled ? unscaled : scaled)) continue;
                    job.Active = false;
                    jobs.Remove(job);
                    dirty = true;
                    try { job.Callback(); } catch (Exception e) { (errors ??= new List<Exception>()).Add(e); }
                }
                // Round-robin among the stable snapshot prevents an early due job from starving later jobs.
                var current = snapshot;
                int start = current.Length == 0 ? 0 : periodicCursor % current.Length;
                int invoked = 0;
                for (int visited = 0; visited < current.Length && invoked < PeriodicBudget; visited++)
                {
                    int index = (start + visited) % current.Length;
                    periodicCursor = (index + 1) % current.Length;
                    var job = current[index];
                    double now = job.Unscaled ? unscaled : scaled;
                    double delta = job.Unscaled ? time.UnscaledDelta : time.Delta;
                    if (!job.Active || job.Interval == 0 || delta == 0 || job.Due > now) continue;
                    double elapsed = now - job.LastRun;
                    job.LastRun = now;
                    // Keep the original staggering after hitches, without replaying missed decisions.
                    job.Due = now + job.Interval - ((now - job.Due) % job.Interval);
                    invoked++;
                    try { job.PeriodicCallback(elapsed); }
                    catch (Exception e)
                    {
                        (errors ??= new List<Exception>()).Add(new LoopCallbackException(job.Owner, LoopPhase.Update, e));
                    }
                }
            }
            finally { ticking = false; }
            if (errors != null) throw new AggregateException("Scheduled callbacks failed.", errors);
        }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            foreach (var job in jobs)
            {
                job.Active = false;
                job.Callback = null;
                job.PeriodicCallback = null;
            }
            jobs.Clear();
            snapshot = Array.Empty<Job>();
        }
    }
}
