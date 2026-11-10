using System;
using DemonWeave.Unity;
using UnityEngine;
namespace DemonWeave.AreaRescue
{
    public enum OpeningResult { Ready, Defeated }
    [QuestLogic("rescue.open", "Open gate and prepare encounter")]
    public sealed class RescueOpeningLogic : QuestNodeLogic<OpeningResult>
    {
        [ShowInQuest] public Animator door;
        [ShowInQuest] public float timeout = 20;
        private RescueEncounter encounter;
        private Guid attempt;
        public override string CheckConfiguration(QuestBindings objects) =>
            objects.RequireOwner<RescueEncounter>().ValidateOpening(door, timeout);
        protected override void OnStart()
        {
            encounter = Objects.RequireOwner<RescueEncounter>();
            attempt = encounter.BeginOpening(door, timeout);
        }
        protected override void OnTick()
        {
            var state = encounter.ReadOpening(attempt);
            Status = encounter.Feedback;
            if (state == EncounterProgress.Defeated) Finish(OpeningResult.Defeated);
            else if (state == EncounterProgress.Ready) Finish(OpeningResult.Ready);
        }
        protected override void OnStop(QuestStopReason reason)
        { if (reason != QuestStopReason.Completed && encounter) encounter.Cancel(attempt); }
    }
}
