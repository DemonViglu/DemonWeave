using DemonWeave.Unity;
using UnityEngine;

namespace DemonWeave.AreaRescue
{
    public enum RecoveryMode { RestoreImmediately, WaitForPlayer }
    public sealed class RescueRecoveryEntity : QuestEntity
    {
        [SerializeField] private RescueEncounter encounter;
        [ShowInQuest, SerializeField] private RecoveryMode mode;
        public override string Comment => "Restores or waits for the checkpoint. Ends this run; no outgoing result.";
        public override string CheckConfiguration() => encounter ? encounter.CheckCheckpoint() : "Encounter is missing.";
        public override QuestExecution CreateExecution() => new Execution(encounter, mode);
        private sealed class Execution : QuestExecution
        {
            private readonly RescueEncounter encounter;
            private readonly RecoveryMode mode;
            public Execution(RescueEncounter encounter, RecoveryMode mode) { this.encounter = encounter; this.mode = mode; }
            protected override void OnStart()
            {
                Status = mode == RecoveryMode.WaitForPlayer ? "Waiting for checkpoint retry" : "Restoring checkpoint";
                // Recovery closes the current run and creates a fresh attempt through the checkpoint.
                if (mode == RecoveryMode.WaitForPlayer) encounter.WaitForRetry();
                else encounter.RestoreCheckpoint();
            }
        }
    }
}
