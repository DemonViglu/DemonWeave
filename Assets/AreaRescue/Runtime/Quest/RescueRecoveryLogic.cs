using DemonWeave.Unity;
namespace DemonWeave.AreaRescue
{
    public enum RecoveryMode { RestoreImmediately, WaitForPlayer }
    [QuestLogic("rescue.recovery", "Recover encounter")]
    public sealed class RescueRecoveryLogic : QuestNodeLogic
    {
        [ShowInQuest] public RecoveryMode mode;
        public override string Comment => "Restores or waits for the checkpoint. Ends this run; no outgoing result.";
        public override string CheckConfiguration(QuestBindings objects) => objects.RequireOwner<RescueEncounter>().CheckCheckpoint();
        protected override void OnStart()
        {
            var encounter = Objects.RequireOwner<RescueEncounter>();
            Status = mode == RecoveryMode.WaitForPlayer ? "Waiting for checkpoint retry" : "Restoring checkpoint";
            if (mode == RecoveryMode.WaitForPlayer) encounter.WaitForRetry();
            else encounter.RestoreCheckpoint();
        }
    }
}
