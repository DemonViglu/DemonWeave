using System;
using DemonWeave.Unity;
namespace DemonWeave.AreaRescue
{
    public enum CombatResult { Cleared, Defeated }
    [QuestLogic("rescue.combat", "Fight prison guards")]
    public sealed class RescueCombatLogic : QuestNodeLogic<CombatResult>
    {
        private RescueEncounter encounter;
        private Guid attempt;
        public override string CheckConfiguration(QuestBindings objects) =>
            objects.RequireOwner<RescueEncounter>().Player ? "" : "Player is missing.";
        protected override void OnStart()
        {
            encounter = Objects.RequireOwner<RescueEncounter>();
            attempt = encounter.CurrentAttempt();
        }
        protected override void OnTick()
        {
            var state = encounter.ReadCombat(attempt);
            Status = encounter.Feedback;
            if (state == EncounterProgress.Defeated) Finish(CombatResult.Defeated);
            else if (state == EncounterProgress.Ready) Finish(CombatResult.Cleared);
        }
        protected override void OnStop(QuestStopReason reason)
        { if (reason != QuestStopReason.Completed && encounter) encounter.Cancel(attempt); }
    }
}
