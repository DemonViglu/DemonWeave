using System;
using System.Collections.Generic;
using DemonWeave.Quests;

namespace DemonWeave.Unity
{
    public interface IQuestRecoveryAuthoring
    {
        string RecoveryTrigger { get; }
        string RecoveryOutcome { get; }
        string RecoveryProperty { get; }
    }
    public readonly struct WorldSignal
    {
        public string Role { get; }
        public string Key { get; }
        public string Label { get; }
        public bool IsEvent { get; }
        public Type ImplementationType { get; }
        public string MethodName { get; }
        public WorldSignal(string role, string key, string label, bool isEvent = false, Type implementationType = null, string methodName = null)
        { Role = role; Key = key; Label = label; IsEvent = isEvent; ImplementationType = implementationType; MethodName = methodName; }
    }

    // The editor depends on this adapter contract, never on lighthouse-specific classes.
    public interface IQuestAuthoringHost
    {
        QuestBookAsset BookAsset { get; set; }
        List<EntityBinding> BindingEntries { get; }
        QuestRun ActiveRun { get; }
        IReadOnlyList<string> Activity { get; }
        IReadOnlyList<WorldSignal> Signals { get; }
        CapabilityCatalog CreateCapabilities();
    }

    [AttributeUsage(AttributeTargets.Parameter)]
    public sealed class CapabilityParameterAttribute : Attribute
    {
        public string Label { get; }
        public CapabilityParameterAttribute(string label) { Label = label; }
    }
}

