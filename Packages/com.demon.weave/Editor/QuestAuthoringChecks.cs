using System;
using System.Collections.Generic;
using System.Linq;
using DemonWeave.Quests;
using DemonWeave.Unity;
using UnityEditor;
using UnityEngine;

namespace DemonWeave.Editor
{
    public static class QuestAuthoringChecks
    {
        public static IReadOnlyList<QuestDiagnostic> Validate(QuestBookAsset book, IQuestAuthoringHost host, IReadOnlyList<QuestDefinition> source = null)
        {
            if (!book) return new[] { new QuestDiagnostic("book", "required", "Select a Quest Collection.") };
            try
            {
                var dependencyErrors = ValidateExternalDependencies(book, source);
                if (dependencyErrors.Count > 0) return dependencyErrors;
                var definitions = QuestBookAsset.RuntimeDefinitions(source ?? book.Quests);
                if (definitions.Count == 0) return Array.Empty<QuestDiagnostic>();
                var bindings = host == null ? null : new QuestBindings(host.BindingEntries);
                var catalog = host?.CreateCapabilities();
                if (host is QuestSceneHost sceneHost) {
                    var stateError = sceneHost.ValidateWorldBindings(definitions);
                    if (!string.IsNullOrEmpty(stateError)) return new[] { new QuestDiagnostic("World State", "state.configuration", stateError) };
                }
                if (host != null)
                    foreach (var quest in definitions) foreach (var node in quest.Stages.Where(n => n.Control == QuestControl.None && n.Results.Count > 0)) {
                        var entity = host.BindingEntries.FirstOrDefault(b => b.Role == node.EntityRole)?.Entity;
                        var error = !entity ? "Target Entity is missing." : !entity.Results.SequenceEqual(node.Results) ? "Entity result contract differs. Rebind the target to refresh it." : entity.CheckConfiguration();
                        if (!string.IsNullOrEmpty(error)) return new[] { new QuestDiagnostic(quest.Title + "/" + node.Title, "entity.configuration", error) };
                    }
                return QuestValidation.Validate(definitions,
                    bindings == null ? (Func<string, bool>)null : role => bindings.Find(role),
                    catalog == null ? (Func<string, string, string>)null : catalog.Validate,
                    host == null ? (Func<string, string, bool, string>)null : (role, key, isEvent) => host.Signals.Any(s => s.Role == role && s.Key == key && s.IsEvent == isEvent) ? "" : "Unregistered fact/event: " + role + "." + key);
            }
            catch (Exception e) { return new[] { new QuestDiagnostic("book", "invalid", e.Message) }; }
        }
        private static IReadOnlyList<QuestDiagnostic> ValidateExternalDependencies(QuestBookAsset current, IReadOnlyList<QuestDefinition> source)
        {
            var errors = new List<QuestDiagnostic>();
            var books = AssetDatabase.FindAssets("t:QuestBookAsset").Select(g => AssetDatabase.LoadAssetAtPath<QuestBookAsset>(AssetDatabase.GUIDToAssetPath(g))).Where(b => b).Append(current).Distinct();
            var lookup = books.SelectMany(b => (b == current && source != null ? source : b.Quests).Select(q => (book: b, quest: q))).GroupBy(p => (p.book.Id, p.quest.Id)).ToDictionary(g => g.Key, g => g.ToArray());
            IEnumerable<QuestCondition> External(QuestCondition c) {
                if (c == null) yield break;
                if (c.Kind == ConditionKind.ExternalQuestCompleted) yield return c;
                if (c.Children != null) foreach (var child in c.Children) foreach (var rule in External(child)) yield return rule;
            }
            var visited = new HashSet<(string, string)>(); var chain = new HashSet<(string, string)>();
            void Visit((string, string) key) {
                if (chain.Contains(key)) { errors.Add(new QuestDiagnostic("Quest dependency", "dependency.cycle", "Cross-collection prerequisite cycle detected.")); return; }
                if (!visited.Add(key)) return;
                if (!lookup.TryGetValue(key, out var matches)) { errors.Add(new QuestDiagnostic("Quest dependency", "dependency.missing", "Source collection or Quest is missing.")); return; }
                if (matches.Length != 1) { errors.Add(new QuestDiagnostic("Quest dependency", "dependency.ambiguous", "Multiple assets share this collection and Quest identity.")); return; }
                chain.Add(key);
                var quest = matches[0].quest;
                var rules = new List<QuestCondition> { quest.Accept, quest.RunWhen };
                foreach (var node in quest.Stages) rules.AddRange(new[] { node.OpenWhen, node.RunWhen });
                foreach (var source in rules.SelectMany(External)) Visit((source.Subject, source.Key));
                chain.Remove(key);
            }
            foreach (var quest in source ?? current.Quests) Visit((current.Id, quest.Id));
            return errors;
        }
        public static void Edit(QuestBookAsset book, string label, Action<List<QuestDefinition>> edit)
        {
            if (EditorApplication.isPlaying) throw new InvalidOperationException("Exit Play to edit definitions. Running instances use snapshots.");
            book.OnBeforeSerialize();
            Undo.RegisterCompleteObjectUndo(book, label);
            edit(book.Quests); book.OnBeforeSerialize(); EditorUtility.SetDirty(book);
        }
        public static IEnumerable<QuestCondition> Dependencies(QuestCondition condition)
        {
            if (condition == null) yield break;
            if (condition.Kind == ConditionKind.QuestState) yield return condition;
            if (condition.Children != null)
                foreach (var child in condition.Children)
                    foreach (var dependency in Dependencies(child)) yield return dependency;
        }
        public static IEnumerable<QuestExec> Executions(QuestDefinition q) => q.BeginExec.Concat(q.FinishExec).Concat(q.FailExec)
            .Concat(q.Stages.SelectMany(s => s.BeginExec.Concat(s.FinishExec).Concat(s.Routes.SelectMany(r => r.SelectedExec))));
        public static bool References(QuestDefinition q, string id) => q.ParentId == id
            || Dependencies(q.Accept).Any(c => c.Subject == id)
            || q.Finish.Concat(q.Fail).Concat(q.Stages.SelectMany(s => s.Objectives.Concat(s.Routes.SelectMany(r => r.Objectives))))
                .Any(c => c.Kind == ContentKind.QuestState && c.Subject == id)
            || q.Stages.SelectMany(s => new[] { s.OpenWhen }.Concat(s.Routes.Select(r => r.AvailableWhen)).Concat(s.Switches.Select(t => t.When)))
                .SelectMany(Dependencies).Any(c => c.Subject == id);
        public static void RemoveCondition(QuestDefinition quest, QuestCondition target)
        {
            if (ReferenceEquals(quest.Accept, target)) { quest.Accept = new QuestCondition { Kind = ConditionKind.All }; return; }
            RemoveChild(quest.Accept, target);
        }
        private static bool RemoveChild(QuestCondition parent, QuestCondition target)
        {
            if (parent?.Children == null) return false;
            if (parent.Children.Remove(target)) return true;
            return parent.Children.Any(child => RemoveChild(child, target));
        }
        public static string ConditionText(QuestCondition condition, Func<string, string> title)
        {
            if (condition == null) return "Missing condition";
            if (condition.Kind == ConditionKind.QuestState) return title(condition.Subject) + " / " + Status(condition.State);
            if (condition.Kind == ConditionKind.StageState) return "Node " + condition.Subject + " / " + Status(condition.State);
            if (condition.Kind == ConditionKind.WorldFact) {
                var fact = AssetDatabase.FindAssets("t:QuestWorldFact").Select(g => AssetDatabase.LoadAssetAtPath<QuestWorldFact>(AssetDatabase.GUIDToAssetPath(g))).FirstOrDefault(f => f && f.Id == condition.Subject);
                return (fact ? (string.IsNullOrEmpty(fact.Label) ? fact.name : fact.Label) : "Missing world fact") + " = " + condition.Expected;
            }
            if (condition.Kind == ConditionKind.ExternalQuestCompleted) {
                var book = AssetDatabase.FindAssets("t:QuestBookAsset").Select(g => AssetDatabase.LoadAssetAtPath<QuestBookAsset>(AssetDatabase.GUIDToAssetPath(g))).FirstOrDefault(b => b && b.Id == condition.Subject);
                return (book ? book.Quests.FirstOrDefault(q => q.Id == condition.Key)?.Title ?? "Missing Quest" : "Missing collection") + " completed";
            }
            if (condition.Kind == ConditionKind.StageResult) return "Node " + condition.Subject + " / " + condition.Key;
            if (condition.Kind == ConditionKind.Fact) return condition.Subject + "." + condition.Key + " = " + condition.Expected;
            if (condition.Children == null || condition.Children.Count == 0) return condition.Kind == ConditionKind.All ? "No prerequisites" : "Empty Any condition";
            return (condition.Kind == ConditionKind.All ? "All" : "Any") + "（" + string.Join("；", condition.Children.Select(c => ConditionText(c, title))) + "）";
        }
        public static string Status(QuestStatus value)
        {
            switch (value) { case QuestStatus.Waiting: return "Waiting"; case QuestStatus.Active: return "Active";
                case QuestStatus.Completed: return "Completed"; case QuestStatus.Failed: return "Failure"; default: return "Cancelled"; }
        }
    }
}
