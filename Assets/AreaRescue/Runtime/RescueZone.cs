using DemonWeave.World;
using UnityEngine;

namespace DemonWeave.AreaRescue
{
    [RequireComponent(typeof(BoxCollider), typeof(Rigidbody))]
    public sealed class RescueZone : MonoBehaviour
    {
        public AreaRescueScenario Scenario;
        private void OnTriggerEnter(Collider other)
        { if (Scenario) Scenario.EnterZone(this, other.GetComponentInParent<CombatActor>()); }
        private void OnTriggerStay(Collider other)
        { if (Scenario) Scenario.EnterZone(this, other.GetComponentInParent<CombatActor>()); }
    }
}
