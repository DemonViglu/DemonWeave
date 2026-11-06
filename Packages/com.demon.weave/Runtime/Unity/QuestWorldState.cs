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
        [SerializeField] private List<QuestWorldFact> facts = new List<QuestWorldFact>();
        [SerializeField] private bool keepAcrossScenes = true;
        private readonly Dictionary<string, QuestWorldFact> definitions = new Dictionary<string, QuestWorldFact>();
        private readonly Dictionary<string, bool> values = new Dictionary<string, bool>();
        private readonly HashSet<string> completed = new HashSet<string>();
        public IReadOnlyList<QuestWorldFact> Facts => facts;
        public static QuestWorldState Current { get; private set; }
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetCurrent() => Current = null;
        private void Awake()
        {
            if (Current && Current != this) { foreach (var fact in facts) Current.Register(fact); enabled = false; return; }
            Current = this;
            foreach (var fact in facts) Register(fact);
            if (keepAcrossScenes && transform.parent == null) DontDestroyOnLoad(gameObject);
        }
        private void OnDestroy() { if (Current == this) Current = null; }
        public void Register(QuestWorldFact fact)
        {
            if (!fact || string.IsNullOrEmpty(fact.Id)) throw new ArgumentException("World fact asset or ID is missing.");
            if (definitions.TryGetValue(fact.Id, out var existing) && existing != fact) throw new ArgumentException("Two world facts share an ID: " + fact.Id);
            definitions[fact.Id] = fact;
            if (!values.ContainsKey(fact.Id)) values.Add(fact.Id, fact.InitialValue);
        }
        public bool? Read(string id) => values.TryGetValue(id, out var value) ? value : (bool?)null;
        public void Set(QuestWorldFact fact, bool value) { Register(fact); values[fact.Id] = value; }
        private static string Key(string collection, string quest) => Uri.EscapeDataString(collection) + "/" + Uri.EscapeDataString(quest);
        public bool HasCompleted(string collection, string quest) => completed.Contains(Key(collection, quest));
        public void RecordCompletion(string collection, string quest) => completed.Add(Key(collection, quest));
        [Serializable] private sealed class FactValue { public string Id; public bool Value; }
        [Serializable] private sealed class Snapshot { public int Version = 1; public List<FactValue> Facts = new List<FactValue>(); public List<string> Completed = new List<string>(); }
        public string ExportState() => JsonUtility.ToJson(new Snapshot {
            Facts = values.OrderBy(p => p.Key).Select(p => new FactValue { Id = p.Key, Value = p.Value }).ToList(),
            Completed = completed.OrderBy(id => id).ToList()
        }, true);
        public void ImportState(string json)
        {
            var document = Newtonsoft.Json.Linq.JObject.Parse(json);
            if ((int?)document["Version"] != 1 || document["Facts"] is not Newtonsoft.Json.Linq.JArray || document["Completed"] is not Newtonsoft.Json.Linq.JArray) throw new ArgumentException("Invalid world-state snapshot schema.");
            var data = JsonUtility.FromJson<Snapshot>(json);
            if (data == null || data.Version != 1 || data.Facts == null || data.Completed == null ||
                data.Facts.Any(f => f == null || string.IsNullOrWhiteSpace(f.Id)) || data.Facts.Select(f => f.Id).Distinct().Count() != data.Facts.Count ||
                data.Completed.Any(string.IsNullOrWhiteSpace) || data.Completed.Distinct().Count() != data.Completed.Count)
                throw new ArgumentException("Invalid world-state snapshot.");
            var restoredValues = data.Facts.ToDictionary(f => f.Id, f => f.Value);
            var restoredDefinitions = new Dictionary<string, QuestWorldFact>(definitions);
            foreach (var fact in facts) {
                if (!fact || string.IsNullOrEmpty(fact.Id)) throw new ArgumentException("A registered world fact is missing.");
                if (restoredDefinitions.TryGetValue(fact.Id, out var existing) && existing != fact) throw new ArgumentException("Conflicting world fact IDs.");
                restoredDefinitions[fact.Id] = fact;
                if (!restoredValues.ContainsKey(fact.Id)) restoredValues.Add(fact.Id, fact.InitialValue);
            }
            values.Clear(); foreach (var pair in restoredValues) values.Add(pair.Key, pair.Value);
            definitions.Clear(); foreach (var pair in restoredDefinitions) definitions.Add(pair.Key, pair.Value);
            completed.Clear(); foreach (var id in data.Completed) completed.Add(id);
        }
    }
}
