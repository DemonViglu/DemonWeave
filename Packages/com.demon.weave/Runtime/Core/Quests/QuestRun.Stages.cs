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
                if (stage.State != QuestStatus.Active && stage.State != QuestStatus.Waiting) continue;
                if (stage.Definition.InterruptWhen != null && Evaluate(stage.Definition.InterruptWhen, owner))
                { Interrupt(Id, owner.DefinitionId, stage.Id, "Interruption condition satisfied"); changed = true; continue; }
                var paused = owner.IsPaused || !Evaluate(stage.Definition.RunWhen, owner);
                if (stage.IsPaused != paused) { stage.IsPaused = paused; StageNotice(owner, stage, QuestNoticeKind.Diagnostic, paused ? "Paused by running condition" : "Resumed"); }
                if (paused) continue;
                if (stage.State == QuestStatus.Waiting)
                {
                    if (!stage.Definition.After.All(id => owner.Stage(id)?.State == QuestStatus.Completed) || !Evaluate(stage.Definition.OpenWhen, owner)) continue;
                    stage.State = QuestStatus.Active; StageNotice(owner, stage, QuestNoticeKind.State, "Stage opened");
                    changed = true;
                }
                if (stage.State == QuestStatus.Active && stage.Definition.Control != QuestControl.None)
                {
                    stage.Result = "Next"; stage.State = QuestStatus.Completed;
                    StageNotice(owner, stage, QuestNoticeKind.State, "Control released"); changed = true;
                }
            }
            return changed;
        }
        public bool CompleteNode(Guid runId, string questId, string stageId, string result)
        {
            var owner = Find(questId); var stage = owner?.Stage(stageId);
            if (runId != Id || !CanProgress(questId, stageId)) return false;
            if (!stage.UsesLogic) return false;
            if (!stage.Definition.Results.Contains(result)) throw new ArgumentException("Undeclared result: " + result);
            stage.Result = result; stage.State = QuestStatus.Completed;
            StageNotice(owner, stage, QuestNoticeKind.State, "Result: " + result);
            Refresh(); return true;
        }

        private void StageNotice(QuestInstance owner, QuestStageInstance stage, QuestNoticeKind kind, string detail)
            => notices.Enqueue(new QuestNotice(Id, owner.DefinitionId, kind, stage.State, detail, stage.Id));
        private void CloseStages(QuestInstance owner, string reason)
        {
            foreach (var stage in owner.Stages.Where(s => s.State == QuestStatus.Active || s.State == QuestStatus.Waiting))
            { stage.State = QuestStatus.Cancelled; stage.IsPaused = false; stage.EndReason = reason; StageNotice(owner, stage, QuestNoticeKind.State, reason); }
        }
        public IReadOnlyList<string> ExplainStage(string questId, string stageId)

        {
            var owner = Find(questId); var stage = owner?.Stage(stageId) ?? throw new ArgumentException("Unknown stage.");
            var result = new List<string> { stage.Title + ": " + stage.State };
            if (stage.IsPaused) result.Add("Paused: running condition is false.");
            if (!string.IsNullOrEmpty(stage.EndReason)) result.Add("Stopped: " + stage.EndReason);
            if (!string.IsNullOrEmpty(stage.Result)) result.Add("Result: " + stage.Result);
            if (IsClosed) { result.Add("Run closed."); return result; }
            foreach (var prerequisite in stage.Definition.After) result.Add("After " + owner.Stage(prerequisite)?.Title + ": " + owner.Stage(prerequisite)?.State);
            void Gate(QuestCondition rule, string indent)
            {
                switch (rule.Kind)
                {
                    case ConditionKind.All:
                    case ConditionKind.Any:
                        if (rule.Children.Count == 0 && rule.Kind == ConditionKind.All) return;
                        result.Add(indent + rule.Kind + " conditions: " + Evaluate(rule, owner));
                        foreach (var child in rule.Children) Gate(child, indent + "  ");
                        break;
                    case ConditionKind.WorldFact: result.Add(indent + rule.Subject + " = " + (world.ReadWorldFact(rule.Subject)?.ToString() ?? "missing") + " (needs " + rule.Expected + ")"); break;
                    case ConditionKind.ExternalQuestCompleted: result.Add(indent + rule.Subject + "/" + rule.Key + " completed = " + world.IsQuestCompleted(rule.Subject, rule.Key)); break;
                    case ConditionKind.StageResult: result.Add(indent + owner.Stage(rule.Subject)?.Title + " needs result " + rule.Key + "; actual " + (string.IsNullOrEmpty(owner.Stage(rule.Subject)?.Result) ? "not reported" : owner.Stage(rule.Subject).Result)); break;
                    case ConditionKind.StageState: result.Add(indent + owner.Stage(rule.Subject)?.Title + " = " + owner.Stage(rule.Subject)?.State + " (needs " + rule.State + ")"); break;
                    case ConditionKind.QuestState: result.Add(indent + Find(rule.Subject)?.Title + " = " + Find(rule.Subject)?.State + " (needs " + rule.State + ")"); break;
                }
            }
            if (owner.State != QuestStatus.Active) result.Add("Quest: " + owner.State);
            if (stage.State == QuestStatus.Waiting) Gate(stage.Definition.OpenWhen, "Start when  ");
            Gate(stage.Definition.RunWhen, "Progress while  ");
            if (stage.Definition.InterruptWhen != null) Gate(stage.Definition.InterruptWhen, "Interrupt when  ");
            return result;
        }
    }
}
