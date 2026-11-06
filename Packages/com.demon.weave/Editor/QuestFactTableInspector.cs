using System;
using DemonWeave.Unity;
using UnityEditor;
using UnityEditorInternal;
using UnityEngine;
namespace DemonWeave.Editor
{
    [CustomEditor(typeof(QuestFactTable))]
    internal sealed class QuestFactTableInspector : UnityEditor.Editor
    {
        private ReorderableList list;
        private void OnEnable()
        {
            var entries = serializedObject.FindProperty("entries");
            list = new ReorderableList(serializedObject, entries, true, true, true, true);
            list.drawHeaderCallback = r => {
                EditorGUI.LabelField(new Rect(r.x, r.y, r.width - 90, r.height), "Name");
                EditorGUI.LabelField(new Rect(r.xMax - 90, r.y, 90, r.height), "Initial value");
            };
            list.elementHeight = EditorGUIUtility.singleLineHeight + 6;
            list.drawElementCallback = (r, index, active, focused) => {
                var entry = entries.GetArrayElementAtIndex(index); r.y += 2; r.height = EditorGUIUtility.singleLineHeight;
                var name = new Rect(r.x, r.y, Mathf.Max(30, r.width - 35), r.height);
                var label = entry.FindPropertyRelative("Label");
                label.stringValue = EditorGUI.DelayedTextField(name, label.stringValue);
                EditorGUI.PropertyField(new Rect(r.xMax - 25, r.y, 25, r.height), entry.FindPropertyRelative("InitialValue"), GUIContent.none);
            };
            list.onAddCallback = _ => {
                var index = entries.arraySize; entries.InsertArrayElementAtIndex(index);
                var entry = entries.GetArrayElementAtIndex(index);
                entry.FindPropertyRelative("id").stringValue = Guid.NewGuid().ToString("N");
                entry.FindPropertyRelative("Label").stringValue = "New fact";
                entry.FindPropertyRelative("InitialValue").boolValue = false;
            };
        }
        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            using (new EditorGUI.DisabledScope(Application.isPlaying)) list.DoLayoutList();
            if (serializedObject.ApplyModifiedProperties()) QuestFactCodeGenerator.Queue((QuestFactTable)target);
            var table = (QuestFactTable)target;
            using (new EditorGUI.DisabledScope(Application.isPlaying))
                if (GUILayout.Button(new GUIContent("Generate C#", "Generate named references. Later table edits update this file automatically.")))
                    try { QuestFactCodeGenerator.ChooseAndGenerate(table); } catch (Exception e) { Debug.LogError(e.Message, table); }
            var generationError = QuestFactCodeGenerator.Status(table);
            if (!string.IsNullOrEmpty(generationError)) EditorGUILayout.HelpBox(generationError, MessageType.Error);
            var error = ((QuestFactTable)target).CheckConfiguration();
            if (!string.IsNullOrEmpty(error)) EditorGUILayout.HelpBox(error, MessageType.Error);
        }
    }
}
