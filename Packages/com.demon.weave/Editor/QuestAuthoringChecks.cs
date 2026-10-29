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
            if (!book) return new[] { new QuestDiagnostic("book", "required", "选择任务集。") };
            try
            {
                var bindings = host == null ? null : new QuestBindings(host.BindingEntries);
                var catalog = host?.CreateCapabilities();
                return QuestValidation.Validate(book.Quests,
                    bindings == null ? (Func<string, bool>)null : role => bindings.Find(role),
                    catalog == null ? (Func<string, string, string>)null : catalog.Validate,
                    host == null ? (Func<string, string, bool, string>)null : (role, key, isEvent) => host.Signals.Any(s => s.Role == role && s.Key == key && s.IsEvent == isEvent) ? "" : "未注册的事实/事件：" + role + "." + key);
            }
            catch (Exception e) { return new[] { new QuestDiagnostic("book", "invalid", e.Message) }; }
        }
        public static void Edit(QuestBookAsset book, string label, Action<List<QuestDefinition>> edit)
        {
            if (EditorApplication.isPlaying) throw new InvalidOperationException("退出 Play 后修改任务定义；运行实例使用快照。");
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
            if (condition == null) return "缺少条件";
            if (condition.Kind == ConditionKind.QuestState) return title(condition.Subject) + " / " + Status(condition.State);
            if (condition.Kind == ConditionKind.Fact) return condition.Subject + "." + condition.Key + " = " + condition.Expected;
            if (condition.Children == null || condition.Children.Count == 0) return condition.Kind == ConditionKind.All ? "无前置限制" : "任一条件组为空";
            return (condition.Kind == ConditionKind.All ? "全部满足" : "任一满足") + "（" + string.Join("；", condition.Children.Select(c => ConditionText(c, title))) + "）";
        }
        public static string Status(QuestStatus value)
        {
            switch (value) { case QuestStatus.Waiting: return "待接取"; case QuestStatus.Active: return "进行中";
                case QuestStatus.Completed: return "已完成"; case QuestStatus.Failed: return "失败"; default: return "已取消"; }
        }
    }
}
