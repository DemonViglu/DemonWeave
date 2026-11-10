using System;
using System.Collections.Generic;
using System.Linq;
using DemonWeave.Quests;
using UnityEngine;

namespace DemonWeave.Unity
{
    // Scene persistence and run ownership. Authors bind targets in Quest Editor, not on this component.
    [AddComponentMenu("DemonWeave/Quest Runner")]
    public sealed class QuestSceneHost : MonoBehaviour, IQuestAuthoringHost, IQuestWorld, IQuestStateWorld
    {
        [SerializeField, HideInInspector] private QuestBookAsset book;
        [SerializeField, HideInInspector] private List<EntityBinding> bindings = new List<EntityBinding>();
        private readonly List<string> activity = new List<string>();
        [SerializeField] private QuestWorldState worldState;
        public QuestWorldState WorldState
        {
            get {
                if (worldState && worldState.isActiveAndEnabled) return worldState;
                if (QuestWorldState.Current) return QuestWorldState.Current;
                if (Application.isPlaying) return null;
                var candidates = FindObjectsByType<QuestWorldState>(FindObjectsSortMode.None).Where(s => s.isActiveAndEnabled).ToArray();
                return candidates.Length == 1 ? candidates[0] : null;
            }
        }
        public bool? ReadWorldFact(string id) => WorldState ? WorldState.Read(id) : null;
        public bool IsQuestCompleted(string collectionId, string questId) => WorldState && WorldState.HasCompleted(collectionId, questId);
        private CapabilityCatalog capabilities;
        private string activeCollectionId;
        private readonly Dictionary<(string quest, string node), QuestExecution> executions = new Dictionary<(string, string), QuestExecution>();
        private readonly Queue<(Guid run, string quest, string node, string result)> results = new Queue<(Guid, string, string, string)>();
        public string ExecutionStatus(string quest, string node) => executions.TryGetValue((quest, node), out var execution) ? execution.Status : "";
        public QuestBookAsset BookAsset { get => book; set => book = value; }
        public List<EntityBinding> BindingEntries => bindings;
        public QuestRun ActiveRun { get; private set; }
        public IReadOnlyList<string> Activity => activity;
        public IReadOnlyList<WorldSignal> Signals => QuestMethods.Conditions(bindings);
        public CapabilityCatalog CreateCapabilities() => CapabilityCatalog.FromBindings(bindings);
        public bool? ReadFact(string role, string key) => QuestMethods.Read(bindings, role, key);
        private bool started;
        private void Start() { started = true; RestartRun(); }
        private void OnEnable() { if (started) RestartRun(); }
        public void RestartRun()
        {
            StopRun();
            if (!isActiveAndEnabled) return;
            try
            {
                if (!book) throw new InvalidOperationException("No Quest Collection is assigned.");
                var definitions = book.RuntimeDefinitions();
                if (definitions.Count == 0) { Record("Collection contains drafts only."); return; }
                foreach (var definition in definitions)
                    foreach (var node in definition.Stages.Where(n => n.UsesEntityExecution())) {
                        var entity = bindings.FirstOrDefault(b => b.Role == node.EntityRole)?.Entity;
                        if (!entity || !entity.Results.SequenceEqual(node.Results)) throw new ArgumentException(node.Title + ": Entity result contract changed or target is missing.");
                        var error = entity.CheckConfiguration();
                        if (!string.IsNullOrEmpty(error)) throw new ArgumentException(node.Title + ": " + error);
                    }
                var objects = new QuestBindings(bindings); capabilities = CreateCapabilities();
                var conditions = Signals;
                var errors = QuestValidation.Validate(definitions, role => objects.Find(role), capabilities.Validate,
                    (role, key, isEvent) => conditions.Any(c => c.Role == role && c.Key == key && c.IsEvent == isEvent) ? "" : "Condition is not declared on the selected object: " + key);
                if (errors.Count > 0) throw new ArgumentException(string.Join("\n", errors));
                var stateError = ValidateWorldBindings(definitions);
                if (!string.IsNullOrEmpty(stateError)) throw new ArgumentException(stateError);
                activeCollectionId = book.Id;
                ActiveRun = new QuestRun(definitions, this); Tick();
            }
            catch (Exception e) { Fail(e); }
        }
        private void Update() { if (ActiveRun != null) { try { Tick(); } catch (Exception e) { Fail(e); } } }
        private void Tick()
        {
            ActiveRun.Refresh();
            var run = ActiveRun;
            var ticked = new HashSet<QuestExecution>();
            int transitions = 128;
            do {
                foreach (var pair in executions.ToArray())
                    if (run.Find(pair.Key.quest)?.Stage(pair.Key.node)?.State != QuestStatus.Active) {
                        executions.Remove(pair.Key); Release(pair.Value);
                    }
                foreach (var owner in run.Instances.Where(q => q.State == QuestStatus.Active))
                    foreach (var node in owner.Stages.Where(n => n.UsesEntity && n.State == QuestStatus.Active)) {
                        var key = (owner.DefinitionId, node.Id);
                        var entity = bindings.FirstOrDefault(b => b.Role == node.EntityRole)?.Entity;
                        if (!entity || !entity.isActiveAndEnabled) throw new InvalidOperationException(node.Title + ": target component is missing or disabled.");
                        if (!executions.TryGetValue(key, out var execution)) {
                            if (node.IsPaused) continue;
                            execution = entity.CreateExecution() ?? throw new InvalidOperationException("CreateExecution returned null.");
                            if (executions.Values.Contains(execution)) throw new InvalidOperationException("Return a fresh execution for each node.");
                            executions.Add(key, execution);
                            execution.Start(result => { if (ActiveRun == run && !run.IsClosed) results.Enqueue((run.Id, key.Item1, key.Item2, result)); });
                        }
                        if (ActiveRun != run || run.IsClosed) return;
                        execution.SetPaused(node.IsPaused);
                        if (ActiveRun != run || run.IsClosed) return;
                        if (ticked.Add(execution)) execution.Tick();
                        if (ActiveRun != run || run.IsClosed) return;
                    }
                if (results.Count == 0) break;
                bool applied = false;
                int count = results.Count;
                for (int i = 0; i < count; i++) {
                    var result = results.Dequeue();
                    if (run.Find(result.quest)?.Stage(result.node)?.State == QuestStatus.Active && !run.CanProgress(result.quest, result.node)) {
                        results.Enqueue(result); continue;
                    }
                    if (--transitions == 0) throw new InvalidOperationException("Entity transition budget exceeded.");
                    var accepted = run.CompleteEntity(result.run, result.quest, result.node, result.result);
                    if (accepted && executions.TryGetValue((result.quest, result.node), out var finished)) {
                        executions.Remove((result.quest, result.node));
                        Release(finished, QuestStopReason.Completed);
                    }
                    if (ActiveRun != run || run.IsClosed) return;
                    applied |= accepted;
                }
                if (!applied) break;
            } while (ActiveRun == run && !run.IsClosed);
            foreach (var pair in executions.ToArray())
                if (run.Find(pair.Key.quest)?.Stage(pair.Key.node)?.State != QuestStatus.Active) { executions.Remove(pair.Key); Release(pair.Value); }
            PublishCompletions(run);
            int budget = 128;
            while (ActiveRun != null && ActiveRun.TryRead(out var notice))
            {
                if (--budget == 0) throw new InvalidOperationException("Quest action budget exceeded.");
                Record(notice.QuestId + "/" + notice.StageId + " " + notice.Kind + ": " + notice.Detail);
                if (notice.Kind != QuestNoticeKind.Exec || !ActiveRun.MayExecute(notice)) continue;
                var result = capabilities.Execute(notice.Execution.CapabilityId, notice.Execution.ArgumentsJson);
                Record(notice.QuestId + "/" + notice.StageId + " " + notice.Execution.CapabilityId + ": " + (result.Success ? "OK / " : "Failed / ") + result.Message);
                // Only the next fact evaluation can complete a Quest, never the action return value itself.
            }
        }
        private void Release(QuestExecution execution, QuestStopReason reason = QuestStopReason.Cancelled)
        { try { execution.Stop(reason); } catch (Exception error) { Record("Cleanup: " + error.Message); Debug.LogException(error, this); } }
        private void PublishCompletions(QuestRun run)
        {
            if (run == null || !WorldState || string.IsNullOrEmpty(activeCollectionId)) return;
            foreach (var quest in run.Instances.Where(q => q.State == QuestStatus.Completed)) WorldState.RecordCompletion(activeCollectionId, quest.DefinitionId);
        }
        public bool Interrupt(Guid runId, string questId, string nodeId, string reason)
        {
            var run = ActiveRun;
            if (run == null || !run.Interrupt(runId, questId, nodeId, reason)) return false;
            foreach (var pair in executions.ToArray())
                if (run.Find(pair.Key.quest)?.Stage(pair.Key.node)?.State != QuestStatus.Active) { executions.Remove(pair.Key); Release(pair.Value); }
            Record("Interrupted " + questId + "/" + nodeId + ": " + reason); return true;
        }
        public string ValidateWorldBindings(IReadOnlyList<QuestDefinition> definitions)
        {
            IEnumerable<QuestCondition> Walk(QuestCondition rule) {
                if (rule == null) yield break;
                yield return rule;
                if (rule.Children != null) foreach (var child in rule.Children) foreach (var value in Walk(child)) yield return value;
            }
            foreach (var quest in definitions) {
                var roots = new List<QuestCondition> { quest.Accept, quest.RunWhen, quest.InterruptWhen };
                foreach (var node in quest.Stages) roots.AddRange(new[] { node.OpenWhen, node.RunWhen, node.InterruptWhen });
                foreach (var rule in roots.SelectMany(Walk)) {
                    if (rule.Kind != ConditionKind.WorldFact && rule.Kind != ConditionKind.ExternalQuestCompleted) continue;
                    var state = WorldState;
                    if (!state) return "Select a World State service on the runner for " + quest.Title + ".";
                    if (state.Facts.Any(f => f == null) || state.Facts.Where(f => f != null).GroupBy(f => f.Id).Any(g => g.Count() > 1))
                        return "World State contains a missing fact or conflicting fact IDs.";
                    if (rule.Kind == ConditionKind.WorldFact && state.Read(rule.Subject) == null && !state.Facts.Any(f => f != null && f.Id == rule.Subject))
                        return quest.Title + ": world fact is not registered in World State: " + rule.Subject;
                }
            }
            return "";
        }
        private void Record(string message) { if (activity.Count >= 100) activity.RemoveAt(0); activity.Add(message); }
        private void Fail(Exception e) { Record(e.Message); StopRun(QuestStopReason.Faulted); Debug.LogError(e.Message, this); }
        public void StopRun() => StopRun(QuestStopReason.Cancelled);
        private void StopRun(QuestStopReason reason)
        {
            var run = ActiveRun; PublishCompletions(run); ActiveRun = null; run?.Dispose(); results.Clear();
            while (run != null && run.TryRead(out var stoppedNotice))
                Record(stoppedNotice.QuestId + "/" + stoppedNotice.StageId + " " + stoppedNotice.Kind + ": " + stoppedNotice.Detail);
            var pending = executions.Values.ToArray(); executions.Clear();
            foreach (var execution in pending) Release(execution, reason);
        }
        private void OnDisable() => StopRun();
    }
}

