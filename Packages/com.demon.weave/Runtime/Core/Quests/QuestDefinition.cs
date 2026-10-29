using System;
using System.Collections.Generic;
using System.Linq;

namespace DemonWeave.Quests
{
    // Local model names; not a Genshin wire enum or reconstructed server policy.
    public enum QuestStatus { Waiting, Active, Completed, Failed, Cancelled }
    public enum RuleCombination { All, Any }
    public enum ConditionKind { Fact, QuestState, All, Any }
    public enum ContentKind { EventCount, Fact, QuestState }
    public enum QuestPhase { Begin, Finish, Fail }

    [Serializable]
    public sealed class QuestCondition
    {
        public ConditionKind Kind;
        public string Subject = ""; // Fact: bound entity role; QuestState: stable quest ID.
        public string Key = "";
        public bool Expected = true;
        public QuestStatus State = QuestStatus.Completed;
        public List<QuestCondition> Children = new List<QuestCondition>();
        public QuestCondition Copy() => new QuestCondition { Kind = Kind, Subject = Subject, Key = Key,
            Expected = Expected, State = State, Children = Children?.Select(x => x?.Copy()).ToList() };
    }

    [Serializable]
    public sealed class QuestContent
    {
        public string Id = "";
        public string Label = "";
        public ContentKind Kind;
        public string Subject = "";
        public string Key = "";
        public int Required = 1;
        public bool Expected = true;
        public QuestStatus State = QuestStatus.Completed;
        public QuestContent Copy() => (QuestContent)MemberwiseClone();
    }

    [Serializable]
    public sealed class QuestExec
    {
        public string Id = "";
        public string CapabilityId = "";
        // Opaque to Quest. The host validates/decodes the selected capability's typed argument schema.
        public string ArgumentsJson = "{}";
        public QuestExec Copy() => (QuestExec)MemberwiseClone();
    }

    [Serializable]
    public sealed class QuestDefinition
    {
        public string Id = "";
        public string Title = "";
        public string ParentId = "";
        public QuestCondition Accept = new QuestCondition { Kind = ConditionKind.All };
        public RuleCombination FinishCombination = RuleCombination.All;
        public RuleCombination FailCombination = RuleCombination.Any;
        public List<QuestContent> Finish = new List<QuestContent>();
        public List<QuestContent> Fail = new List<QuestContent>();
        public List<QuestExec> BeginExec = new List<QuestExec>();
        public List<QuestExec> FinishExec = new List<QuestExec>();
        public List<QuestExec> FailExec = new List<QuestExec>();
        // Explicit local content policy, never inferred from ParentId or visual order.
        public bool CancelChildrenOnEnd;
        public QuestDefinition Copy() => new QuestDefinition { Id = Id, Title = Title, ParentId = ParentId,
            Accept = Accept?.Copy(), FinishCombination = FinishCombination, FailCombination = FailCombination,
            Finish = Finish?.Select(x => x?.Copy()).ToList(), Fail = Fail?.Select(x => x?.Copy()).ToList(),
            BeginExec = BeginExec?.Select(x => x?.Copy()).ToList(), FinishExec = FinishExec?.Select(x => x?.Copy()).ToList(),
            FailExec = FailExec?.Select(x => x?.Copy()).ToList(), CancelChildrenOnEnd = CancelChildrenOnEnd };
    }

    public interface IQuestWorld
    {
        // Missing is distinct from false: an absent/destroyed binding cannot satisfy Expected=false.
        bool? ReadFact(string role, string key);
    }

    public readonly struct QuestWorldEvent
    {
        public Guid Id { get; }
        public string Subject { get; }
        public string Key { get; }
        public QuestWorldEvent(Guid id, string subject, string key) { Id = id; Subject = subject; Key = key; }
    }
}
