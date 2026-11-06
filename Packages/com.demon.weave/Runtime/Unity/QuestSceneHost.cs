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
        [SerializeField, HideInInspector] private List<EntityBinding> bindings = new List<EntityBinding>();
        private readonly List<string> activity = new List<string>();
        private CapabilityCatalog capabilities;
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
        private void Start() => RestartRun();
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
                    foreach (var node in definition.Stages.Where(n => n.Control == QuestControl.None && n.Results.Count > 0)) {
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
                        executions.Remove(pair.Key); pair.Value.Dispose();
                    }
                foreach (var owner in run.Instances.Where(q => q.State == QuestStatus.Active))
                    foreach (var node in owner.Stages.Where(n => n.UsesEntity && n.State == QuestStatus.Active)) {
                        var key = (owner.DefinitionId, node.Id);
                        var entity = bindings.FirstOrDefault(b => b.Role == node.EntityRole)?.Entity;
                        if (!entity || !entity.isActiveAndEnabled) throw new InvalidOperationException(node.Title + ": target component is missing or disabled.");
                        if (!executions.TryGetValue(key, out var execution)) {
                            execution = entity.CreateExecution() ?? throw new InvalidOperationException("CreateExecution returned null.");
                            if (executions.Values.Contains(execution)) throw new InvalidOperationException("Return a fresh execution for each node.");
                            executions.Add(key, execution);
                            execution.Start(result => { if (ActiveRun == run && !run.IsClosed) results.Enqueue((run.Id, key.Item1, key.Item2, result)); });
                        }
                        if (ticked.Add(execution)) execution.Tick();
                    }
                if (results.Count == 0) break;
                while (results.Count > 0) {
                    var result = results.Dequeue();
                    if (--transitions == 0) throw new InvalidOperationException("Entity transition budget exceeded.");
                    run.CompleteEntity(result.run, result.quest, result.node, result.result);
                }
            } while (ActiveRun == run && !run.IsClosed);
            foreach (var pair in executions.ToArray())
                if (run.Find(pair.Key.quest)?.Stage(pair.Key.node)?.State != QuestStatus.Active) { executions.Remove(pair.Key); pair.Value.Dispose(); }
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
        private void Record(string message) { if (activity.Count >= 100) activity.RemoveAt(0); activity.Add(message); }
        private void Fail(Exception e) { Record(e.Message); StopRun(); Debug.LogError(e.Message, this); }
        public void StopRun()
        {
            var run = ActiveRun; ActiveRun = null; run?.Dispose(); results.Clear();
            while (run != null && run.TryRead(out var stoppedNotice))
                Record(stoppedNotice.QuestId + "/" + stoppedNotice.StageId + " " + stoppedNotice.Kind + ": " + stoppedNotice.Detail);
            var pending = executions.Values.ToArray(); executions.Clear();
            foreach (var execution in pending) try { execution.Dispose(); } catch (Exception e) { Record("Cleanup: " + e.Message); Debug.LogException(e, this); }
        }
        private void OnDisable() => StopRun();
    }
}

