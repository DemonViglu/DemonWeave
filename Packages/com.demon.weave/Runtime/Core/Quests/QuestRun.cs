using System;
using System.Collections.Generic;
using System.Linq;

namespace DemonWeave.Quests
{
    public enum QuestNoticeKind { State, Diagnostic }
    public sealed class QuestNotice
    {
        public Guid RunId { get; }
        public string QuestId { get; }
        public QuestNoticeKind Kind { get; }
        public QuestStatus State { get; }
        public string Detail { get; }
        public string StageId { get; }
        internal QuestNotice(Guid run, string quest, QuestNoticeKind kind, QuestStatus state, string detail, string stage = "")
        { RunId = run; QuestId = quest; Kind = kind; State = state; Detail = detail; StageId = stage; }
    }

    public sealed class QuestInstance
    {
        internal readonly QuestDefinition Definition;
        internal readonly Dictionary<string, QuestStageInstance> StageMap;
        public IReadOnlyList<QuestStageInstance> Stages { get; }
        public Guid Id { get; } = Guid.NewGuid();
        public string DefinitionId => Definition.Id;
        public string Title => Definition.Title;
        public string ParentId => Definition.ParentId;
        public QuestStatus State { get; internal set; } = QuestStatus.Waiting;
        internal QuestInstance(QuestDefinition definition)
        {
            Definition = definition;
            StageMap = definition.Stages.ToDictionary(s => s.Id, s => new QuestStageInstance(s), StringComparer.Ordinal);
            Stages = Array.AsReadOnly(definition.Stages.Select(s => StageMap[s.Id]).ToArray());
        }
        public bool IsPaused { get; internal set; }
        public string EndReason { get; internal set; } = "";
        public QuestDefinition DefinitionSnapshot() => Definition.Copy();
        public QuestStageInstance Stage(string id) => id != null && StageMap.TryGetValue(id, out var stage) ? stage : null;
    }

    /// <summary>
    /// A local set of independent quest instances. Parent ownership is not ordering.
    /// Re-evaluation/terminal conflict rules here are our local policy, not recovered Genshin algorithms.
    /// Host calls Refresh after world changes and runs node executions outside the evaluation stack.
    /// </summary>
    public sealed partial class QuestRun : IDisposable
    {
        private readonly Dictionary<string, QuestInstance> instances;
        private readonly QuestInstance[] ordered;
        private readonly Queue<QuestNotice> notices = new Queue<QuestNotice>();
        private IQuestWorld world;
        public Guid Id { get; } = Guid.NewGuid();
        public bool IsClosed { get; private set; }
        public IReadOnlyList<QuestInstance> Instances { get; }
        internal event Action<QuestRun> Closed;

        public QuestRun(IReadOnlyList<QuestDefinition> definitions, IQuestWorld world)
        {
            var errors = QuestValidation.Validate(definitions);
            if (errors.Count > 0) throw new ArgumentException(string.Join("\n", errors));
            this.world = world ?? throw new ArgumentNullException(nameof(world));
            ordered = definitions.Select(d => new QuestInstance(d.Copy())).OrderBy(i => i.DefinitionId, StringComparer.Ordinal).ToArray();
            instances = ordered.ToDictionary(i => i.DefinitionId, StringComparer.Ordinal);
            Instances = Array.AsReadOnly(ordered);
        }
        public QuestInstance Find(string questId) => questId != null && instances.TryGetValue(questId, out var instance) ? instance : null;

