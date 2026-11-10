using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace DemonWeave.Unity
{
    [DefaultExecutionOrder(-1000)]
    [AddComponentMenu("DemonWeave/World State")]
    public sealed class QuestWorldState : MonoBehaviour
    {
        [SerializeField] private QuestFactTable table;
        [SerializeField] private bool keepAcrossScenes = true;
        private readonly Dictionary<string, QuestWorldFact> definitions = new Dictionary<string, QuestWorldFact>();
        private readonly Dictionary<string, bool> values = new Dictionary<string, bool>();
        private readonly HashSet<string> completed = new HashSet<string>();
        public IReadOnlyList<QuestWorldFact> Facts => table ? table.Entries : Array.Empty<QuestWorldFact>();
        public static QuestWorldState Current { get; private set; }
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetCurrent() => Current = null;
        private void Awake()
        {
            if (Current && Current != this) { foreach (var fact in Facts) Current.Register(fact); enabled = false; return; }
            Current = this;
            foreach (var fact in Facts) Register(fact);
            if (keepAcrossScenes && transform.parent == null) DontDestroyOnLoad(gameObject);
        }
        private void OnDestroy() { if (Current == this) Current = null; }
        public void Register(QuestWorldFact fact)
        {
            if (fact == null || string.IsNullOrEmpty(fact.Id)) throw new ArgumentException("World fact asset or ID is missing.");
            if (definitions.TryGetValue(fact.Id, out var existing) && existing != fact) throw new ArgumentException("Two world facts share an ID: " + fact.Id);
            definitions[fact.Id] = fact;
            if (!values.ContainsKey(fact.Id)) values.Add(fact.Id, fact.InitialValue);
        }
        // Read-only copy of currently registered runtime facts.
        public IEnumerable<KeyValuePair<string, bool>> RuntimeFacts => values.OrderBy(pair => LabelFor(pair.Key)).ToArray();
        public string LabelFor(string id) => definitions.TryGetValue(id, out var fact) && fact != null
            ? string.IsNullOrWhiteSpace(fact.Label) ? fact.Id : fact.Label : id;
        public void Set(string id, bool value)
        {
            if (!values.ContainsKey(id)) throw new ArgumentException("World fact has not been registered: " + id);
            values[id] = value;
        }
        public bool? Read(string id) => !string.IsNullOrEmpty(id) && values.TryGetValue(id, out var value) ? value : (bool?)null;
        public bool? Read(QuestFactReference fact) => Read(fact.Id);
        public void Set(QuestFactReference fact, bool value) => Set(fact.Id, value);
        private static string Key(string collection, string quest) => Uri.EscapeDataString(collection) + "/" + Uri.EscapeDataString(quest);
        public bool HasCompleted(string collection, string quest) => completed.Contains(Key(collection, quest));
        public void RecordCompletion(string collection, string quest) => completed.Add(Key(collection, quest));
    }
}
