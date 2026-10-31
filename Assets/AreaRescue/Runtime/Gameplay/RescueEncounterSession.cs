using System;
using System.Linq;
using DemonWeave.Unity;
using DemonWeave.World;
using UnityEngine;
using UnityEngine.Timeline;

namespace DemonWeave.AreaRescue
{
    // One encounter owns its generated objects, facts, callbacks and cancellation lifetime.
    public sealed class RescueEncounterSession : IDisposable
    {
        private readonly AttemptBindings objects = new AttemptBindings();
        private readonly EncounterSpawner spawner;
        private readonly TimelineOperation timeline;
        private readonly CombatActor player;
        private readonly Action changed;
        private readonly Action<string> feedback;
        private readonly Transform door;
        private readonly Vector3 doorPosition;
        private readonly Quaternion doorRotation;
        private bool watching, closed;
        public Guid Id => objects.Id;
        public bool Prepared { get; private set; }
        public bool Defeated { get; private set; }
        public bool RescueAvailable { get; private set; }
        public bool Rescued { get; private set; }
        public bool DialogueFinished { get; private set; }
        public bool Cleared => !closed && !Defeated && spawner && spawner.IsCleared;
        public GameObject Companion => !closed && spawner ? spawner.Companion : null;
        public TimelineOperation Timeline => timeline;
        public Transform Door => door;

        public RescueEncounterSession(EncounterSpawner spawner, EncounterDefinition definition, CombatActor player,
            TimelineOperation timeline, TimelineAsset opening, AnimationTrack track, SignalAsset signal,
            GameObject doorObject, float timeout, Action changed, Action<string> feedback)
        {
            if (!spawner || !definition || !player || !timeline || !opening || !track || !signal || !doorObject || !doorObject.GetComponent<Animator>())
                throw new ArgumentException("Encounter requires its spawner, definition, player, Timeline, track, release signal and door Animator.");
            if (!opening.GetOutputTracks().Contains(track)) throw new ArgumentException("Door track does not belong to the encounter Timeline.");
            this.spawner = spawner; this.player = player; this.timeline = timeline;
            this.changed = changed; this.feedback = feedback;
            door = doorObject.transform; doorPosition = door.localPosition; doorRotation = door.localRotation;
            try
            {
                spawner.Prepare(definition, objects);
                player.Died += OnPlayerDied; watching = true;
                timeline.Director.SetGenericBinding(track, doorObject.GetComponent<Animator>());
                timeline.Begin(opening, signal, timeout);
                feedback("The gate is opening. Stay ready.");
            }
            catch { Dispose(); throw; }
        }
        public void Tick()
        {
            if (closed) return;
            if (timeline.ReleaseRequested && spawner.IsReady && !spawner.Attempt.IsReleased) spawner.Release();
            Prepared = spawner.IsReady && spawner.Attempt.IsReleased && timeline.State == TimelineOperationState.Completed;
            if (!string.IsNullOrEmpty(spawner.Error)) feedback("Preparation failed: " + spawner.Error);
            else if (timeline.State == TimelineOperationState.Failed) feedback(timeline.Error);
            else if (spawner.Attempt?.HasUnavailableRequiredMember == true) feedback("A required guard disappeared without a confirmed defeat. Restore the checkpoint.");
        }
        public ActionResult EnableRescue()
        {
            if (!Companion) return new ActionResult(false, "This encounter has no companion.");
            RescueAvailable = true; feedback("The guards are down. Speak to your companion.");
            return new ActionResult(true, "Rescue interaction available.");
        }
        public bool BeginRescue()
        {
            if (!RescueAvailable || !Companion || Rescued) return false;
            Rescued = true; Companion.transform.position = player.transform.position + Vector3.right * 1.5f; return true;
        }
        public bool FinishDialogue()
        {
            if (!Rescued || !Companion) return false;
            DialogueFinished = true; feedback("Your companion is safe."); return true;
        }
        private void OnPlayerDied(CombatActor actor)
        {
            if (closed || !watching || actor != player) return;
            Defeated = true; changed();
        }
        public void StopWatchingPlayer()
        { if (watching && player) player.Died -= OnPlayerDied; watching = false; }
        public void Dispose()
        {
            if (closed) return; closed = true; StopWatchingPlayer(); objects.Dispose();
            try { if (timeline) timeline.Cancel(); }
            finally
            {
                try { if (spawner) spawner.Cancel(); }
                finally { if (door) { door.localPosition = doorPosition; door.localRotation = doorRotation; } }
            }
        }
    }
}

