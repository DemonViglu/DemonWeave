using System.Collections.Generic;
using System.Linq;
using DemonWeave.Quests;
using UnityEditor;

namespace DemonWeave.Editor
{
    public sealed partial class QuestEditorWindow
    {
        private void ConnectStages(QuestDefinition quest, string from, string to)
        {
            if (EditorApplication.isPlaying) return;
            if (QuestStageGraph.ParseResultPort(from, out var sourceId, out var result))
            {
                var entitySource = quest.Stages.Single(s => s.Id == sourceId);
                if (to == "$finish") {
                    Edit("Connect Finish", () => { entitySource.Required = true; entitySource.FinishResult = result; }); return;
                }
                var branchTarget = quest.Stages.Single(s => s.Id == to);
                bool HasOtherRule(QuestCondition c) => c.Kind != ConditionKind.All && c.Kind != ConditionKind.Any && c.Kind != ConditionKind.StageResult || c.Children.Any(HasOtherRule);
                if (HasOtherRule(branchTarget.OpenWhen)) { notice = "This node has custom start conditions. Preserve or migrate them before replacing its result input."; UpdateStatus(); return; }
                var visitedResults = new HashSet<string>();
                bool ReachesTarget(string id) => id == to || visitedResults.Add(id) && quest.Stages.Where(n => n.Id == id).Any(n => n.After.Concat(QuestStageGraph.ResultConditions(n.OpenWhen).Select(c => c.Subject)).Any(ReachesTarget));
                if (ReachesTarget(sourceId)) { notice = "Connection rejected: cyclic dependency."; UpdateStatus(); return; }
                var incoming = QuestStageGraph.ResultConditions(branchTarget.OpenWhen).Select(c => c.Copy()).ToList();
                if (incoming.Any(c => c.Subject == sourceId && c.Key == result)) return;
                incoming.Add(new QuestCondition { Kind = ConditionKind.StageResult, Subject = sourceId, Key = result });
                Edit("Connect Result", () => branchTarget.OpenWhen = new QuestCondition { Kind = branchTarget.Control == QuestControl.All ? ConditionKind.All : ConditionKind.Any, Children = incoming });
                return;
            }
            var source = quest.Stages.FirstOrDefault(s => s.Id == from); var target = quest.Stages.FirstOrDefault(s => s.Id == to);
            if (from == "$start" && target != null) { Edit("Make Entry Node", () => { target.After.Clear(); if (target.UsesEntityExecution()) target.OpenWhen = new QuestCondition { Kind = ConditionKind.All }; else EnableEntry(target); }); return; }
            if (source != null && source.UsesEntityExecution() && source.Results.Count == 0) return;
            if (to == "$finish" && source != null) { Edit("Require Node For Completion", () => source.Required = true); return; }
            if (source == null || target == null || target.After.Contains(from)) return;
            var visited = new HashSet<string>();
            bool DependsOn(string id) => id == to || visited.Add(id) && quest.Stages.First(s => s.Id == id).After.Any(DependsOn);
            if (DependsOn(from)) { notice = "Connection rejected: cyclic dependency."; UpdateStatus(); return; }
            Edit("Connect Node Dependency", () => { target.After.Add(from); EnableEntry(target); });
        }
        private void DisconnectStages(QuestDefinition quest, string from, string to)
        {
            if (EditorApplication.isPlaying) return;
            if (QuestStageGraph.ParseResultPort(from, out var sourceId, out var result)) {
                if (to == "$finish") { Edit("Disconnect Finish", () => { var node = quest.Stages.Single(s => s.Id == sourceId); node.Required = false; node.FinishResult = ""; }); return; }
                var target = quest.Stages.Single(s => s.Id == to);
                Edit("Disconnect Result", () => target.OpenWhen = new QuestCondition { Kind = target.Control == QuestControl.All && QuestStageGraph.ResultConditions(target.OpenWhen).Count() > 1 ? ConditionKind.All : ConditionKind.Any,
                    Children = QuestStageGraph.ResultConditions(target.OpenWhen).Where(c => c.Subject != sourceId || c.Key != result).Select(c => c.Copy()).ToList() });
                return;
            }
            if (from == "$start")
            {
                Edit("Disconnect Entry", () => DisableEntry(quest.Stages.Single(s => s.Id == to))); return;
            }
            if (to == "$finish") Edit("Make Node Optional", () => quest.Stages.Single(s => s.Id == from).Required = false);
            else Edit("Remove Node Dependency", () => {
                var node = quest.Stages.Single(s => s.Id == to);
                node.After.Remove(from);
                if (node.After.Count == 0) DisableEntry(node);
            });
        }
        private static void DisableEntry(QuestStage node)
        {
            if (QuestStageGraph.EntryBlocked(node.OpenWhen)) return;
            if (node.OpenWhen.Kind != ConditionKind.All)
                node.OpenWhen = new QuestCondition { Kind = ConditionKind.All, Children = new List<QuestCondition> { node.OpenWhen } };
            node.OpenWhen.Children.Add(new QuestCondition { Kind = ConditionKind.Any });
        }
        private static void EnableEntry(QuestStage node)
        {
            if (node.OpenWhen.Kind == ConditionKind.Any && node.OpenWhen.Children.Count == 0)
                node.OpenWhen = new QuestCondition { Kind = ConditionKind.All };
            else if (node.OpenWhen.Kind == ConditionKind.All)
                node.OpenWhen.Children.RemoveAll(c => c.Kind == ConditionKind.Any && c.Children.Count == 0);
        }
        private void DeleteQuests(IReadOnlyList<string> ids)
        {
            if (EditorApplication.isPlaying) return;
            var references = book.Quests.Where(q => !ids.Contains(q.Id) && ids.Any(id => QuestAuthoringChecks.References(q, id))).Select(q => q.Title).ToArray();
            if (references.Length > 0) { notice = "Referenced by: " + string.Join("、", references) + ". Update those prerequisites or objectives first."; UpdateStatus(); return; }
            selectedStageId = null;
            Edit("Delete Quest", () => book.Quests.RemoveAll(q => ids.Contains(q.Id)));
        }
        private void DeleteStages(QuestDefinition quest, IReadOnlyList<string> ids)
        {
            if (EditorApplication.isPlaying) return;
            bool ConditionUses(QuestCondition c) => c != null && (c.Kind == ConditionKind.StageState && ids.Contains(c.Subject) || c.Children != null && c.Children.Any(ConditionUses));
            bool GoalUses(QuestContent c) => c.Kind == ContentKind.StageState && ids.Contains(c.Subject);
            var remaining = quest.Stages.Where(s => !ids.Contains(s.Id)).ToArray();
            var dependents = remaining.Where(s => s.After.Any(ids.Contains) || ConditionUses(s.OpenWhen) || QuestStageGraph.ResultConditions(s.OpenWhen).Any(c => ids.Contains(c.Subject))
                || s.Objectives.Any(GoalUses) || s.Routes.Any(r => ConditionUses(r.AvailableWhen) || r.Objectives.Any(GoalUses))
                || s.Switches.Any(r => ConditionUses(r.When))).Select(s => s.Title).ToArray();
            if (dependents.Length > 0 || quest.Finish.Concat(quest.Fail).Any(GoalUses))
            { notice = "Cannot delete: " + (dependents.Length > 0 ? string.Join("、", dependents) : "Quest completion/failure rules") + " still references these nodes. Update the references first."; UpdateStatus(); return; }
            selectedStageId = null;
            Edit("Delete Nodes", () => quest.Stages.RemoveAll(s => ids.Contains(s.Id)));
        }
    }
}
