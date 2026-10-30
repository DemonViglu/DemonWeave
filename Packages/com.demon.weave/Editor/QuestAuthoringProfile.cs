using System;
using System.Collections.Generic;
using DemonWeave.Quests;
using DemonWeave.Unity;
using UnityEngine;

namespace DemonWeave.Editor
{
    // Optional editor extension. Gameplay owns vocabulary and bindings; the package owns the UI.
    public abstract class QuestAuthoringProfile
    {
        public abstract bool Supports(IQuestAuthoringHost host);
        public abstract IReadOnlyList<QuestAuthoringResource> Resources(IQuestAuthoringHost host);
        public virtual IEnumerable<string> ResourcesFor(IQuestAuthoringHost host, QuestStage stage) { yield break; }
        public virtual string ValidateResource(IQuestAuthoringHost host, string id) => "";
        public virtual string ProducerFor(IQuestAuthoringHost host, QuestCondition condition) => null;
        public virtual IEnumerable<WorldSignal> Outcomes(IQuestAuthoringHost host, string capability) { yield break; }
    }

    public sealed class QuestAuthoringResource
    {
        public string Id, Label, Description;
        public UnityEngine.Object Owner;
        public string PropertyPath;
        public string[] Settings = Array.Empty<string>();
        public string[] SettingLabels = Array.Empty<string>();
        // A named output belongs to an existing capability/attempt, never a guessed scene object ID.
        public string ProducerCapability;
    }
}
