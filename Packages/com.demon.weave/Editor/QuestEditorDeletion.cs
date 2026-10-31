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
            var source = quest.Stages.FirstOrDefault(s => s.Id == from); var target = quest.Stages.FirstOrDefault(s => s.Id == to);
            if (source == null || target == null || target.After.Contains(from)) return;
            var visited = new HashSet<string>();
            bool DependsOn(string id) => id == to || visited.Add(id) && quest.Stages.First(s => s.Id == id).After.Any(DependsOn);
            if (DependsOn(from)) { notice = "Connection rejected: cyclic dependency."; UpdateStatus(); return; }
            Edit("Connect Node Dependency", () => target.After.Add(from));
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
            var dependents = remaining.Where(s => s.After.Any(ids.Contains) || ConditionUses(s.OpenWhen)
                || s.Objectives.Any(GoalUses) || s.Routes.Any(r => ConditionUses(r.AvailableWhen) || r.Objectives.Any(GoalUses))
                || s.Switches.Any(r => ConditionUses(r.When))).Select(s => s.Title).ToArray();
            if (dependents.Length > 0 || quest.Finish.Concat(quest.Fail).Any(GoalUses))
            { notice = "Cannot delete: " + (dependents.Length > 0 ? string.Join("、", dependents) : "Quest completion/failure rules") + " still references these nodes. Update the references first."; UpdateStatus(); return; }
            selectedStageId = null;
            Edit("Delete Nodes", () => quest.Stages.RemoveAll(s => ids.Contains(s.Id)));
        }
    }
}
