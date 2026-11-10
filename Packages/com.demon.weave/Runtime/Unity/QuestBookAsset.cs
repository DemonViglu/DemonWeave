using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using DemonWeave.Quests;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace DemonWeave.Unity
{
    [CreateAssetMenu(menuName = "DemonWeave/Quest Collection", fileName = "QuestCollection")]
    public sealed class QuestBookAsset : ScriptableObject, ISerializationCallbackReceiver
    {
        public string Id = "";
        public string Title = "";
        [SerializeField, HideInInspector] private string definitionJson = "";
        [SerializeField, HideInInspector] private string editorLayoutJson = "";
        [NonSerialized] private List<QuestDefinition> definitions;
        private static JsonSerializerSettings Settings => new JsonSerializerSettings {
            TypeNameHandling = TypeNameHandling.None, MissingMemberHandling = MissingMemberHandling.Error,
            MaxDepth = 64, Converters = { new StringEnumConverter { AllowIntegerValues = false } }
        };
        // Unity's inline serializer cannot safely store recursive condition types. One JSON value is
        // authoritative; the editable/cache view is reconstructed after load/Undo, never serialized twice.
        public List<QuestDefinition> Quests
        {
            get => definitions ??= string.IsNullOrEmpty(definitionJson) ? new List<QuestDefinition>()
                : ParseDefinitions(definitionJson);
            set => definitions = value ?? throw new ArgumentNullException(nameof(value));
        }
        private void OnValidate() { if (string.IsNullOrEmpty(Id)) Id = Guid.NewGuid().ToString("N"); }
        public bool HasSerializedDefinition => !string.IsNullOrEmpty(definitionJson);
        // A saved definition without a completion contract is an authoring draft.
        public static bool IsDraft(QuestDefinition quest) => quest != null && quest.Stages != null
            && !quest.Stages.Any(s => s != null && s.Required);
        public static IReadOnlyList<QuestDefinition> RuntimeDefinitions(IEnumerable<QuestDefinition> source)
        {
            var definitions = source.ToArray();
            var ids = new HashSet<string>(StringComparer.Ordinal);
            foreach (var quest in definitions)
                if (quest == null || string.IsNullOrWhiteSpace(quest.Id) || !ids.Add(quest.Id))
                    throw new ArgumentException("Quest IDs must be present and unique, including drafts.");
            return definitions.Where(q => !IsDraft(q)).Select(q => q.Copy()).ToArray();
        }
        public IReadOnlyList<QuestDefinition> RuntimeDefinitions() => RuntimeDefinitions(Quests);
        public string ExportDefinitions() => JsonConvert.SerializeObject(Quests, Formatting.Indented, Settings);
        public static List<QuestDefinition> ParseDefinitions(string json)
        {
            using (var reader = new JsonTextReader(new StringReader(json)) { MaxDepth = 64, DateParseHandling = DateParseHandling.None })
            {
                var token = JToken.Load(reader, new JsonLoadSettings { DuplicatePropertyNameHandling = DuplicatePropertyNameHandling.Error });
                if (token.Type != JTokenType.Array || reader.Read()) throw new ArgumentException("Expected exactly one quest array.");
                return token.ToObject<List<QuestDefinition>>(JsonSerializer.Create(Settings)) ?? throw new ArgumentException("quests: null document");
            }
        }
        public void OnBeforeSerialize() { if (definitions != null) definitionJson = ExportDefinitions(); }
        public void OnAfterDeserialize() { definitions = null; }
    }
}
