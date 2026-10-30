using System;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Timeline;

namespace DemonWeave.World
{
    public enum TimelineOperationState { Idle, Running, Completed, Cancelled, Failed }

    public sealed class TimelineOperation : MonoBehaviour
    {
        public PlayableDirector Director;
        public Guid RunId { get; private set; }
        public TimelineOperationState State { get; private set; }
        public bool ReleaseRequested { get; private set; }
        public string Error { get; private set; }
        public event Action Changed;
        private double duration;
        private float deadline;
        private Action<PlayableDirector> stopped;
        private INotificationReceiver receiver;

        public void Begin(TimelineAsset timeline, SignalAsset releaseSignal, float timeoutSeconds)
        {
            Cancel();
            if (!Director || !timeline || !releaseSignal || !float.IsFinite(timeoutSeconds) || timeoutSeconds <= 0)
                throw new ArgumentException("Timeline, director, release signal and positive timeout required.");
            duration = timeline.duration;
            if (double.IsInfinity(duration) || double.IsNaN(duration) || duration <= 0) throw new ArgumentException("Timeline needs a finite positive duration.");
            RunId = Guid.NewGuid(); var run = RunId;
            ReleaseRequested = false; Error = ""; State = TimelineOperationState.Running;
            deadline = Time.unscaledTime + timeoutSeconds;
            // Hold the final pose. Stopping or destroying the graph is a cancellation, not normal completion.
            Director.playableAsset = timeline; Director.extrapolationMode = DirectorWrapMode.Hold; Director.time = 0;
            stopped = _ => { if (RunId == run && State == TimelineOperationState.Running) Fail("Timeline stopped before completion."); };
            Director.stopped += stopped;
            receiver = new ReleaseReceiver(this, run, releaseSignal);
            try
            {
                Director.Play();
                var graph = Director.playableGraph;
                for (int i = 0; i < graph.GetOutputCount(); i++) graph.GetOutput(i).AddNotificationReceiver(receiver);
                Director.Evaluate();
            }
            catch (Exception exception) { Fail(exception.Message); }
        }
        private void Update()
        {
            if (State != TimelineOperationState.Running) return;
            if (!Director) { Fail("Timeline director was destroyed."); return; }
            if (Time.unscaledTime > deadline) { Fail("Timeline timed out."); return; }
            if (Director.time + .0001 < duration) return;
            State = TimelineOperationState.Completed; Director.Pause(); Detach(); Changed?.Invoke();
        }
        private void Fail(string reason)
        {
            Error = reason; State = TimelineOperationState.Failed; Detach();
            if (Director) Director.Stop(); Changed?.Invoke();
        }
        public void Cancel()
        {
            bool wasRunning = State == TimelineOperationState.Running;
            RunId = Guid.Empty; Detach();
            if (Director) Director.Stop();
            if (wasRunning || State == TimelineOperationState.Completed) State = TimelineOperationState.Cancelled;
            ReleaseRequested = false;
        }
        private void Detach()
        {
            if (Director)
            {
                if (stopped != null) Director.stopped -= stopped;
                var graph = Director.playableGraph;
                if (graph.IsValid() && receiver != null)
                    for (int i = 0; i < graph.GetOutputCount(); i++) graph.GetOutput(i).RemoveNotificationReceiver(receiver);
            }
            stopped = null; receiver = null;
        }
        private void OnDisable() => Cancel();
        private sealed class ReleaseReceiver : INotificationReceiver
        {
            private readonly TimelineOperation owner;
            private readonly Guid run;
            private readonly SignalAsset signal;
            public ReleaseReceiver(TimelineOperation owner, Guid run, SignalAsset signal) { this.owner = owner; this.run = run; this.signal = signal; }
            public void OnNotify(Playable origin, INotification notification, object context)
            {
                if (owner.RunId != run || owner.State != TimelineOperationState.Running || owner.ReleaseRequested) return;
                if (!(notification is SignalEmitter emitter) || emitter.asset != signal) return;
                owner.ReleaseRequested = true; owner.Changed?.Invoke();
            }
        }
    }
}
