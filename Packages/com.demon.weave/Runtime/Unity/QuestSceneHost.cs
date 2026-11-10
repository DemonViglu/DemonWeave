using System;
using System.Collections.Generic;
using System.Linq;
using DemonWeave.Quests;
using UnityEngine;

namespace DemonWeave.Unity
{
    // Scene persistence and run ownership. Authors bind targets in Quest Editor, not on this component.
    [AddComponentMenu("DemonWeave/Quest Runner")]
    public sealed class QuestSceneHost : MonoBehaviour, IQuestAuthoringHost, IQuestWorld
    {
        [SerializeField, HideInInspector] private QuestBookAsset book;
        [SerializeField, HideInInspector] private List<ObjectBinding> bindings = new List<ObjectBinding>();
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
        private string activeCollectionId;
        private readonly Dictionary<(string quest, string node), QuestNodeLogic> executions = new Dictionary<(string, string), QuestNodeLogic>();
        private readonly Queue<(Guid run, string quest, string node, string result)> results = new Queue<(Guid, string, string, string)>();
        public string ExecutionStatus(string quest, string node) => executions.TryGetValue((quest, node), out var execution) ? execution.Status : "";
        public QuestBookAsset BookAsset { get => book; set => book = value; }
        public List<ObjectBinding> BindingEntries => bindings;
        public QuestRun ActiveRun { get; private set; }
        public IReadOnlyList<string> Activity => activity;
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
                var objects = new QuestBindings(bindings, gameObject);
                foreach (var definition in definitions)
                    foreach (var node in definition.Stages.Where(n => n.UsesLogicExecution()))
                        QuestLogicCatalog.Validate(node, objects);
                var errors = QuestValidation.Validate(definitions, id => QuestLogicCatalog.Find(id) != null);
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
            var ticked = new HashSet<QuestNodeLogic>();
            int transitions = 128;
            do {
                foreach (var pair in executions.ToArray())
                    if (run.Find(pair.Key.quest)?.Stage(pair.Key.node)?.State != QuestStatus.Active) {
                        executions.Remove(pair.Key); Release(pair.Value);
                    }
                foreach (var owner in run.Instances.Where(q => q.State == QuestStatus.Active))
                    foreach (var node in owner.Stages.Where(n => n.UsesLogic && n.State == QuestStatus.Active)) {
                        var key = (owner.DefinitionId, node.Id);
                        if (!executions.TryGetValue(key, out var execution)) {
                            if (node.IsPaused) continue;
                            var definition = node.DefinitionSnapshot();
                            var objects = new QuestBindings(bindings, gameObject);
                            var logic = QuestLogicCatalog.Validate(definition, objects);
                            execution = logic;
                            executions.Add(key, execution);
                            execution.Start(objects, result => { if (ActiveRun == run && !run.IsClosed) results.Enqueue((run.Id, key.Item1, key.Item2, result)); });
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
                    if (--transitions == 0) throw new InvalidOperationException("Node transition budget exceeded.");
                    var accepted = run.CompleteNode(result.run, result.quest, result.node, result.result);
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
            while (ActiveRun != null && ActiveRun.TryRead(out var notice))
                Record(notice.QuestId + "/" + notice.StageId + " " + notice.Kind + ": " + notice.Detail);
        }
        private void Release(QuestNodeLogic execution, QuestStopReason reason = QuestStopReason.Cancelled)
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

