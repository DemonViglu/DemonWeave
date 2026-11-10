using System;
using DemonWeave.World;
using UnityEngine;
using UnityEngine.Timeline;

namespace DemonWeave.AreaRescue
{
    public enum EncounterProgress { Running, Ready, Defeated }

    [RequireComponent(typeof(EncounterSpawner))]
    public sealed class RescueEncounter : MonoBehaviour
    {
        public CombatActor Player;
        public EncounterDefinition Definition;
        public TimelineOperation Opening;
        public TimelineAsset Performance;
        public AnimationTrack DoorTrack;
        public SignalAsset ReleaseSignal;
        [SerializeField] private RescueCheckpoint checkpoint;
        [Header("Defeat response")]
        [Tooltip("Shown on the HUD when this encounter waits for a manual retry.")]
        [TextArea(2, 4), SerializeField] private string retryMessage = "Defeated. Select RETRY CHECKPOINT.";
        private RescueEncounterSession session;
        public EncounterSpawner Spawner => GetComponent<EncounterSpawner>();
        public Guid AttemptId => session?.Id ?? Guid.Empty;
        public GameObject Companion => session?.Companion;
        public bool CanRescue => session?.RescueAvailable ?? false;
        public string Feedback { get; private set; } = "Enter the courtyard.";
        public event Action Ended;

        public bool IsPrepared() => session?.Prepared ?? false;
        public bool AreGuardsDefeated() => session?.Cleared ?? false;
        public bool IsPlayerDefeated() => session?.Defeated ?? false;
        public bool IsCompanionRescued() => session?.Rescued ?? false;
        public bool IsDialogueFinished() => session?.DialogueFinished ?? false;

        // The attempt ID identifies existing gameplay state; it is not another execution object.
        public Guid BeginOpening(Animator door, float timeout)
        {
            if (session != null) throw new InvalidOperationException("Encounter is already running. Restore the checkpoint before restarting.");
            var error = ValidateOpening(door, timeout);
            if (!string.IsNullOrEmpty(error)) throw new ArgumentException(error);
            session = new RescueEncounterSession(Spawner, Definition, Player, Opening, Performance, DoorTrack, ReleaseSignal,
                door, timeout, () => Feedback = "Player defeated.", message => Feedback = message);
            return session.Id;
        }
        public Guid CurrentAttempt()
        {
            if (!this || session == null) throw new InvalidOperationException("Encounter has not started.");
            return session.Id;
        }
        public Guid BeginCompanion()
        {
            var attempt = CurrentAttempt();
            var result = session.EnableRescue();
            if (!result.Success) throw new InvalidOperationException(result.Message);
            return attempt;
        }
        private RescueEncounterSession RequireAttempt(Guid attempt)
        {
            if (!this || session == null || attempt == Guid.Empty || session.Id != attempt)
                throw new InvalidOperationException("This encounter attempt has ended.");
            return session;
        }
        public EncounterProgress ReadOpening(Guid attempt)
        {
            var current = RequireAttempt(attempt);
            if (current.Defeated) return EncounterProgress.Defeated;
            if (!Opening || !Spawner) throw new InvalidOperationException("Opening gameplay component is unavailable.");
            if (Opening.State == TimelineOperationState.Failed) throw new InvalidOperationException(Opening.Error);
            if (!string.IsNullOrEmpty(Spawner.Error)) throw new InvalidOperationException(Spawner.Error);
            return current.Prepared ? EncounterProgress.Ready : EncounterProgress.Running;
        }
        public EncounterProgress ReadCombat(Guid attempt)
        {
            var current = RequireAttempt(attempt);
            return current.Defeated ? EncounterProgress.Defeated : current.Cleared ? EncounterProgress.Ready : EncounterProgress.Running;
        }
        public EncounterProgress ReadCompanion(Guid attempt)
        {
            var current = RequireAttempt(attempt);
            return current.Defeated ? EncounterProgress.Defeated : current.DialogueFinished ? EncounterProgress.Ready : EncounterProgress.Running;
        }
        public void Cancel(Guid attempt)
        {
            if (this && attempt != Guid.Empty && session?.Id == attempt) Clear();
        }
        [UnityEngine.Scripting.Preserve]
        public string ValidateOpening(Animator door, float timeout)
        {
            if (!door) return "Select a gate with Animator.";
            if (!float.IsFinite(timeout) || timeout <= 0) return "Timeout must be positive.";
            if (!Player || !Definition || !Opening || !Performance || !DoorTrack || !ReleaseSignal) return "Complete the encounter component's gameplay setup.";
            return Definition.Validate(Spawner.SpawnPoints?.Length ?? 0);
        }
        public bool ReleaseCompanion(Guid attempt) => attempt == AttemptId && session?.BeginRescue() == true;
        public bool FinishDialogue(Guid attempt)
        {
            if (attempt != AttemptId || session?.FinishDialogue() != true) return false;
            session.StopWatchingPlayer(); return true;
        }
        public void RestoreCheckpoint() => checkpoint.Restore();
        public void WaitForRetry() => checkpoint.WaitForRetry(retryMessage);
        [UnityEngine.Scripting.Preserve]
        public string CheckCheckpoint() => checkpoint ? "" : "Assign the checkpoint on this Gameplay encounter.";
        private void Update() => session?.Tick();
        public void Clear()
        {
            var previous = session; session = null;
            try { previous?.Dispose(); } finally { Feedback = "Enter the courtyard."; Ended?.Invoke(); }
        }
        private void OnDisable() => Clear();
    }
}


