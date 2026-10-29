using System;
using System.Collections.Generic;
using System.Linq;
using DemonWeave.Editor;
using DemonWeave.Quests;
using DemonWeave.Unity;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace DemonWeave.Lighthouse.Editor
{
    // Deterministic protocol/example fixture. This is not a model response.
    public static class LighthouseProposalExample
    {
        public static List<QuestDefinition> BatteryOnly()
        {
            var definitions = LighthouseSetup.Definitions(); definitions.RemoveAll(q => q.Id == "generator");
            var choices = definitions.Single(q => q.Id == "light").Accept.Children.Single(c => c.Kind == ConditionKind.Any);
            choices.Children.RemoveAll(c => c.Subject == "generator"); return definitions;
        }
        public static string Create(QuestBookAsset book)
        {
            if (book.Quests.Any(q => q.Id == "generator")) throw new InvalidOperationException("此示例用于单路线任务集；当前已存在发电机路线。 ");
            var temporary = ScriptableObject.CreateInstance<QuestBookAsset>();
            try
            {
                temporary.Quests = LighthouseSetup.Definitions();
                var generator = JArray.Parse(temporary.ExportDefinitions()).OfType<JObject>().Single(q => (string)q["Id"] == "generator");
                var light = book.Quests.Single(q => q.Id == "light").Copy();
                bool replaced = false;
                QuestCondition AddAlternative(QuestCondition condition)
                {
                    if (condition.Kind == ConditionKind.QuestState && condition.Subject == "battery" && condition.State == QuestStatus.Completed)
                    {
                        replaced = true;
                        return new QuestCondition { Kind = ConditionKind.Any, Children = new List<QuestCondition> {
                            condition.Copy(), new QuestCondition { Kind = ConditionKind.QuestState, Subject = "generator" } } };
                    }
                    var copy = condition.Copy(); copy.Children = copy.Children.Select(AddAlternative).ToList(); return copy;
                }
                light.Accept = AddAlternative(light.Accept);
                if (!replaced) throw new InvalidOperationException("样本没有电池完成依赖，不能推测应该改哪个条件。 ");
                temporary.Quests = new List<QuestDefinition> { light };
                var accept = JArray.Parse(temporary.ExportDefinitions())[0]["Accept"].DeepClone();
                return JsonConvert.SerializeObject(new QuestProposal { Schema = 1, BookId = book.Id, BaseRevision = QuestProposalReview.Revision(book),
                    Summary = "离线协议示例（非模型）：新增维修发电机任务，并将电池供电条件扩为电池或发电机；保留实际亮灯与交付规则。",
                    Operations = new List<QuestPatchOperation> {
                        new QuestPatchOperation { Op = "add", QuestId = "generator", Value = generator },
                        new QuestPatchOperation { Op = "set", QuestId = "light", Field = "Accept", Value = accept }
                    } }, Formatting.Indented);
            }
            finally { UnityEngine.Object.DestroyImmediate(temporary); }
        }
        [MenuItem("DemonWeave/示例/切换电池单路线并审查离线维修提案")]
        public static void Open()
        {
            if (EditorApplication.isPlaying) { Debug.LogWarning("先退出 Play。"); return; }
            var host = UnityEngine.Object.FindAnyObjectByType<LighthouseScenario>();
            if (!host) { Debug.LogWarning("先打开灯塔样本。"); return; }
            const string path = "Assets/Lighthouse/Content/BatteryOnly.asset";
            var book = AssetDatabase.LoadAssetAtPath<QuestBookAsset>(path);
            if (!book)
            {
                book = ScriptableObject.CreateInstance<QuestBookAsset>(); book.Id = "before-storm-battery-only"; book.Title = "灯塔 · 单路线起点"; book.Quests = BatteryOnly();
                AssetDatabase.CreateAsset(book, path); AssetDatabase.SaveAssetIfDirty(book);
            }
            // Never reset an already edited sample. If it already has a repair route, preserve it.
            string proposal;
            try { proposal = Create(book); } catch (Exception e) { Debug.LogWarning(e.Message); return; }
            Undo.RecordObject(host, "切换单路线作者示例"); host.Book = book; EditorUtility.SetDirty(host);
            PrefabUtility.RecordPrefabInstancePropertyModifications(host); EditorSceneManager.MarkSceneDirty(host.gameObject.scene);
            QuestProposalWindow.Open(book, host, proposal);
        }
    }
}
