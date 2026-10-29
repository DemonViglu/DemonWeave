using System;
using System.Collections.Generic;

namespace DemonViglu.Startup
{
    public enum KernelState { Created, Starting, Running, Stopping, Stopped, Faulted }

    /// <summary>Owns one application lifetime. Create a new Kernel to restart.</summary>
    public sealed class Kernel : IDisposable
    {
        private sealed class StartedFeature
        {
            public Feature Feature;
            public Lifetime Lifetime = new Lifetime();
        }
        private readonly List<Feature> features;
        private readonly List<StartedFeature> started = new List<StartedFeature>();
        private readonly Action<ServiceRegistry> compose;
        private bool transitioning;
        private bool cleaned;
        public AppContext App { get; }
        public KernelState State { get; private set; }
        public Exception LastError { get; private set; }
        public int RunningFeatureCount => started.Count;

        public Kernel(IEnumerable<Feature> features, Action<ServiceRegistry> compose = null, KernelLog log = null,
            int periodicBudget = 128)
        {
            this.features = new List<Feature>(features ?? throw new ArgumentNullException(nameof(features)));
            this.compose = compose;
            App = new AppContext(log ?? new KernelLog(), periodicBudget);
        }

        public void Start()
        {
            if (State != KernelState.Created || transitioning)
                throw new InvalidOperationException($"Cannot start Kernel in {State}.");
            transitioning = true;
            State = KernelState.Starting;
            string owner = "kernel", stage = "Validate";
            App.Log.Write(KernelLogLevel.Info, owner, stage, "Starting application.");
            try
            {
                // Validate the entire graph before any registration or side effects.
                var ordered = OrderFeatures();
                stage = "Compose";
                compose?.Invoke(App.Services);
                foreach (var feature in ordered)
                {
                    owner = feature.Id;
                    stage = "Configure";
                    feature.Configure(App.Services);
                    App.Log.Write(KernelLogLevel.Info, owner, stage, "Feature configured.");
                }
                App.Services.Freeze();
                foreach (var feature in ordered)
                {
                    var item = new StartedFeature { Feature = feature };
                    started.Add(item);
                    owner = feature.Id;
                    stage = "Start";
                    feature.Start(App, item.Lifetime);
                    App.Log.Write(KernelLogLevel.Info, owner, stage, "Feature started.");
                }
                State = KernelState.Running;
                App.Log.Write(KernelLogLevel.Info, "kernel", "Running", "Application running.");
            }
            catch (Exception e)
            {
                State = KernelState.Faulted;
                App.Log.Write(KernelLogLevel.Error, owner, stage, "Application startup failed.", e);
                LastError = Cleanup(e);
                throw LastError;
            }
            finally { transitioning = false; }
        }

        public void Tick(LoopPhase phase, FrameTime time)
        {
            if (State != KernelState.Running) return;
            try
            {
                if (phase == LoopPhase.Update) App.Scheduler.Tick(time);
                if (State == KernelState.Running) App.Loop.Tick(phase, time);
            }
            catch (Exception e)
            {
                State = KernelState.Faulted;
                App.Log.Write(KernelLogLevel.Error, "kernel", phase.ToString(), "Application tick failed.", e);
                transitioning = true;
                try { LastError = Cleanup(e); }
                finally { transitioning = false; }
                throw LastError;
            }
        }

        public void Dispose()
        {
            if (transitioning) throw new InvalidOperationException("Cannot stop during a lifecycle transition.");
            if (cleaned) return;
            transitioning = true;
            State = KernelState.Stopping;
            App.Log.Write(KernelLogLevel.Info, "kernel", "Stopping", "Stopping application.");
            try
            {
                LastError = Cleanup(null);
                State = LastError == null ? KernelState.Stopped : KernelState.Faulted;
                App.Log.Write(LastError == null ? KernelLogLevel.Info : KernelLogLevel.Error,
                    "kernel", State.ToString(), "Application cleanup finished.", LastError);
                if (LastError != null) throw LastError;
            }
            finally { transitioning = false; }
        }

        private Exception Cleanup(Exception cause)
        {
            if (cleaned) return cause;
            cleaned = true;
            var errors = new List<Exception>();
            if (cause != null) errors.Add(cause);
            for (int i = started.Count - 1; i >= 0; --i)
            {
                var item = started[i];
                try
                {
                    item.Feature.Stop(App);
                    App.Log.Write(KernelLogLevel.Info, item.Feature.Id, "Stop", "Feature stopped.");
                }
                catch (Exception e)
                {
                    errors.Add(e);
                    App.Log.Write(KernelLogLevel.Error, item.Feature.Id, "Stop", "Feature stop failed.", e);
                }
                try { item.Lifetime.Dispose(); }
                catch (Exception e)
                {
                    errors.Add(e);
                    App.Log.Write(KernelLogLevel.Error, item.Feature.Id, "Release", "Feature resources failed to release.", e);
                }
            }
            started.Clear();
            foreach (var resource in new IDisposable[] { App.Scheduler, App.Loop, App.Events, App.Services })
                try { resource.Dispose(); }
                catch (Exception e)
                {
                    errors.Add(e);
                    App.Log.Write(KernelLogLevel.Error, resource.GetType().Name, "Release", "Application resource cleanup failed.", e);
                }
            return errors.Count == 0 ? null : new AggregateException("Application lifetime failed.", errors);
        }

        private List<Feature> OrderFeatures()
        {
            var byId = new Dictionary<string, Feature>(StringComparer.Ordinal);
            foreach (var feature in features)
            {
                if (feature == null || string.IsNullOrWhiteSpace(feature.Id))
                    throw new InvalidOperationException("Feature and stable ID are required.");
                if (byId.ContainsKey(feature.Id)) throw new InvalidOperationException($"Duplicate feature: {feature.Id}");
                byId.Add(feature.Id, feature);
            }
            var ordered = new List<Feature>();
            var marks = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (var feature in features) Visit(feature, byId, marks, ordered);
            return ordered;
        }

        private static void Visit(Feature feature, Dictionary<string, Feature> byId,
            Dictionary<string, int> marks, List<Feature> ordered)
        {
            if (marks.TryGetValue(feature.Id, out int mark))
            {
                if (mark == 1) throw new InvalidOperationException($"Feature dependency cycle at {feature.Id}");
                return;
            }
            marks[feature.Id] = 1;
            if (feature.Dependencies == null) throw new InvalidOperationException($"Null dependencies: {feature.Id}");
            foreach (var dependencyId in feature.Dependencies)
            {
                if (dependencyId == null || !byId.TryGetValue(dependencyId, out var dependency))
                    throw new InvalidOperationException($"Feature {feature.Id} requires {dependencyId}");
                Visit(dependency, byId, marks, ordered);
            }
            marks[feature.Id] = 2;
            ordered.Add(feature);
        }
    }
}
