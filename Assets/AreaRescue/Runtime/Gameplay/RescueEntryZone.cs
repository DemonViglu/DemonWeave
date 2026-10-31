using DemonWeave.Unity;
using DemonWeave.World;
using UnityEngine;
namespace DemonWeave.AreaRescue
{
    [RequireComponent(typeof(BoxCollider), typeof(Rigidbody))]
    public sealed class RescueEntryZone : QuestEntity
    {
        public CombatActor Player;
        private bool entered;
        [QuestCondition("entered", Label = "Player entered the courtyard")]
        public bool HasEntered() => entered;
        public void ResetEntry() => entered = false;
        private void OnTriggerEnter(Collider other) => Observe(other);
        private void OnTriggerStay(Collider other) => Observe(other);
        private void Observe(Collider other) { if (Player && other.GetComponentInParent<CombatActor>() == Player) entered = true; }
    }
}
