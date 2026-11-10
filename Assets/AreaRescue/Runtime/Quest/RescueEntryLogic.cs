using System;
using DemonWeave.Unity;
namespace DemonWeave.AreaRescue
{
    public enum EntryResult { Entered }
    [QuestLogic("rescue.entry", "Enter courtyard")]
    public sealed class RescueEntryLogic : QuestNodeLogic<EntryResult>
    {
        [ShowInQuest] public RescueEntryZone zone;
        public override string CheckConfiguration(QuestBindings objects) => zone.Player ? "" : "Player is missing.";
        protected override void OnTick()
        {
            if (!zone || !zone.isActiveAndEnabled) throw new InvalidOperationException("Entry trigger is unavailable.");
            Status = "Waiting for player to enter the courtyard";
            if (zone.HasEntered()) Finish(EntryResult.Entered);
        }
    }
}
