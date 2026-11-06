using DemonWeave.Unity;
using UnityEngine;

namespace DemonWeave.AreaRescue
{
    public enum RecoveryMode { RestoreImmediately, WaitForPlayer }
    public enum RecoveryResult { Requested }
    public sealed class RescueRecoveryEntity : QuestEntity<RecoveryResult>
    {
        [SerializeField] private RescueEncounter encounter;
        [ShowInQuest, SerializeField] private RecoveryMode mode;
        public override string CheckConfiguration() => encounter ? encounter.CheckCheckpoint() : "Encounter is missing.";
        public override QuestExecution CreateExecution() => new Execution(encounter, mode);
        private sealed class Execution : QuestExecution<RecoveryResult>
        {
            private readonly RescueEncounter encounter;
            private readonly RecoveryMode mode;
            public Execution(RescueEncounter encounter, RecoveryMode mode) { this.encounter = encounter; this.mode = mode; }
            protected override void OnStart()
            {
                // Recovery closes the current run and creates a fresh attempt through the checkpoint.
                if (mode == RecoveryMode.WaitForPlayer) encounter.WaitForRetry();
                else encounter.RestoreCheckpoint();
                Finish(RecoveryResult.Requested);
            }
        }
    }
}