        public void Refresh()
        {
            if (IsClosed) return;
            // Monotone lifecycle: Waiting -> Active -> terminal. No rewind or hidden auto-restart.
            // At most two state changes per quest; order only stabilizes notices, never defines dependencies.
            bool changed;
            do
            {
                changed = false;
                foreach (var instance in ordered)
                {
                    if (instance.State == QuestStatus.Waiting)
                    {
                        if (Evaluate(instance.Definition.Accept, instance)) { Commit(instance, QuestStatus.Active, "Accept conditions satisfied"); changed = true; }
                        continue;
                    }
                    if (instance.State != QuestStatus.Active) continue;
                    if (instance.Definition.InterruptWhen != null && Evaluate(instance.Definition.InterruptWhen, instance))
                    { CancelTree(instance, "Interruption condition satisfied"); changed = true; continue; }
                    var paused = !Evaluate(instance.Definition.RunWhen, instance);
                    if (instance.IsPaused != paused) { instance.IsPaused = paused; Emit(instance, QuestNoticeKind.Diagnostic, paused ? "Paused by running condition" : "Resumed"); }
                    if (paused) { changed |= RefreshStages(instance); continue; }
                    changed |= RefreshStages(instance);
                    var terminals = instance.Stages.Where(s => s.Definition.Required).ToArray();
                    bool Reached(QuestStageInstance s) => s.State == QuestStatus.Completed &&
                        (string.IsNullOrEmpty(s.Definition.FinishResult) || s.Result == s.Definition.FinishResult);
                    bool finished = terminals.Length > 0 && (instance.Definition.StageCompletion == RuleCombination.Any
                        ? terminals.Any(Reached) : terminals.All(Reached));
                    if (!finished) continue;
                    Commit(instance, QuestStatus.Completed, "Finish connection reached");
                    changed = true;
                }
            } while (changed);
        }

        private bool Evaluate(QuestCondition condition, QuestInstance owner = null)
        {
            switch (condition.Kind)
            {
                case ConditionKind.All: return condition.Children.All(c => Evaluate(c, owner));
                case ConditionKind.Any: return condition.Children.Any(c => Evaluate(c, owner));
                case ConditionKind.QuestState: return Find(condition.Subject)?.State == condition.State;
                case ConditionKind.StageState: return owner?.Stage(condition.Subject)?.State == condition.State;
                case ConditionKind.StageResult: return owner?.Stage(condition.Subject)?.State == QuestStatus.Completed && owner.Stage(condition.Subject).Result == condition.Key;
                case ConditionKind.WorldFact: return world?.ReadWorldFact(condition.Subject) is bool fact && fact == condition.Expected;
                case ConditionKind.ExternalQuestCompleted: return world?.IsQuestCompleted(condition.Subject, condition.Key) == true;
                default: throw new InvalidOperationException("Condition was not validated.");
            }
        }
        private void Commit(QuestInstance instance, QuestStatus state, string reason)
        {
            instance.State = state; Emit(instance, QuestNoticeKind.State, reason);
            if (state != QuestStatus.Active) CloseStages(instance, reason);
            if (state != QuestStatus.Active && instance.Definition.CancelChildrenOnEnd)
                foreach (var child in ordered.Where(x => x.ParentId == instance.DefinitionId)) CancelTree(child, "Parent scope ended: " + instance.DefinitionId);
        }
        private void CancelTree(QuestInstance instance, string reason)
        {
            if (instance.State == QuestStatus.Waiting || instance.State == QuestStatus.Active)
            { instance.State = QuestStatus.Cancelled; instance.EndReason = reason; instance.IsPaused = false; CloseStages(instance, reason); Emit(instance, QuestNoticeKind.State, reason); }
            foreach (var child in ordered.Where(x => x.ParentId == instance.DefinitionId)) CancelTree(child, reason);
        }
        public bool Interrupt(Guid runId, string questId, string nodeId, string reason)
        {
            if (runId != Id || IsClosed || string.IsNullOrWhiteSpace(reason)) return false;
            var owner = Find(questId);
            if (owner == null || owner.State != QuestStatus.Active && owner.State != QuestStatus.Waiting) return false;
            if (string.IsNullOrEmpty(nodeId)) { CancelTree(owner, reason); return true; }
            var node = owner.Stage(nodeId);
            if (node == null || node.State != QuestStatus.Active && node.State != QuestStatus.Waiting) return false;
            node.State = QuestStatus.Cancelled; node.IsPaused = false; node.EndReason = reason;
            StageNotice(owner, node, QuestNoticeKind.State, reason); return true;
        }
        public bool CanProgress(string questId, string nodeId)
        {
            var owner = Find(questId); var node = owner?.Stage(nodeId);
            return !IsClosed && owner?.State == QuestStatus.Active && node?.State == QuestStatus.Active &&
                Evaluate(owner.Definition.RunWhen, owner) && Evaluate(node.Definition.RunWhen, owner) &&
                (owner.Definition.InterruptWhen == null || !Evaluate(owner.Definition.InterruptWhen, owner)) &&
                (node.Definition.InterruptWhen == null || !Evaluate(node.Definition.InterruptWhen, owner));
        }

