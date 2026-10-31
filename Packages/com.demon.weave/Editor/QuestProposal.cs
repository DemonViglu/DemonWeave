using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using DemonWeave.Quests;
using DemonWeave.Unity;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;

namespace DemonWeave.Editor
{
    public sealed class QuestProposal
    {
        [JsonProperty(Required = Required.Always)] public int Schema;
        [JsonProperty(Required = Required.Always)] public string BookId;
        [JsonProperty(Required = Required.Always)] public string BaseRevision;
        [JsonProperty(Required = Required.Always)] public string Summary;
        [JsonProperty(Required = Required.Always)] public List<QuestPatchOperation> Operations;
    }
    public sealed class QuestPatchOperation
    {
        [JsonProperty(Required = Required.Always)] public string Op;
        [JsonProperty(Required = Required.Always)] public string QuestId;
        public string Field;
        public string StageId;
        public JToken Value;
    }
    public sealed class PreparedQuestChange
    {
        internal QuestBookAsset Book;
        internal string Revision, BindingRevision;
        internal List<QuestDefinition> Definitions;
        internal bool AllowUnbound;
        public string Summary { get; internal set; }
        public string Diff { get; internal set; }
    }
    public static class QuestProposalReview
    {
        private static readonly HashSet<string> Fields = new HashSet<string>(new[] {
            "Title", "ParentId", "Accept", "FinishCombination", "FailCombination", "Finish", "Fail", "BeginExec", "FinishExec", "FailExec", "CancelChildrenOnEnd", "Stages"
        }, StringComparer.Ordinal);
        public static string Revision(QuestBookAsset book) => Hash(book.Id + "\n" + book.Title + "\n" + book.ExportDefinitions());
        private static string Bindings(IQuestAuthoringHost host) => host == null ? "unbound" : Hash(string.Join("\n", host.BindingEntries.OrderBy(b => b.Role).Select(b => b.Role + ":" + (b.Target ? (b.Entity ? b.Entity.Id : "object") + ":" + b.Target.GetEntityId() : "missing"))));
        private static string Hash(string value)
        { using (var sha = SHA256.Create()) return BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(value))).Replace("-", "").ToLowerInvariant(); }
        public static PreparedQuestChange Prepare(QuestBookAsset book, IQuestAuthoringHost host, string proposalJson, bool tableImport = false)
        {
            if (!book || host == null && !tableImport) throw new ArgumentException("Select a Quest Collection and Scene Host for validation.");
            if (proposalJson == null || proposalJson.Length > (tableImport ? 16000000 : 200000)) throw new ArgumentException("Proposal is empty or too large.");
            QuestProposal proposal;
            using (var reader = new JsonTextReader(new StringReader(proposalJson)) { MaxDepth = 64, DateParseHandling = DateParseHandling.None })
            {
                var token = JToken.Load(reader, new JsonLoadSettings { DuplicatePropertyNameHandling = DuplicatePropertyNameHandling.Error });
                if (token.Type != JTokenType.Object || reader.Read()) throw new ArgumentException("Expected one JSON object without Markdown.");
                proposal = token.ToObject<QuestProposal>(JsonSerializer.Create(new JsonSerializerSettings { TypeNameHandling = TypeNameHandling.None, MissingMemberHandling = MissingMemberHandling.Error }));
            }
            if (proposal.Schema != 1 || proposal.BookId != book.Id) throw new ArgumentException("Proposal version or collection mismatch.");
            if (proposal.BaseRevision != Revision(book)) throw new InvalidOperationException("Proposal is stale. Generate a new proposal.");
            if (proposal.Operations == null || proposal.Operations.Count == 0 || proposal.Operations.Count > (tableImport ? 10000 : 64)) throw new ArgumentException("Patch operation limit exceeded.");
            var array = JArray.Parse(book.ExportDefinitions()); var diff = new StringBuilder();
            foreach (var operation in proposal.Operations)
            {
                if (operation == null || string.IsNullOrWhiteSpace(operation.QuestId)) throw new ArgumentException("Each operation requires a stable Quest ID.");
                var quest = array.OfType<JObject>().SingleOrDefault(q => (string)q["Id"] == operation.QuestId);
                var path = "quests[" + operation.QuestId + "]";
                if (operation.Op == "add")
                {
                    if (!string.IsNullOrEmpty(operation.StageId)) throw new ArgumentException("StageId only applies to set-stage.");
                    if (quest != null || operation.Value is not JObject value || (string)value["Id"] != operation.QuestId || !string.IsNullOrEmpty(operation.Field))
                        throw new ArgumentException(path + ": add requires a new quest and a unique ID.");
                    array.Add(value.DeepClone()); diff.AppendLine("+ " + path + "\n" + value.ToString(Formatting.Indented));
                }
                else if (operation.Op == "remove")
                {
                    if (!string.IsNullOrEmpty(operation.StageId)) throw new ArgumentException("StageId only applies to set-stage.");
                    if (quest == null || !string.IsNullOrEmpty(operation.Field) || operation.Value != null) throw new ArgumentException(path + ": invalid remove operation.");
                    diff.AppendLine("- " + path + "\n" + quest.ToString(Formatting.Indented)); quest.Remove();
                }
                else if (operation.Op == "set")
                {
                    if (!string.IsNullOrEmpty(operation.StageId)) throw new ArgumentException("StageId only applies to set-stage.");
                    if (quest == null || !Fields.Contains(operation.Field ?? "") || operation.Value == null) throw new ArgumentException(path + ": set requires an existing quest and an allowed field.");
                    diff.AppendLine("~ " + path + "." + operation.Field + "\nBefore: " + quest[operation.Field]?.ToString(Formatting.Indented) + "\nAfter: " + operation.Value.ToString(Formatting.Indented));
                    quest[operation.Field] = operation.Value.DeepClone();
                }
                else if (operation.Op == "set-stage")
                {
                    var allowed = new[] { "TypeId", "EntityRole", "Title", "Required", "After", "OpenWhen", "Objectives", "Completion", "BeginExec", "FinishExec", "Routes", "Switches" };
                    var stage = (quest?["Stages"] as JArray)?.OfType<JObject>().SingleOrDefault(s => (string)s["Id"] == operation.StageId);
                    if (stage == null || string.IsNullOrWhiteSpace(operation.StageId) || !allowed.Contains(operation.Field) || operation.Value == null)
                        throw new ArgumentException(path + ": set-stage requires an existing stage ID and allowed field.");
                    diff.AppendLine("~ " + path + ".stages[" + operation.StageId + "]." + operation.Field + "\nBefore: " + stage[operation.Field] + "\nAfter: " + operation.Value);
                    stage[operation.Field] = operation.Value.DeepClone();
                }
                else throw new ArgumentException("Unknown operation: " + operation.Op);
            }
            var definitions = QuestBookAsset.ParseDefinitions(array.ToString(Formatting.None));
            Validate(definitions, host);
            return new PreparedQuestChange { Book = book, Revision = Revision(book), BindingRevision = Bindings(host), Definitions = definitions,
                Summary = proposal.Summary, Diff = diff.ToString(), AllowUnbound = tableImport };
        }
        private static void Validate(IReadOnlyList<QuestDefinition> definitions, IQuestAuthoringHost host)
        {
            definitions = QuestBookAsset.RuntimeDefinitions(definitions);
            if (definitions.Count == 0) return;
            var bindings = host == null ? null : new QuestBindings(host.BindingEntries); var catalog = host?.CreateCapabilities();
            var errors = QuestValidation.Validate(definitions, bindings == null ? (Func<string,bool>)null : role => bindings.Find(role), catalog == null ? (Func<string,string,string>)null : catalog.Validate,
                host == null ? (Func<string,string,bool,string>)null : (role, key, isEvent) => host.Signals.Any(s => s.Role == role && s.Key == key && s.IsEvent == isEvent) ? "" : "Unregistered signal: " + role + "." + key);
            if (errors.Count > 0) throw new ArgumentException(string.Join("\n", errors));
        }
        public static void Apply(PreparedQuestChange prepared, IQuestAuthoringHost host)
        {
            if (prepared == null || !prepared.Book || host == null && !prepared.AllowUnbound) throw new ArgumentException("Review a valid proposal first.");
            if (EditorApplication.isPlaying) throw new InvalidOperationException("Exit Play before applying a definition.");
            if (Revision(prepared.Book) != prepared.Revision || Bindings(host) != prepared.BindingRevision)
                throw new InvalidOperationException("Content or bindings changed. Review again.");
            Validate(prepared.Definitions, host);
            QuestAuthoringChecks.Edit(prepared.Book, "Apply Quest Proposal", quests => { quests.Clear(); quests.AddRange(prepared.Definitions.Select(q => q.Copy())); });
            if (AssetDatabase.Contains(prepared.Book)) AssetDatabase.SaveAssetIfDirty(prepared.Book);
        }
    }
}
