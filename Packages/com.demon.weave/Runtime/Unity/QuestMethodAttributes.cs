using System;

namespace DemonWeave.Unity
{
    [AttributeUsage(AttributeTargets.Method, Inherited = true)]
    public sealed class QuestConditionAttribute : UnityEngine.Scripting.PreserveAttribute
    {
        public string Id { get; }
        public string Label { get; set; }
        public QuestConditionAttribute(string id = null) { Id = id; }
    }

    [AttributeUsage(AttributeTargets.Method, Inherited = true)]
    public sealed class QuestActionAttribute : UnityEngine.Scripting.PreserveAttribute
    {
        public string Id { get; }
        public string Label { get; set; }
        // Optional pure validator with the same parameters and a string result (empty means valid).
        public string Validator { get; set; }
        public QuestActionAttribute(string id = null) { Id = id; }
    }
}
