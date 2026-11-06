using DemonWeave.Unity;
using DemonWeave.World;
using UnityEngine;
namespace DemonWeave.AreaRescue
{
    public enum EntryResult { Entered }

    [RequireComponent(typeof(BoxCollider), typeof(Rigidbody))]
    public sealed class RescueEntryZone : QuestEntity<EntryResult>
    {
        public CombatActor Player;
        private bool entered;
        public bool HasEntered() => entered;
        public override string CheckConfiguration() => Player ? "" : "Player is missing.";
        public override QuestExecution CreateExecution() => new Execution(this);
        private sealed class Execution : QuestExecution<EntryResult>
        {
            private readonly RescueEntryZone zone;
            public Execution(RescueEntryZone zone) => this.zone = zone;
            protected override void OnTick()
            {
                if (!zone || !zone.isActiveAndEnabled) throw new System.InvalidOperationException("Entry trigger is unavailable.");
                Status = "Waiting for player to enter the courtyard";
                if (zone.HasEntered()) Finish(EntryResult.Entered);
            }
        }
        public void ResetEntry() => entered = false;
        private void OnTriggerEnter(Collider other) => Observe(other);
        private void OnTriggerStay(Collider other) => Observe(other);
        private void Observe(Collider other) { if (Player && other.GetComponentInParent<CombatActor>() == Player) entered = true; }
    }
}
