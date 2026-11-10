using DemonWeave.Unity;
using DemonWeave.World;
using UnityEngine;
namespace DemonWeave.AreaRescue
{

    [RequireComponent(typeof(BoxCollider), typeof(Rigidbody))]
    public sealed class RescueEntryZone : MonoBehaviour
    {
        public CombatActor Player;
        private bool entered;
        public bool HasEntered() => entered;
        public void ResetEntry() => entered = false;
        private void OnTriggerEnter(Collider other) => Observe(other);
        private void OnTriggerStay(Collider other) => Observe(other);
        private void Observe(Collider other) { if (Player && other.GetComponentInParent<CombatActor>() == Player) entered = true; }
    }
}
