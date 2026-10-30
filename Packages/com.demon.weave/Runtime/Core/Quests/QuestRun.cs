using System;
using System.Collections.Generic;
using System.Linq;

namespace DemonWeave.Quests
{
    public enum QuestNoticeKind { State, Progress, Exec, Diagnostic, RouteChoice }
    public sealed class QuestNotice
    {
        public Guid RunId { get; }
        public string QuestId { get; }
        public QuestNoticeKind Kind { get; }
        public QuestStatus State { get; }
        public string Detail { get; }
        public QuestPhase Phase { get; }
        public QuestExec Execution { get; }
        public string StageId { get; }
        public string RouteId { get; }
        public long RouteRevision { get; }
        internal QuestNotice(Guid run, string quest, QuestNoticeKind kind, QuestStatus state, string detail,
            QuestPhase phase = default, QuestExec exec = null, string stage = "", string route = "", long routeRevision = 0)
        { RunId = run; QuestId = quest; Kind = kind; State = state; Detail = detail; Phase = phase; Execution = exec?.Copy();
            StageId = stage; RouteId = route; RouteRevision = routeRevision; }
    }

    public sealed class QuestInstance
    {
        internal readonly QuestDefinition Definition;
        internal readonly Dictionary<string, int> Counts = new Dictionary<string, int>(StringComparer.Ordinal);
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
            foreach (var content in definition.Finish.Concat(definition.Fail)) Counts.Add(content.Id, 0);
            StageMap = definition.Stages.ToDictionary(s => s.Id, s => new QuestStageInstance(s), StringComparer.Ordinal);
            Stages = Array.AsReadOnly(definition.Stages.Select(s => StageMap[s.Id]).ToArray());
        }
        public int Progress(string contentId) => Counts.TryGetValue(contentId, out var count) ? count : 0;
        public QuestDefinition DefinitionSnapshot() => Definition.Copy();
        public QuestStageInstance Stage(string id) => id != null && StageMap.TryGetValue(id, out var stage) ? stage : null;
    }

    /// <summary>
    /// A local set of independent quest instances. Parent ownership is not ordering.
    /// Re-evaluation/terminal conflict rules here are our local policy, not recovered Genshin algorithms.
    /// Host calls Refresh after world changes, and handles queued Exec outside the evaluation stack.
    /// </summary>
    public sealed partial class QuestRun : IDisposable
    {
        private readonly Dictionary<string, QuestInstance> instances;
        private readonly QuestInstance[] ordered;
        private readonly HashSet<Guid> consumed = new HashSet<Guid>();
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
                    RefreshContents(instance);
                    bool failed = Satisfied(instance, instance.Definition.Fail, instance.Definition.FailCombination);
                    if (!failed)
                    { changed |= RefreshStages(instance); RefreshContents(instance); failed = Satisfied(instance, instance.Definition.Fail, instance.Definition.FailCombination); }
                    bool finished = (instance.Definition.Finish.Count == 0 || Satisfied(instance, instance.Definition.Finish, instance.Definition.FinishCombination))
                        && instance.Stages.Where(s => s.Definition.Required).All(s => s.State == QuestStatus.Completed);
                    if (!failed && !finished) continue;
                    // Explicit local failure precedence; trace the conflict instead of hiding the policy.
                    if (failed && finished) Emit(instance, QuestNoticeKind.Diagnostic, "Finish and fail both satisfied; configured local policy gives fail precedence.");
                    Commit(instance, failed ? QuestStatus.Failed : QuestStatus.Completed, failed ? "Fail content satisfied" : "Finish content satisfied");
                    changed = true;
                }
            } while (changed);
        }

        public bool Report(QuestWorldEvent occurrence)
        {
            if (IsClosed) return false;
            if (occurrence.Id == Guid.Empty || string.IsNullOrWhiteSpace(occurrence.Subject) || string.IsNullOrWhiteSpace(occurrence.Key))
                throw new ArgumentException("event: identity, subject and key are required");
            if (!consumed.Add(occurrence.Id)) return false;
            bool matched = false;
            // Only tasks active BEFORE this event consume it. Successors never inherit the triggering event.
            foreach (var instance in ordered.Where(x => x.State == QuestStatus.Active))
            {
                matched |= ReportToStages(instance, occurrence);
                foreach (var content in instance.Definition.Finish.Concat(instance.Definition.Fail))
                {
                    if (content.Kind != ContentKind.EventCount || content.Subject != occurrence.Subject || content.Key != occurrence.Key) continue;
                    matched = true;
                    if (instance.Counts[content.Id] >= content.Required) continue;
                    ++instance.Counts[content.Id];
                    Emit(instance, QuestNoticeKind.Progress, content.Id + ": " + instance.Counts[content.Id] + "/" + content.Required);
                }
            }
            Refresh(); return matched;
        }

        private bool Evaluate(QuestCondition condition, QuestInstance owner = null)
        {
            switch (condition.Kind)
            {
                case ConditionKind.All: return condition.Children.All(c => Evaluate(c, owner));
                case ConditionKind.Any: return condition.Children.Any(c => Evaluate(c, owner));
                case ConditionKind.QuestState: return Find(condition.Subject)?.State == condition.State;
                case ConditionKind.StageState: return owner?.Stage(condition.Subject)?.State == condition.State;
                case ConditionKind.Fact: return world.ReadFact(condition.Subject, condition.Key) is bool actual && actual == condition.Expected;
                default: throw new InvalidOperationException("Condition was not validated.");
            }
        }
        private void RefreshContents(QuestInstance instance)
        {
            foreach (var content in instance.Definition.Finish.Concat(instance.Definition.Fail))
            {
                if (content.Kind == ContentKind.EventCount) continue;
                bool satisfied = content.Kind == ContentKind.QuestState ? Find(content.Subject)?.State == content.State
                    : content.Kind == ContentKind.StageState ? instance.Stage(content.Subject)?.State == content.State
                    : world.ReadFact(content.Subject, content.Key) is bool actual && actual == content.Expected;
                int value = satisfied ? 1 : 0;
                if (instance.Counts[content.Id] == value) continue;
                instance.Counts[content.Id] = value;
                Emit(instance, QuestNoticeKind.Progress, content.Id + ": " + value + "/1");
            }
        }
        private static bool Satisfied(QuestInstance instance, List<QuestContent> contents, RuleCombination combination)
        {
            if (contents.Count == 0) return false;
            return combination == RuleCombination.All ? contents.All(c => instance.Counts[c.Id] >= c.Required)
                : contents.Any(c => instance.Counts[c.Id] >= c.Required);
        }
        private void Commit(QuestInstance instance, QuestStatus state, string reason)
        {
            instance.State = state; Emit(instance, QuestNoticeKind.State, reason);
            if (state != QuestStatus.Active) CloseStages(instance, reason);
            var phase = state == QuestStatus.Active ? QuestPhase.Begin : state == QuestStatus.Completed ? QuestPhase.Finish : QuestPhase.Fail;
            var exec = phase == QuestPhase.Begin ? instance.Definition.BeginExec : phase == QuestPhase.Finish ? instance.Definition.FinishExec : instance.Definition.FailExec;
            if (state != QuestStatus.Cancelled)
                foreach (var request in exec) notices.Enqueue(new QuestNotice(Id, instance.DefinitionId, QuestNoticeKind.Exec, state, reason, phase, request));
            if (state != QuestStatus.Active && instance.Definition.CancelChildrenOnEnd)
                foreach (var child in ordered.Where(x => x.ParentId == instance.DefinitionId)) CancelTree(child, "Parent scope ended: " + instance.DefinitionId);
        }
        private void CancelTree(QuestInstance instance, string reason)
        {
            if (instance.State == QuestStatus.Waiting || instance.State == QuestStatus.Active)
            { instance.State = QuestStatus.Cancelled; CloseStages(instance, reason); Emit(instance, QuestNoticeKind.State, reason); }
            foreach (var child in ordered.Where(x => x.ParentId == instance.DefinitionId)) CancelTree(child, reason);
        }
        public bool MayExecute(QuestNotice notice)
        {
            if (IsClosed || notice == null || notice.RunId != Id || notice.Kind != QuestNoticeKind.Exec) return false;
            var instance = Find(notice.QuestId);
            if (instance == null || instance.State == QuestStatus.Cancelled) return false;
            if (!string.IsNullOrEmpty(notice.StageId)) return MayExecuteStage(instance, notice);
            // Begin effects whose task has already ended are deliberately skipped, not revived later.
            return notice.Phase == QuestPhase.Begin ? instance.State == QuestStatus.Active
                : notice.Phase == QuestPhase.Finish ? instance.State == QuestStatus.Completed : instance.State == QuestStatus.Failed;
        }
        public IReadOnlyList<string> Explain(string questId)
        {
            var instance = Find(questId) ?? throw new ArgumentException("Unknown quest: " + questId);
            var lines = new List<string>();
            if (IsClosed) { lines.Add("Run closed; no further input is consumed."); return lines; }
            if (instance.State == QuestStatus.Waiting)
            {
                ExplainCondition(instance.Definition.Accept, lines, "accept");
            }
            else if (instance.State == QuestStatus.Active)
                foreach (var content in instance.Definition.Finish)
                    lines.Add((string.IsNullOrEmpty(content.Label) ? content.Id : content.Label) + ": " + instance.Progress(content.Id) + "/" + content.Required);
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
            else if (condition.Kind == ConditionKind.QuestState)
                lines.Add(path + ": " + Find(condition.Subject)?.Title + " requires " + condition.State + ", actual " + Find(condition.Subject)?.State);
            else lines.Add(path + ": " + condition.Subject + "." + condition.Key + " requires " + condition.Expected + ", actual " + (world.ReadFact(condition.Subject, condition.Key)?.ToString() ?? "missing"));
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
            consumed.Clear(); world = null;
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
