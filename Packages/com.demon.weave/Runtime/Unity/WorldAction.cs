using UnityEngine;

namespace DemonWeave.Unity
{
    public readonly struct ActionResult
    {
        public bool Success { get; }
        public string Message { get; }
        public ActionResult(bool success, string message) { Success = success; Message = message; }
    }

    // Reusable Unity gameplay action, independent of quest IDs and lifecycle decisions.
    public abstract class WorldAction : ScriptableObject
    {
        public abstract string CapabilityId { get; }
        public abstract string Validate(QuestBindings bindings);
        public abstract ActionResult Execute(QuestBindings bindings);
    }
}
