using System;
using DemonViglu.Dialogue.Unity;
using UnityEditor;
using UnityEngine;

namespace DemonViglu.Dialogue.Editor
{
    [CustomEditor(typeof(DialogueAsset))]
    public sealed class DialogueAssetEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            var lines = serializedObject.FindProperty("Lines");
            EditorGUILayout.HelpBox("从第一句开始。没有选项时点击继续；有选项时等待选择。跳转设为结束对话，仍会等玩家确认当前台词。角色留空表示旁白。", MessageType.Info);
            for (int i = 0; i < lines.arraySize; i++)
            {
                var line = lines.GetArrayElementAtIndex(i);
                EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                EditorGUILayout.BeginHorizontal();
                line.isExpanded = EditorGUILayout.Foldout(line.isExpanded, $"第 {i + 1} 句{(i == 0 ? " · 开场" : "")}", true);
                using (new EditorGUI.DisabledScope(i == 0))
                    if (GUILayout.Button("上移", GUILayout.Width(44))) { lines.MoveArrayElement(i, i - 1); GUIUtility.keyboardControl = 0; }
                if (GUILayout.Button("删除", GUILayout.Width(44)))
                {
                    lines.DeleteArrayElementAtIndex(i);
                    EditorGUILayout.EndHorizontal(); EditorGUILayout.EndVertical(); break;
                }
                EditorGUILayout.EndHorizontal();
                if (line.isExpanded)
                {
                    EditorGUILayout.PropertyField(line.FindPropertyRelative("Speaker"), new GUIContent("角色"));
                    EditorGUILayout.PropertyField(line.FindPropertyRelative("Text"), new GUIContent("台词"));
                    var options = line.FindPropertyRelative("Options");
                    if (options.arraySize == 0) TargetPopup(line.FindPropertyRelative("Next"), lines, "继续后前往");
                    else EditorGUILayout.LabelField("玩家选项", EditorStyles.boldLabel);
                    for (int j = 0; j < options.arraySize; j++)
                    {
                        var option = options.GetArrayElementAtIndex(j);
                        EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                        EditorGUILayout.PropertyField(option.FindPropertyRelative("Text"), new GUIContent($"选项 {j + 1}"));
                        TargetPopup(option.FindPropertyRelative("Next"), lines, "选择后前往");
                        EditorGUILayout.PropertyField(option.FindPropertyRelative("Condition"), new GUIContent("宿主条件（可选）"));
                        EditorGUILayout.PropertyField(option.FindPropertyRelative("Command"), new GUIContent("宿主命令（可选）"));
                        bool remove = GUILayout.Button("删除此选项");
                        EditorGUILayout.EndVertical();
                        if (remove) { options.DeleteArrayElementAtIndex(j); break; }
                    }
                    if (GUILayout.Button("添加选项"))
                    {
                        int index = options.arraySize++; var option = options.GetArrayElementAtIndex(index);
                        option.FindPropertyRelative("Text").stringValue = "新选项";
                        option.FindPropertyRelative("Next").stringValue = "";
                        option.FindPropertyRelative("Condition").stringValue = "";
                        option.FindPropertyRelative("Command").stringValue = "";
                    }
                }
                EditorGUILayout.EndVertical();
            }
            if (GUILayout.Button("添加一句台词"))
            {
                int i = lines.arraySize++; var line = lines.GetArrayElementAtIndex(i);
                string id = Guid.NewGuid().ToString("N");
                line.FindPropertyRelative("Id").stringValue = id;
                line.FindPropertyRelative("Speaker").objectReferenceValue = null;
                line.FindPropertyRelative("Text").stringValue = "请输入台词";
                line.FindPropertyRelative("Next").stringValue = "";
                line.FindPropertyRelative("Options").ClearArray(); line.isExpanded = true;
                if (i > 0)
                {
                    var previous = lines.GetArrayElementAtIndex(i - 1);
                    if (previous.FindPropertyRelative("Options").arraySize == 0 && string.IsNullOrEmpty(previous.FindPropertyRelative("Next").stringValue))
                        previous.FindPropertyRelative("Next").stringValue = id;
                }
            }
            serializedObject.ApplyModifiedProperties();
            try { ((DialogueAsset)target).BuildGraph(); }
            catch (Exception error) { EditorGUILayout.HelpBox(error.Message, MessageType.Error); }
        }
        private static void TargetPopup(SerializedProperty property, SerializedProperty lines, string label)
        {
            var labels = new System.Collections.Generic.List<string> { "结束对话" };
            var ids = new System.Collections.Generic.List<string> { "" }; int selected = 0;
            for (int i = 0; i < lines.arraySize; i++)
            {
                var line = lines.GetArrayElementAtIndex(i);
                string text = line.FindPropertyRelative("Text").stringValue.Replace('\n', ' ');
                labels.Add($"第 {i + 1} 句：{(text.Length > 28 ? text.Substring(0, 28) + "…" : text)}");
                ids.Add(line.FindPropertyRelative("Id").stringValue);
                if (ids[i + 1] == property.stringValue) selected = i + 1;
            }
            if (selected == 0 && !string.IsNullOrEmpty(property.stringValue))
            { selected = labels.Count; labels.Add("目标已删除，请重新选择"); ids.Add(property.stringValue); }
            EditorGUI.BeginChangeCheck();
            int next = EditorGUILayout.Popup(label, selected, labels.ToArray());
            if (EditorGUI.EndChangeCheck()) property.stringValue = ids[next];
        }
    }
}
