using System;
using System.Collections.Generic;
using DemonWeave.Quests;
using DemonWeave.Unity;

namespace DemonWeave.Editor
{
    public static class QuestOutcomeAuthoring
    {
        // Authoring sugar over existing conditions/objectives/Exec, not an additional runtime graph.
        public static QuestStage CreateBranch(WorldSignal outcome, CapabilityDescriptor action)
        {
            if (outcome.IsEvent) throw new ArgumentException("Branch activation needs a retained outcome fact; transient events require a separate event policy.");
            if (action == null) throw new ArgumentNullException(nameof(action));
            return new QuestStage {
                Id = "result-" + Guid.NewGuid().ToString("N"), Title = outcome.Label, Required = false,
                OpenWhen = new QuestCondition { Kind = ConditionKind.Fact, Subject = outcome.Role, Key = outcome.Key },
                Objectives = new List<QuestContent> { new QuestContent { Id = Guid.NewGuid().ToString("N"), Label = outcome.Label, Kind = ContentKind.Fact, Subject = outcome.Role, Key = outcome.Key } },
                FinishExec = new List<QuestExec> { new QuestExec { Id = Guid.NewGuid().ToString("N"), CapabilityId = action.Id, ArgumentsJson = action.DefaultJson() } }
            };
        }
    }
}
