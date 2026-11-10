using System;
using DemonWeave.Unity;
namespace DemonWeave.AreaRescue
{
    public enum CompanionResult { Rescued, Defeated }
    [QuestLogic("rescue.companion", "Rescue companion")]
    public sealed class RescueCompanionLogic : QuestNodeLogic<CompanionResult>
    {
        private RescueEncounter encounter;
        private Guid attempt;
        public override string CheckConfiguration(QuestBindings objects) =>
            objects.RequireOwner<RescueEncounter>().Player ? "" : "Player is missing.";
        protected override void OnStart()
        {
            encounter = Objects.RequireOwner<RescueEncounter>();
            attempt = encounter.BeginCompanion();
        }
        protected override void OnTick()
        {
            var state = encounter.ReadCompanion(attempt);
            Status = encounter.Feedback;
            if (state == EncounterProgress.Defeated) Finish(CompanionResult.Defeated);
            else if (state == EncounterProgress.Ready) Finish(CompanionResult.Rescued);
        }
        protected override void OnStop(QuestStopReason reason)
        { if (reason != QuestStopReason.Completed && encounter) encounter.Cancel(attempt); }
    }
}
