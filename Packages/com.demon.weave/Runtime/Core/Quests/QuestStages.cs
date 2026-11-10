using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace DemonWeave.Quests
{
    public enum QuestControl { None, Parallel, All, Any }

    [Serializable]
    public sealed class QuestStage
    {
        public string Id = "";
        public string LogicId = "";
        public JObject Parameters = new JObject();
        public QuestControl Control;
        public List<string> Results = new List<string>();
        public string Title = "";
        public string Comment; // null inherits the logic comment; empty explicitly hides it.
        public bool Required = true;
        public string FinishResult = "";
        public List<string> After = new List<string>();
        public QuestCondition OpenWhen = new QuestCondition { Kind = ConditionKind.All };
        public QuestCondition RunWhen = new QuestCondition { Kind = ConditionKind.All };
        public QuestCondition InterruptWhen;
        public bool UsesLogicExecution() => Control == QuestControl.None;
        public QuestStage Copy() => new QuestStage { Id = Id, LogicId = LogicId, Parameters = (JObject)Parameters?.DeepClone(), Control = Control, Results = Results?.ToList(), Title = Title, Comment = Comment, Required = Required, FinishResult = FinishResult,
            After = After?.ToList(), OpenWhen = OpenWhen?.Copy(), RunWhen = RunWhen?.Copy(), InterruptWhen = InterruptWhen?.Copy() };
    }
    public sealed class QuestStageInstance
    {
        internal readonly QuestStage Definition;
        public string Id => Definition.Id;
        public string Title => Definition.Title;
        public bool UsesLogic => Definition.UsesLogicExecution();
        public QuestStage DefinitionSnapshot() => Definition.Copy();
        public QuestStatus State { get; internal set; } = QuestStatus.Waiting;
        public bool IsPaused { get; internal set; }
        public string EndReason { get; internal set; } = "";
        public string Result { get; internal set; } = "";
        internal QuestStageInstance(QuestStage definition) { Definition = definition; }
    }
}
