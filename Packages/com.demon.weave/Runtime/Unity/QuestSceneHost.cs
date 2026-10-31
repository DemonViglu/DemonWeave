using System;
using System.Collections.Generic;
using System.Linq;
using DemonWeave.Quests;
using UnityEngine;

namespace DemonWeave.Unity
{
    // Scene persistence and run ownership. Authors bind targets in Quest Editor, not on this component.
    [AddComponentMenu("")]
    public sealed class QuestSceneHost : MonoBehaviour, IQuestAuthoringHost, IQuestWorld
    {
        [SerializeField, HideInInspector] private QuestBookAsset book;
        [SerializeField, HideInInspector] private List<EntityBinding> bindings = new List<EntityBinding>();
        private readonly List<string> activity = new List<string>();
        private CapabilityCatalog capabilities;
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
            ActiveRun.Refresh(); int budget = 128;
            while (ActiveRun != null && ActiveRun.TryRead(out var notice))
            {
                if (--budget == 0) throw new InvalidOperationException("Quest action budget exceeded.");
                Record(notice.QuestId + "/" + notice.StageId + " " + notice.Kind + ": " + notice.Detail);
                if (notice.Kind != QuestNoticeKind.Exec || !ActiveRun.MayExecute(notice)) continue;
                var result = capabilities.Execute(notice.Execution.CapabilityId, notice.Execution.ArgumentsJson);
                Record(notice.Execution.CapabilityId + ": " + (result.Success ? "OK / " : "Failed / ") + result.Message);
                // Only the next fact evaluation can complete a Quest, never the action return value itself.
            }
        }
        private void Record(string message) { if (activity.Count >= 100) activity.RemoveAt(0); activity.Add(message); }
        private void Fail(Exception e) { Record(e.Message); ActiveRun?.Dispose(); ActiveRun = null; Debug.LogError(e.Message, this); }
        public void StopRun() { ActiveRun?.Dispose(); ActiveRun = null; }
        private void OnDisable() => StopRun();
    }
}

