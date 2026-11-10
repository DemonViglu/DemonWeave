using System;
using System.Collections.Generic;
using UnityEngine;

namespace DemonWeave.Unity
{
    // An Entity is reusable configuration. Each node receives its own execution object.
    public abstract class QuestEntity<TResult> : QuestEntity where TResult : struct, Enum
    {
        public sealed override IReadOnlyList<string> Results => Enum.GetNames(typeof(TResult));
        public abstract override QuestExecution CreateExecution();
    }

    public enum QuestStopReason { Completed, Cancelled, Faulted }

    public abstract class QuestExecution : IDisposable
    {
        private Action<string> completed;
        private bool stopped, started, reported;
        private int thread;
        private bool paused;
        private double accumulated, runningSince;
        private string pendingResult;
        public bool IsPaused => paused;
        protected double ActiveTime => accumulated + (started && !paused && !stopped ? Time.timeAsDouble - runningSince : 0);
        public string Status { get; protected set; } = "Starting";
        internal void Start(Action<string> callback) {
            if (started || stopped) throw new InvalidOperationException("CreateExecution must return a new execution each time.");
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

    public abstract class QuestExecution<TResult> : QuestExecution where TResult : struct, Enum
    {
        protected void Finish(TResult result)
        {
            if (!Enum.IsDefined(typeof(TResult), result)) throw new ArgumentException("Undeclared result.");
            Complete(result.ToString());
        }
    }
}
