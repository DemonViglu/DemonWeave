using System;
using System.Collections.Generic;
using System.Linq;

namespace DemonWeave.Quests
{
    // Local model names; not a Genshin wire enum or reconstructed server policy.
    public enum QuestStatus { Waiting, Active, Completed, Cancelled }
    public enum RuleCombination { All, Any }
    public enum ConditionKind { QuestState, All, Any, StageState, StageResult, WorldFact, ExternalQuestCompleted }
    [Serializable]
    public sealed class QuestCondition
    {
        public ConditionKind Kind;
        public string Subject = ""; // Stable fact, quest or node ID, according to Kind.
        public string Key = "";
        public bool Expected = true;
        public QuestStatus State = QuestStatus.Completed;
        public List<QuestCondition> Children = new List<QuestCondition>();
        public QuestCondition Copy() => new QuestCondition { Kind = Kind, Subject = Subject, Key = Key,
            Expected = Expected, State = State, Children = Children?.Select(x => x?.Copy()).ToList() };
    }

    [Serializable]
    public sealed class QuestDefinition
    {
        public string Id = "";
        public string Title = "";
        public string ParentId = "";
        public string StartComment = "";
        public string FinishComment = "";
        public QuestCondition Accept = new QuestCondition { Kind = ConditionKind.All };
        public QuestCondition RunWhen = new QuestCondition { Kind = ConditionKind.All };
        public QuestCondition InterruptWhen;
        public RuleCombination StageCompletion = RuleCombination.All;
        public List<QuestStage> Stages = new List<QuestStage>();
        // Explicit local content policy, never inferred from ParentId or visual order.
        public bool CancelChildrenOnEnd;
        public QuestDefinition Copy() => new QuestDefinition { Id = Id, Title = Title, ParentId = ParentId, StartComment = StartComment, FinishComment = FinishComment,
            Accept = Accept?.Copy(), RunWhen = RunWhen?.Copy(), InterruptWhen = InterruptWhen?.Copy(), StageCompletion = StageCompletion,
            CancelChildrenOnEnd = CancelChildrenOnEnd,
            Stages = Stages?.Select(x => x?.Copy()).ToList() };
    }

    public interface IQuestWorld
    {
        // A missing fact never satisfies a false requirement.
        bool? ReadWorldFact(string id);
        bool IsQuestCompleted(string collectionId, string questId);
    }
}
