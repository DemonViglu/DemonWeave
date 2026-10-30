using System;
using System.Collections.Generic;
using System.Linq;

namespace DemonWeave.Quests
{
    public sealed partial class QuestRun
    {
        private bool RefreshStages(QuestInstance owner)
        {
            bool changed = false;
            foreach (var stage in owner.Stages)
            {
                if (stage.State == QuestStatus.Waiting)
                {
                    if (!stage.Definition.After.All(id => owner.Stage(id)?.State == QuestStatus.Completed) || !Evaluate(stage.Definition.OpenWhen, owner)) continue;
                    stage.State = QuestStatus.Active; StageNotice(owner, stage, QuestNoticeKind.State, "Stage opened");
                    StageExec(owner, stage, stage.Definition.BeginExec, QuestPhase.Begin); changed = true;
                }
                if (stage.State != QuestStatus.Active) continue;
                UpdateStageFacts(owner, stage, stage.Definition.Objectives, stage.Counts);
                var route = stage.Definition.Routes.FirstOrDefault(r => r.Id == stage.SelectedRouteId);
                if (route != null) UpdateStageFacts(owner, stage, route.Objectives, stage.RouteCounts[route.Id], route.Id);
                bool routeDone = stage.Definition.Routes.Count == 0 || route != null && ObjectivesDone(route.Objectives, stage.RouteCounts[route.Id], route.Completion, true);
                if (!routeDone || !ObjectivesDone(stage.Definition.Objectives, stage.Counts, stage.Definition.Completion, false)) continue;
                stage.State = QuestStatus.Completed; StageNotice(owner, stage, QuestNoticeKind.State, "Stage objectives satisfied");
                StageExec(owner, stage, stage.Definition.FinishExec, QuestPhase.Finish); changed = true;
            }
            return changed;
        }
        private void UpdateStageFacts(QuestInstance owner, QuestStageInstance stage, List<QuestContent> goals, Dictionary<string, int> counts, string route = "")
        {
            foreach (var goal in goals)
            {
                if (goal.Kind == ContentKind.EventCount) continue;
                bool satisfied = goal.Kind == ContentKind.QuestState ? Find(goal.Subject)?.State == goal.State
                    : goal.Kind == ContentKind.StageState ? owner.Stage(goal.Subject)?.State == goal.State
                    : world.ReadFact(goal.Subject, goal.Key) is bool actual && actual == goal.Expected;
                var value = satisfied ? 1 : 0;
                if (counts[goal.Id] == value) continue;
                counts[goal.Id] = value;
                StageNotice(owner, stage, QuestNoticeKind.Progress, goal.Id + ": " + value + "/1", route);
            }
        }
        private static bool ObjectivesDone(List<QuestContent> goals, Dictionary<string, int> counts, RuleCombination join, bool emptyAllowed)
            => goals.Count == 0 ? emptyAllowed : join == RuleCombination.All ? goals.All(g => counts[g.Id] >= g.Required) : goals.Any(g => counts[g.Id] >= g.Required);
        private bool ReportToStages(QuestInstance owner, QuestWorldEvent occurrence)
        {
            bool matched = false;
            foreach (var stage in owner.Stages.Where(s => s.State == QuestStatus.Active))
            {
                matched |= CountStageEvent(owner, stage, stage.Definition.Objectives, stage.Counts, occurrence);
                var route = stage.Definition.Routes.FirstOrDefault(r => r.Id == stage.SelectedRouteId);
                if (route != null) matched |= CountStageEvent(owner, stage, route.Objectives, stage.RouteCounts[route.Id], occurrence, route.Id);
            }
            return matched;
        }
        private bool CountStageEvent(QuestInstance owner, QuestStageInstance stage, List<QuestContent> goals, Dictionary<string, int> counts, QuestWorldEvent occurrence, string route = "")
        {
            bool matched = false;
            foreach (var goal in goals)
            {
                if (goal.Kind != ContentKind.EventCount || goal.Subject != occurrence.Subject || goal.Key != occurrence.Key) continue;
                matched = true;
                if (counts[goal.Id] >= goal.Required) continue;
                ++counts[goal.Id]; StageNotice(owner, stage, QuestNoticeKind.Progress, goal.Id + ": " + counts[goal.Id] + "/" + goal.Required, route);
            }
            return matched;
        }
        public bool ChooseRoute(string questId, string stageId, string routeId, string source, out string rejection)
        {
            rejection = "";
            var owner = Find(questId); var stage = owner?.Stage(stageId);
            if (IsClosed || owner?.State != QuestStatus.Active || stage?.State != QuestStatus.Active)
            { rejection = "Quest/stage is not active."; return false; }
            if (string.IsNullOrWhiteSpace(source)) { rejection = "Choice source is required."; return false; }
            var route = stage.Definition.Routes.FirstOrDefault(r => r.Id == routeId);
            if (route == null) { rejection = "Unknown route."; return false; }
            if (stage.SelectedRouteId == routeId) return true; // Idempotent selection never repeats effects.
            if (stage.ClosedRoutes.Contains(routeId)) { rejection = "Previously closed route cannot be reopened in this version."; return false; }
            if (!Evaluate(route.AvailableWhen, owner)) { rejection = "Route availability conditions are not satisfied."; return false; }
            var from = stage.SelectedRouteId;
            if (!string.IsNullOrEmpty(from) && !stage.Definition.Switches.Any(s => s.From == from && s.To == routeId && Evaluate(s.When, owner)))
            { rejection = "No permitted route switch under current conditions."; return false; }
            if (!string.IsNullOrEmpty(from)) stage.ClosedRoutes.Add(from);
            stage.SelectedRouteId = routeId; stage.RouteRevision = checked(stage.RouteRevision + 1);
            stage.Choices.Add(new RouteChoice(stage.RouteRevision, from, routeId, source));
            StageNotice(owner, stage, QuestNoticeKind.RouteChoice, from + " -> " + routeId + " / " + source, routeId);
            StageExec(owner, stage, route.SelectedExec, QuestPhase.Begin, routeId);
            Refresh(); return true;
        }
        // Action results correlated to a selected route cannot leak from an earlier selection into the new one.
        // Actual global world changes remain facts; this is not a world rollback mechanism.
        public bool ReportRouteEvent(string questId, string stageId, string routeId, long revision, QuestWorldEvent occurrence)
        {
            var owner = Find(questId); var stage = owner?.Stage(stageId);
            if (IsClosed || owner?.State != QuestStatus.Active || stage?.State != QuestStatus.Active || stage.SelectedRouteId != routeId || stage.RouteRevision != revision) return false;
            if (occurrence.Id == Guid.Empty || string.IsNullOrWhiteSpace(occurrence.Subject) || string.IsNullOrWhiteSpace(occurrence.Key)) throw new ArgumentException("Invalid route event.");
            if (!consumed.Add(occurrence.Id)) return false;
            var route = stage.Definition.Routes.Single(r => r.Id == routeId);
            bool matched = CountStageEvent(owner, stage, stage.Definition.Objectives, stage.Counts, occurrence)
                | CountStageEvent(owner, stage, route.Objectives, stage.RouteCounts[routeId], occurrence, routeId);
            Refresh(); return matched;
        }
        private void StageExec(QuestInstance owner, QuestStageInstance stage, List<QuestExec> requests, QuestPhase phase, string route = "")
        {
            foreach (var exec in requests) notices.Enqueue(new QuestNotice(Id, owner.DefinitionId, QuestNoticeKind.Exec, stage.State,
                "Stage lifecycle", phase, exec, stage.Id, route, stage.RouteRevision));
        }
        private void StageNotice(QuestInstance owner, QuestStageInstance stage, QuestNoticeKind kind, string detail, string route = "")
            => notices.Enqueue(new QuestNotice(Id, owner.DefinitionId, kind, stage.State, detail, stage: stage.Id, route: route, routeRevision: stage.RouteRevision));
        private bool MayExecuteStage(QuestInstance owner, QuestNotice notice)
        {
            var stage = owner.Stage(notice.StageId);
            if (stage == null || (owner.State != QuestStatus.Active && owner.State != QuestStatus.Completed)) return false;
            if (!string.IsNullOrEmpty(notice.RouteId) && (stage.SelectedRouteId != notice.RouteId || stage.RouteRevision != notice.RouteRevision)) return false;
            return notice.Phase == QuestPhase.Begin ? stage.State == QuestStatus.Active : notice.Phase == QuestPhase.Finish && stage.State == QuestStatus.Completed;
        }
        private void CloseStages(QuestInstance owner, string reason)
        {
            foreach (var stage in owner.Stages.Where(s => s.State == QuestStatus.Active || s.State == QuestStatus.Waiting))
            { stage.State = QuestStatus.Cancelled; StageNotice(owner, stage, QuestNoticeKind.State, reason); }
        }
        public IReadOnlyList<string> ExplainStage(string questId, string stageId)
        {
            var owner = Find(questId); var stage = owner?.Stage(stageId) ?? throw new ArgumentException("Unknown stage.");
            var result = new List<string> { stage.Title + ": " + stage.State };
            if (IsClosed) { result.Add("Run closed."); return result; }
            foreach (var prerequisite in stage.Definition.After) result.Add("After " + owner.Stage(prerequisite)?.Title + ": " + owner.Stage(prerequisite)?.State);
            if (stage.State == QuestStatus.Waiting) result.Add("Open conditions: " + Evaluate(stage.Definition.OpenWhen, owner));
            foreach (var goal in stage.Definition.Objectives) result.Add(goal.Label + ": " + stage.Progress(goal.Id) + "/" + goal.Required);
            foreach (var route in stage.Definition.Routes)
            {
                result.Add(route.Title + ": " + (stage.SelectedRouteId == route.Id ? "selected" : stage.ClosedRoutes.Contains(route.Id) ? "closed" : Evaluate(route.AvailableWhen, owner) ? "available" : "unavailable"));
                if (stage.SelectedRouteId == route.Id) foreach (var goal in route.Objectives) result.Add("  " + goal.Label + ": " + stage.RouteProgress(route.Id, goal.Id) + "/" + goal.Required);
            }
            return result;
        }
    }
}
