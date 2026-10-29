using DemonWeave.Unity;
using UnityEngine;

namespace DemonWeave.SampleGameplay
{
    [CreateAssetMenu(menuName = "DemonWeave/Actions/Move world object")]
    public sealed class MoveWorldObject : WorldAction
    {
        public string TargetRole = "gate";
        public Vector3 Offset = new Vector3(0, 3, 0);
        public override string CapabilityId => "world.transform.translate";
        public override string Validate(QuestBindings bindings)
        {
            if (!bindings.Find(TargetRole)) return "move.target: missing role";
            if (float.IsNaN(Offset.sqrMagnitude) || float.IsInfinity(Offset.sqrMagnitude) || Offset.magnitude > 20)
                return "move.offset: expected finite distance <= 20";
            return "";
        }
        public override ActionResult Execute(QuestBindings bindings)
        {
            var target = bindings.Require(TargetRole); target.transform.position += Offset;
            return new ActionResult(true, target.DisplayName + " moved by " + Offset);
        }
    }
}
