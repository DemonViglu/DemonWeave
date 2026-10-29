using System;
using System.Collections.Generic;
using System.IO;
using DemonWeave.Quests;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace DemonWeave.Unity
{
    [CreateAssetMenu(menuName = "DemonWeave/Quest book", fileName = "QuestBook")]
    public sealed class QuestBookAsset : ScriptableObject, ISerializationCallbackReceiver
    {
        public string Id = "";
        public string Title = "";
        [SerializeField, HideInInspector] private string definitionJson = "";
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
        public bool HasSerializedDefinition => !string.IsNullOrEmpty(definitionJson);
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
