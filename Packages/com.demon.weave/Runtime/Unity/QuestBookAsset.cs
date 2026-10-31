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
        // A saved definition without a completion contract is an authoring draft.
        public static bool IsDraft(QuestDefinition quest) => quest != null && quest.Finish != null && quest.Stages != null
            && quest.Finish.Count == 0 && !quest.Stages.Any(s => s != null && s.Required);
        public static IReadOnlyList<QuestDefinition> RuntimeDefinitions(IEnumerable<QuestDefinition> source)
        {
            var definitions = source.ToArray();
            var ids = new HashSet<string>(StringComparer.Ordinal);
            foreach (var quest in definitions)
                if (quest == null || string.IsNullOrWhiteSpace(quest.Id) || !ids.Add(quest.Id))
                    throw new ArgumentException("Quest IDs must be present and unique, including drafts.");
            return definitions.Where(q => !IsDraft(q)).Select(q => Compile(q)).ToArray();
        }
        public static QuestDefinition Compile(QuestDefinition source, bool requireOwners = true)
        {
            var result = source.Copy();
            foreach (var node in result.Stages)
            {
                if (node == null) continue;
                if (string.IsNullOrWhiteSpace(node.EntityRole))
                {
                    bool ObjectCondition(QuestCondition c) => c != null && (c.Kind == ConditionKind.Fact || c.Children != null && c.Children.Any(ObjectCondition));
                    bool ObjectGoals(List<QuestContent> goals) => goals != null && goals.Any(g => g != null && (g.Kind == ContentKind.Fact || g.Kind == ContentKind.EventCount));
                    if (requireOwners && (ObjectCondition(node.OpenWhen) || ObjectGoals(node.Objectives) || node.BeginExec.Count + node.FinishExec.Count > 0 || node.Routes.Any(route => ObjectCondition(route.AvailableWhen) || ObjectGoals(route.Objectives) || route.SelectedExec.Count > 0)))
                        throw new ArgumentException("Node " + node.Title + ": select its Entity before running.");
                    continue;
                }
                var owner = node.EntityRole;
                void BindCondition(QuestCondition c)
                {
                    if (c == null) return;
                    if (c.Kind == ConditionKind.Fact) { CheckOwner(c.Subject); c.Subject = owner; }
                    if (c.Children != null) foreach (var child in c.Children) BindCondition(child);
                }
                void CheckOwner(string reference)
                { if (!string.IsNullOrEmpty(reference) && reference != owner) throw new ArgumentException("Node " + node.Title + ": condition/action target differs from its Entity. Use an explicit method parameter for other objects."); }
                void BindGoals(List<QuestContent> goals)
                { if (goals != null) foreach (var goal in goals.Where(g => g != null && (g.Kind == ContentKind.Fact || g.Kind == ContentKind.EventCount))) { CheckOwner(goal.Subject); goal.Subject = owner; } }
                void BindActions(List<QuestExec> actions)
                {
                    if (actions == null) return;
                    foreach (var action in actions.Where(a => a != null))
                    {
                        var args = JObject.Parse(action.ArgumentsJson); CheckOwner((string)args[CapabilityCatalog.TargetKey]);
                        args[CapabilityCatalog.TargetKey] = owner; action.ArgumentsJson = args.ToString(Formatting.None);
                    }
                }
                BindCondition(node.OpenWhen); BindGoals(node.Objectives); BindActions(node.BeginExec); BindActions(node.FinishExec);
                foreach (var route in node.Routes) { BindCondition(route.AvailableWhen); BindGoals(route.Objectives); BindActions(route.SelectedExec); }
                foreach (var change in node.Switches) BindCondition(change.When);
            }
            return result;
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
