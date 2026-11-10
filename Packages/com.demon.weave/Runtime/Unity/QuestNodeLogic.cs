using System;
using System.Collections.Generic;
using UnityEngine.Scripting;
using UnityEngine;
namespace DemonWeave.Unity
{
    // Stable authoring identity, independent of class names and scene instances.
    [AttributeUsage(AttributeTargets.Class, Inherited = false)]
    public sealed class QuestLogicAttribute : PreserveAttribute
    {
        public string Id { get; }
        public string Name { get; }
        public QuestLogicAttribute(string id, string name) { Id = id; Name = name; }
    }
    public enum QuestStopReason { Completed, Cancelled, Faulted }

    // Each activation gets a new instance. Only public parameter fields enter JSON.
    public abstract class QuestNodeLogic : IDisposable
    {
        public virtual string Comment => "";
        public virtual IReadOnlyList<string> Results => Array.Empty<string>();
        public virtual string CheckConfiguration(QuestBindings objects) => "";
        protected QuestBindings Objects { get; private set; }
        private Action<string> completed;
        private bool stopped, started, reported;
        private int thread;
        private bool paused;
        private double accumulated, runningSince;
        private string pendingResult;
        public bool IsPaused => paused;
        protected double ActiveTime => accumulated + (started && !paused && !stopped ? Time.timeAsDouble - runningSince : 0);
        public string Status { get; protected set; } = "Starting";
        internal void Start(QuestBindings objects, Action<string> callback) {
            if (started || stopped) throw new InvalidOperationException("A node instance can only be started once.");
            Objects = objects ?? throw new ArgumentNullException(nameof(objects));
            started = true; runningSince = Time.timeAsDouble; thread = System.Threading.Thread.CurrentThread.ManagedThreadId; completed = callback; OnStart();
        }
        internal void Tick() { if (!stopped && !paused && !reported) OnTick(); }
        internal void SetPaused(bool value)
        {
            if (stopped || reported || paused == value) return;
            if (value) { accumulated = ActiveTime; paused = true; OnPause(); }
            else { paused = false; runningSince = Time.timeAsDouble; OnResume(); if (!stopped && pendingResult != null) Complete(pendingResult); }
        }
        protected virtual void OnPause() { }
        protected virtual void OnResume() { }
        protected virtual void OnStart() { }
        protected virtual void OnTick() { }
        protected virtual void OnStop(QuestStopReason reason) { }
        protected void Complete(string result)
        {
            if (stopped || reported) return;
            if (System.Threading.Thread.CurrentThread.ManagedThreadId != thread) throw new InvalidOperationException("Report results on the Unity main thread.");
            if (paused) { pendingResult ??= result; return; }
            result = pendingResult ?? result;
            var callback = completed;
            reported = true;
            callback?.Invoke(result);
        }
        public void Dispose() => Stop(QuestStopReason.Cancelled);
        internal void Stop(QuestStopReason reason)
        {
            if (stopped) return;
            accumulated = ActiveTime; stopped = true; completed = null;
            OnStop(reason);
        }
    }

    public abstract class QuestNodeLogic<TResult> : QuestNodeLogic where TResult : struct, Enum
    {
        public sealed override IReadOnlyList<string> Results => Enum.GetNames(typeof(TResult));
        protected void Finish(TResult result)
        {
            if (!Enum.IsDefined(typeof(TResult), result)) throw new ArgumentException("Undeclared result.");
            Complete(result.ToString());
        }
    }
}
