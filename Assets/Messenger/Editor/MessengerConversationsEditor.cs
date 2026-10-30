using System;
using System.Linq;
using DemonViglu.Dialogue.Unity;
using DemonWeave.Unity;
using UnityEditor;
using UnityEngine;

namespace DemonWeave.Messenger.Editor
{
    [CustomEditor(typeof(MessengerConversations))]
    public sealed class MessengerConversationsEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            if (EditorApplication.isPlaying) { EditorGUILayout.HelpBox("退出 Play 后修改对白配置。", MessageType.Info); return; }
            var content = (MessengerConversations)target;
            serializedObject.Update();
            EditorGUILayout.HelpBox("命令绑定现有交互或任务路线；对白选项引用命令。路线条件仍由 Quest 决定，不在这里重复编排。", MessageType.Info);
            EditorGUILayout.PropertyField(serializedObject.FindProperty("AuthoringBook"), new GUIContent("任务定义（作者引用）"));
            var commands = serializedObject.FindProperty("Commands");
            var host = UnityEngine.Object.FindAnyObjectByType<MessengerScenario>();
            var roles = host ? host.Bindings.Select(b => b.Role).ToArray() : Array.Empty<string>();
            for (int i = 0; i < commands.arraySize; i++)
            {
                var command = commands.GetArrayElementAtIndex(i);
                EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                EditorGUILayout.PropertyField(command.FindPropertyRelative("Id"), new GUIContent("稳定命令标识"));
                EditorGUILayout.PropertyField(command.FindPropertyRelative("Kind"), new GUIContent("操作"));
                if (command.FindPropertyRelative("Kind").enumValueIndex == (int)ConversationCommandKind.Interact)
                    Select(command.FindPropertyRelative("Role"), roles, roles, "交互对象角色");
                else if (command.FindPropertyRelative("Kind").enumValueIndex != (int)ConversationCommandKind.ChooseRoute)
                    EditorGUILayout.LabelField("提交真实接取/交接请求；不会直接写 Quest 完成状态。");
                else if (content.AuthoringBook)
                {
                    var quests = content.AuthoringBook.Quests;
                    Select(command.FindPropertyRelative("QuestId"), quests.Select(q => q.Id).ToArray(), quests.Select(q => q.Title).ToArray(), "任务");
                    var quest = quests.FirstOrDefault(q => q.Id == command.FindPropertyRelative("QuestId").stringValue);
                    if (quest != null)
                    {
                        Select(command.FindPropertyRelative("StageId"), quest.Stages.Select(s => s.Id).ToArray(), quest.Stages.Select(s => s.Title).ToArray(), "阶段");
                        var stage = quest.Stages.FirstOrDefault(s => s.Id == command.FindPropertyRelative("StageId").stringValue);
                        if (stage != null) Select(command.FindPropertyRelative("RouteId"), stage.Routes.Select(r => r.Id).ToArray(), stage.Routes.Select(r => r.Title).ToArray(), "剧情路线");
                    }
                }
                else EditorGUILayout.HelpBox("先选择任务定义，才能选择路线。", MessageType.Warning);
                if (GUILayout.Button("移除此绑定")) { commands.DeleteArrayElementAtIndex(i); EditorGUILayout.EndVertical(); break; }
                EditorGUILayout.EndVertical();
            }
            if (GUILayout.Button("添加命令绑定"))
            {
                var index = commands.arraySize++; var c = commands.GetArrayElementAtIndex(index);
                foreach (var name in new[] { "Role", "QuestId", "StageId", "RouteId" }) c.FindPropertyRelative(name).stringValue = "";
                c.FindPropertyRelative("Id").stringValue = "command-" + Guid.NewGuid().ToString("N").Substring(0, 8);
                c.FindPropertyRelative("Kind").enumValueIndex = 0;
            }
            EditorGUILayout.PropertyField(serializedObject.FindProperty("Conversations"), new GUIContent("角色对白资产"), true);
            EditorGUILayout.PropertyField(serializedObject.FindProperty("Conditions"), new GUIContent("世界事实条件（全部满足）"), true);
            serializedObject.ApplyModifiedProperties();
            foreach (var entry in content.Conversations.Where(e => e?.Conversation)) DrawConversation(content, entry.Conversation);
        }
        private static void DrawConversation(MessengerConversations content, DialogueAsset asset)
        {
            var data = new SerializedObject(asset); data.Update(); var lines = data.FindProperty("Lines");
            EditorGUILayout.Space(); EditorGUILayout.LabelField(asset.name, EditorStyles.boldLabel);
            if (GUILayout.Button("选择对白资产（增删台词/角色）")) Selection.activeObject = asset;
            var commandIds = new[] { "" }.Concat(content.Commands.Select(c => c.Id)).Distinct().ToArray();
            var conditionIds = commandIds.Concat(content.Conditions.Select(c => c.Id)).Distinct().ToArray();
            var lineIds = new[] { "" }.Concat(asset.Lines.Select(l => l.Id)).ToArray();
            var lineLabels = new[] { "结束对白" }.Concat(asset.Lines.Select((l, i) => (i + 1) + ". " + l.Text.Substring(0, Math.Min(30, l.Text.Length)))).ToArray();
            for (int i = 0; i < lines.arraySize; i++)
            {
                var line = lines.GetArrayElementAtIndex(i); line.isExpanded = EditorGUILayout.Foldout(line.isExpanded, "台词 " + (i + 1), true);
                if (!line.isExpanded) continue;
                EditorGUILayout.PropertyField(line.FindPropertyRelative("Speaker")); EditorGUILayout.PropertyField(line.FindPropertyRelative("Text"));
                var options = line.FindPropertyRelative("Options");
                if (options.arraySize == 0) Select(line.FindPropertyRelative("Next"), lineIds, lineLabels, "继续后前往");
                for (int j = 0; j < options.arraySize; j++)
                {
                    var option = options.GetArrayElementAtIndex(j); EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                    EditorGUILayout.PropertyField(option.FindPropertyRelative("Text"), new GUIContent("选项文字"));
                    Select(option.FindPropertyRelative("Next"), lineIds, lineLabels, "选择后前往");
                    Select(option.FindPropertyRelative("Command"), commandIds, commandIds.Select(x => x == "" ? "无操作" : x).ToArray(), "提交命令");
                    Select(option.FindPropertyRelative("Condition"), conditionIds, conditionIds.Select(x => x == "" ? "始终可用" : x).ToArray(), "可用条件");
                    EditorGUILayout.EndVertical();
                }
            }
            data.ApplyModifiedProperties();
        }
        private static void Select(SerializedProperty property, string[] ids, string[] labels, string title)
        {
            var values = ids.ToList(); var names = labels.ToList(); var index = values.IndexOf(property.stringValue);
            if (index < 0) { index = values.Count; values.Add(property.stringValue); names.Add("请选择 / 缺失：" + property.stringValue); }
            EditorGUI.BeginChangeCheck(); var selected = EditorGUILayout.Popup(title, index, names.ToArray());
            if (EditorGUI.EndChangeCheck()) property.stringValue = values[selected];
        }
        [MenuItem("DemonWeave/使者样本/编辑对白与路线绑定")]
        public static void Open()
        {
            var presentation = UnityEngine.Object.FindAnyObjectByType<MessengerPresentation>();
            if (presentation && presentation.Content) Selection.activeObject = presentation.Content;
            else Debug.LogWarning("先打开使者剧情体验场景。");
        }
    }
}