        public IReadOnlyList<string> Explain(string questId)
        {
            var instance = Find(questId) ?? throw new ArgumentException("Unknown quest: " + questId);
            var lines = new List<string>();
            if (instance.IsPaused) { lines.Add("Paused by running condition"); ExplainCondition(instance.Definition.RunWhen, lines, "runWhen"); }
            if (!string.IsNullOrEmpty(instance.EndReason)) lines.Add("Stopped: " + instance.EndReason);
            if (IsClosed) { lines.Add("Run closed; no further input is consumed."); return lines; }
            if (instance.State == QuestStatus.Waiting)
            {
                ExplainCondition(instance.Definition.Accept, lines, "accept");
            }
            else if (instance.State == QuestStatus.Active)
                foreach (var node in instance.Stages.Where(n => n.State == QuestStatus.Active))
                    lines.Add(node.Title + ": " + (node.IsPaused ? "Paused" : "Waiting for node result"));
            else lines.Add(instance.State.ToString());
            return lines;
        }
        private void ExplainCondition(QuestCondition condition, List<string> lines, string path)
        {
            if (condition.Kind == ConditionKind.All || condition.Kind == ConditionKind.Any)
            {
                lines.Add(path + ": " + condition.Kind + " = " + Evaluate(condition));
                for (int i = 0; i < condition.Children.Count; i++) ExplainCondition(condition.Children[i], lines, path + "/" + i);
            }
            else if (condition.Kind == ConditionKind.WorldFact)
                lines.Add(path + ": " + condition.Subject + " needs " + condition.Expected + ", actual " + (world?.ReadWorldFact(condition.Subject)?.ToString() ?? "missing"));
            else if (condition.Kind == ConditionKind.ExternalQuestCompleted)
                lines.Add(path + ": waiting for " + condition.Subject + "/" + condition.Key + " to complete");
            else if (condition.Kind == ConditionKind.QuestState)
                lines.Add(path + ": " + Find(condition.Subject)?.Title + " requires " + condition.State + ", actual " + Find(condition.Subject)?.State);
            else lines.Add(path + ": " + condition.Kind + " / " + condition.Subject + " / " + condition.Key);
        }
        private void Emit(QuestInstance instance, QuestNoticeKind kind, string detail)
            => notices.Enqueue(new QuestNotice(Id, instance.DefinitionId, kind, instance.State, detail));
        public bool TryRead(out QuestNotice notice)
        { if (notices.Count == 0) { notice = null; return false; } notice = notices.Dequeue(); return true; }
        public void Dispose()
        {
            if (IsClosed) return;
            IsClosed = true;
            foreach (var instance in ordered)
                if (instance.State == QuestStatus.Waiting || instance.State == QuestStatus.Active)
                { instance.State = QuestStatus.Cancelled; CloseStages(instance, "Scene/application scope disposed"); Emit(instance, QuestNoticeKind.State, "Scene/application scope disposed"); }
            world = null;
            Closed?.Invoke(this); Closed = null;
        }
    }

    public sealed class QuestService : IDisposable
    {
        private readonly HashSet<QuestRun> runs = new HashSet<QuestRun>();
        public int ActiveRunCount => runs.Count;
        public bool IsDisposed { get; private set; }
        public QuestRun Open(IReadOnlyList<QuestDefinition> definitions, IQuestWorld world)
        {
            if (IsDisposed) throw new ObjectDisposedException(nameof(QuestService));
            var run = new QuestRun(definitions, world);
            run.Closed += OnClosed; runs.Add(run); return run;
        }
        private void OnClosed(QuestRun run) { runs.Remove(run); run.Closed -= OnClosed; }
        public void Dispose()
        {
            if (IsDisposed) return;
            IsDisposed = true; foreach (var run in runs.ToArray()) run.Dispose(); runs.Clear();
        }
    }
}
