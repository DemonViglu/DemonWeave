using System;
using DemonWeave.Unity;
using DemonWeave.World;
using UnityEngine;
using UnityEngine.Timeline;

namespace DemonWeave.AreaRescue
{
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

        public ActionResult Prepare(GameObject door, float timeout = 20)
        {
            if (session != null) return new ActionResult(false, "Encounter is already running. Restore the checkpoint before restarting.");
            session = new RescueEncounterSession(Spawner, Definition, Player, Opening, Performance, DoorTrack, ReleaseSignal,
                door, timeout, () => Feedback = "Player defeated.", message => Feedback = message);
            return new ActionResult(true, "Encounter started; wait for IsPrepared.");
        }
        [UnityEngine.Scripting.Preserve]
        public string ValidateOpening(GameObject door, float timeout)
        {
            if (!door || !door.GetComponent<Animator>()) return "Select a gate with Animator.";
            if (!float.IsFinite(timeout) || timeout <= 0) return "Timeout must be positive.";
            if (!Player || !Definition || !Opening || !Performance || !DoorTrack || !ReleaseSignal) return "Complete the encounter component's gameplay setup.";
            return Definition.Validate(Spawner.SpawnPoints?.Length ?? 0);
        }
        public ActionResult EnableRescue() => session?.EnableRescue() ?? new ActionResult(false, "Encounter has not started.");
        public bool ReleaseCompanion(Guid attempt) => attempt == AttemptId && session?.BeginRescue() == true;
        public bool FinishDialogue(Guid attempt)
        {
            if (attempt != AttemptId || session?.FinishDialogue() != true) return false;
            session.StopWatchingPlayer(); return true;
        }
        public void RestoreCheckpoint() => checkpoint.Restore();
        public void WaitForRetry() => checkpoint.WaitForRetry(retryMessage);
        [UnityEngine.Scripting.Preserve]
        public string CheckCheckpoint() => checkpoint ? "" : "Assign the internal checkpoint on this Entity.";
        private void Update() => session?.Tick();
        public void Clear()
        {
            var previous = session; session = null;
            try { previous?.Dispose(); } finally { Feedback = "Enter the courtyard."; Ended?.Invoke(); }
        }
        private void OnDisable() => Clear();
    }
}


