using System;
using System.Collections.Generic;
using System.Linq;

namespace DemonWeave.Quests
{
    [Serializable]
    public sealed class QuestStage
    {
        public string Id = "";
        public string Title = "";
        public bool Required = true;
        public List<string> After = new List<string>();
        public QuestCondition OpenWhen = new QuestCondition { Kind = ConditionKind.All };
        public List<QuestContent> Objectives = new List<QuestContent>();
        public RuleCombination Completion = RuleCombination.All;
        public List<QuestExec> BeginExec = new List<QuestExec>();
        public List<QuestExec> FinishExec = new List<QuestExec>();
        public List<QuestRoute> Routes = new List<QuestRoute>();
        public List<RouteSwitch> Switches = new List<RouteSwitch>();
        public QuestStage Copy() => new QuestStage { Id = Id, Title = Title, Required = Required,
            After = After?.ToList(), OpenWhen = OpenWhen?.Copy(), Objectives = Objectives?.Select(x => x?.Copy()).ToList(), Completion = Completion,
            BeginExec = BeginExec?.Select(x => x?.Copy()).ToList(), FinishExec = FinishExec?.Select(x => x?.Copy()).ToList(),
            Routes = Routes?.Select(x => x?.Copy()).ToList(), Switches = Switches?.Select(x => x?.Copy()).ToList() };
    }
    [Serializable]
    public sealed class QuestRoute
    {
        public string Id = "";
        public string Title = "";
        public QuestCondition AvailableWhen = new QuestCondition { Kind = ConditionKind.All };
        public List<QuestContent> Objectives = new List<QuestContent>();
        public RuleCombination Completion = RuleCombination.All;
        public List<QuestExec> SelectedExec = new List<QuestExec>();
        public QuestRoute Copy() => new QuestRoute { Id = Id, Title = Title, AvailableWhen = AvailableWhen?.Copy(),
            Objectives = Objectives?.Select(x => x?.Copy()).ToList(), Completion = Completion, SelectedExec = SelectedExec?.Select(x => x?.Copy()).ToList() };
    }
    [Serializable]
    public sealed class RouteSwitch
    {
        public string Id = "";
        public string From = "";
        public string To = "";
        public QuestCondition When = new QuestCondition { Kind = ConditionKind.All };
        public RouteSwitch Copy() => new RouteSwitch { Id = Id, From = From, To = To, When = When?.Copy() };
    }
    public readonly struct RouteChoice
    {
        public long Revision { get; }
        public string From { get; }
        public string To { get; }
        public string Source { get; }
        internal RouteChoice(long revision, string from, string to, string source) { Revision = revision; From = from; To = to; Source = source; }
    }
    public sealed class QuestStageInstance
    {
        internal readonly QuestStage Definition;
        internal readonly Dictionary<string, int> Counts = new Dictionary<string, int>(StringComparer.Ordinal);
        internal readonly Dictionary<string, Dictionary<string, int>> RouteCounts = new Dictionary<string, Dictionary<string, int>>(StringComparer.Ordinal);
        internal readonly HashSet<string> ClosedRoutes = new HashSet<string>(StringComparer.Ordinal);
        internal readonly List<RouteChoice> Choices = new List<RouteChoice>();
        public string Id => Definition.Id;
        public string Title => Definition.Title;
        public QuestStatus State { get; internal set; } = QuestStatus.Waiting;
        public string SelectedRouteId { get; internal set; } = "";
        public long RouteRevision { get; internal set; }
        public IReadOnlyList<RouteChoice> ChoiceHistory => Choices.AsReadOnly();
        public int Progress(string objectiveId) => Counts.TryGetValue(objectiveId, out var count) ? count : 0;
        public int RouteProgress(string routeId, string objectiveId) => RouteCounts.TryGetValue(routeId, out var values) && values.TryGetValue(objectiveId, out var count) ? count : 0;
        internal QuestStageInstance(QuestStage definition)
        {
            Definition = definition;
            foreach (var objective in definition.Objectives) Counts.Add(objective.Id, 0);
            foreach (var route in definition.Routes) RouteCounts.Add(route.Id, route.Objectives.ToDictionary(o => o.Id, o => 0, StringComparer.Ordinal));
        }
    }
}
