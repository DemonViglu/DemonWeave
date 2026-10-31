using System;
using DemonWeave.Unity;
using DemonWeave.World;
using UnityEngine;
using UnityEngine.Timeline;

namespace DemonWeave.AreaRescue
{
    [RequireComponent(typeof(EncounterSpawner))]
    public sealed class RescueEncounter : QuestEntity
    {
        public CombatActor Player;
        public EncounterDefinition Definition;
        public TimelineOperation Opening;
        public TimelineAsset Performance;
        public AnimationTrack DoorTrack;
        public SignalAsset ReleaseSignal;
        [SerializeField] private RescueCheckpoint checkpoint;
        private RescueEncounterSession session;
        public EncounterSpawner Spawner => GetComponent<EncounterSpawner>();
        public Guid AttemptId => session?.Id ?? Guid.Empty;
        public GameObject Companion => session?.Companion;
        public bool CanRescue => session?.RescueAvailable ?? false;
        public string Feedback { get; private set; } = "Enter the courtyard.";
        public event Action Ended;

        [QuestCondition("prepared", Label = "Opening prepared")]
        public bool IsPrepared() => session?.Prepared ?? false;
        [QuestCondition("cleared", Label = "All encounter enemies defeated")]
        public bool AreGuardsDefeated() => session?.Cleared ?? false;
        [QuestCondition("player-defeated", Label = "Player defeated during encounter")]
        public bool IsPlayerDefeated() => session?.Defeated ?? false;
        [QuestCondition("rescued", Label = "Companion released")]
        public bool IsCompanionRescued() => session?.Rescued ?? false;
        [QuestCondition("dialogue-finished", Label = "Rescue dialogue completed")]
        public bool IsDialogueFinished() => session?.DialogueFinished ?? false;

        [QuestAction("rescue.prepare", Label = "Open gate and prepare encounter", Validator = nameof(ValidateOpening))]
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
        [QuestAction("rescue.enable", Label = "Enable companion rescue")]
        public ActionResult EnableRescue() => session?.EnableRescue() ?? new ActionResult(false, "Encounter has not started.");
        public bool ReleaseCompanion(Guid attempt) => attempt == AttemptId && session?.BeginRescue() == true;
        public bool FinishDialogue(Guid attempt)
        {
            if (attempt != AttemptId || session?.FinishDialogue() != true) return false;
            session.StopWatchingPlayer(); return true;
        }
        [QuestAction("rescue.restore", Label = "Restore encounter checkpoint", Validator = nameof(CheckCheckpoint))]
        public void RestoreCheckpoint() => checkpoint.Restore();
        [QuestAction("rescue.wait-retry", Label = "Wait for player to retry", Validator = nameof(CheckCheckpoint))]
        public void WaitForRetry() => checkpoint.WaitForRetry();
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


