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
        public static IReadOnlyList<QuestDiagnostic> Validate(QuestBookAsset book, IQuestAuthoringHost host)
        {
            if (!book) return new[] { new QuestDiagnostic("book", "required", "Select a Quest Collection.") };
            try
            {
                var definitions = book.RuntimeDefinitions();
                if (definitions.Count == 0) return Array.Empty<QuestDiagnostic>();
                var bindings = host == null ? null : new QuestBindings(host.BindingEntries);
                var catalog = host?.CreateCapabilities();
                return QuestValidation.Validate(definitions,
                    bindings == null ? (Func<string, bool>)null : role => bindings.Find(role),
                    catalog == null ? (Func<string, string, string>)null : catalog.Validate,
                    host == null ? (Func<string, string, bool, string>)null : (role, key, isEvent) => host.Signals.Any(s => s.Role == role && s.Key == key && s.IsEvent == isEvent) ? "" : "Unregistered fact/event: " + role + "." + key);
            }
            catch (Exception e) { return new[] { new QuestDiagnostic("book", "invalid", e.Message) }; }
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
