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
        public JToken Value;
    }
    public sealed class PreparedQuestChange
    {
        internal QuestBookAsset Book;
        internal string Revision, BindingRevision;
        internal List<QuestDefinition> Definitions;
        public string Summary { get; internal set; }
        public string Diff { get; internal set; }
    }
    public static class QuestProposalReview
    {
        private static readonly HashSet<string> Fields = new HashSet<string>(new[] {
            "Title", "ParentId", "Accept", "FinishCombination", "FailCombination", "Finish", "Fail", "BeginExec", "FinishExec", "FailExec", "CancelChildrenOnEnd"
        }, StringComparer.Ordinal);
        public static string Revision(QuestBookAsset book) => Hash(book.Id + "\n" + book.Title + "\n" + book.ExportDefinitions());
        private static string Bindings(IQuestAuthoringHost host) => Hash(string.Join("\n", host.BindingEntries.OrderBy(b => b.Role).Select(b => b.Role + ":" + (b.Entity ? b.Entity.Id + ":" + b.Entity.GetEntityId() : "missing"))));
        private static string Hash(string value)
        { using (var sha = SHA256.Create()) return BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(value))).Replace("-", "").ToLowerInvariant(); }
        public static PreparedQuestChange Prepare(QuestBookAsset book, IQuestAuthoringHost host, string proposalJson)
        {
            if (!book || host == null) throw new ArgumentException("需要任务集与场景宿主，才能校验对象、事实和行为。 ");
            if (proposalJson == null || proposalJson.Length > 200000) throw new ArgumentException("提案为空或过大。 ");
            QuestProposal proposal;
            using (var reader = new JsonTextReader(new StringReader(proposalJson)) { MaxDepth = 64, DateParseHandling = DateParseHandling.None })
            {
                var token = JToken.Load(reader, new JsonLoadSettings { DuplicatePropertyNameHandling = DuplicatePropertyNameHandling.Error });
                if (token.Type != JTokenType.Object || reader.Read()) throw new ArgumentException("提案必须为单个 JSON 对象，不含 Markdown。 ");
                proposal = token.ToObject<QuestProposal>(JsonSerializer.Create(new JsonSerializerSettings { TypeNameHandling = TypeNameHandling.None, MissingMemberHandling = MissingMemberHandling.Error }));
            }
            if (proposal.Schema != 1 || proposal.BookId != book.Id) throw new ArgumentException("提案版本或目标任务集不匹配。 ");
            if (proposal.BaseRevision != Revision(book)) throw new InvalidOperationException("提案已过期：定义发生变化，请重新生成。 ");
            if (proposal.Operations == null || proposal.Operations.Count == 0 || proposal.Operations.Count > 64) throw new ArgumentException("提案需要 1–64 个局部操作。 ");
            var array = JArray.Parse(book.ExportDefinitions()); var diff = new StringBuilder();
            foreach (var operation in proposal.Operations)
            {
                if (operation == null || string.IsNullOrWhiteSpace(operation.QuestId)) throw new ArgumentException("每个操作必须指定稳定任务 ID。 ");
                var quest = array.OfType<JObject>().SingleOrDefault(q => (string)q["Id"] == operation.QuestId);
                var path = "quests[" + operation.QuestId + "]";
                if (operation.Op == "add")
                {
                    if (quest != null || operation.Value is not JObject value || (string)value["Id"] != operation.QuestId || !string.IsNullOrEmpty(operation.Field))
                        throw new ArgumentException(path + ": add 必须提供新任务且不能覆盖既有 ID。 ");
                    array.Add(value.DeepClone()); diff.AppendLine("+ " + path + "\n" + value.ToString(Formatting.Indented));
                }
                else if (operation.Op == "remove")
                {
                    if (quest == null || !string.IsNullOrEmpty(operation.Field) || operation.Value != null) throw new ArgumentException(path + ": remove 操作无效。 ");
                    diff.AppendLine("- " + path + "\n" + quest.ToString(Formatting.Indented)); quest.Remove();
                }
                else if (operation.Op == "set")
                {
                    if (quest == null || !Fields.Contains(operation.Field ?? "") || operation.Value == null) throw new ArgumentException(path + ": set 需要已有任务和允许的字段。 ");
                    diff.AppendLine("~ " + path + "." + operation.Field + "\n之前：" + quest[operation.Field]?.ToString(Formatting.Indented) + "\n之后：" + operation.Value.ToString(Formatting.Indented));
                    quest[operation.Field] = operation.Value.DeepClone();
                }
                else throw new ArgumentException("未知操作：" + operation.Op);
            }
            var definitions = QuestBookAsset.ParseDefinitions(array.ToString(Formatting.None));
            Validate(definitions, host);
            return new PreparedQuestChange { Book = book, Revision = Revision(book), BindingRevision = Bindings(host), Definitions = definitions,
                Summary = proposal.Summary, Diff = diff.ToString() };
        }
        private static void Validate(IReadOnlyList<QuestDefinition> definitions, IQuestAuthoringHost host)
        {
            var bindings = new QuestBindings(host.BindingEntries); var catalog = host.CreateCapabilities();
            var errors = QuestValidation.Validate(definitions, role => bindings.Find(role), catalog.Validate,
                (role, key, isEvent) => host.Signals.Any(s => s.Role == role && s.Key == key && s.IsEvent == isEvent) ? "" : "未注册信号：" + role + "." + key);
            if (errors.Count > 0) throw new ArgumentException(string.Join("\n", errors));
        }
        public static void Apply(PreparedQuestChange prepared, IQuestAuthoringHost host)
        {
            if (prepared == null || !prepared.Book || host == null) throw new ArgumentException("先审查有效提案。 ");
            if (EditorApplication.isPlaying) throw new InvalidOperationException("本阶段只采用未运行定义；请先退出 Play。 ");
            if (Revision(prepared.Book) != prepared.Revision || Bindings(host) != prepared.BindingRevision)
                throw new InvalidOperationException("审查后内容或对象绑定已变化，请重新审查。 ");
            Validate(prepared.Definitions, host);
            QuestAuthoringChecks.Edit(prepared.Book, "采用任务局部提案", quests => { quests.Clear(); quests.AddRange(prepared.Definitions.Select(q => q.Copy())); });
            if (AssetDatabase.Contains(prepared.Book)) AssetDatabase.SaveAssetIfDirty(prepared.Book);
        }
    }
}
