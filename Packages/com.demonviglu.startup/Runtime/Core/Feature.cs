using System;
using System.Collections.Generic;

namespace DemonViglu.Startup
{
    /// <summary>Code-owned module. Dependencies are stable feature IDs, never object names.</summary>
    public abstract class Feature
    {
        public abstract string Id { get; }
        public virtual IReadOnlyList<string> Dependencies => Array.Empty<string>();
        public virtual void Configure(ServiceRegistry services) { }
        public abstract void Start(AppContext app, Lifetime lifetime);
        // Also called after a partially failed Start; must tolerate partial initialization.
        public virtual void Stop(AppContext app) { }
    }

    public sealed class AppContext
    {
        public KernelLog Log { get; }
        public ServiceRegistry Services { get; } = new ServiceRegistry();
        public EventBus Events { get; } = new EventBus();
        public GameLoop Loop { get; } = new GameLoop();
        public Scheduler Scheduler { get; }
        internal AppContext(KernelLog log, int periodicBudget)
        {
            Log = log;
            Scheduler = new Scheduler(periodicBudget);
            // The registry exposes exactly the bus owned and disposed by this application.
            Services.Add<IEventBus>(Events);
        }
    }
}
