using DemonWeave.Unity;
using UnityEditor;
using UnityEditor.Callbacks;
using UnityEngine;

namespace DemonWeave.Editor
{
    [CustomEditor(typeof(QuestBookAsset))]
    public sealed class QuestBookInspector : UnityEditor.Editor
    {
        [OnOpenAsset(0)]
        private static bool OpenAsset(EntityId id, int line)
        {
            var book = EditorUtility.EntityIdToObject(id) as QuestBookAsset;
            if (!book) return false;
            QuestEditorWindow.Open(book); return true;
        }
        public override void OnInspectorGUI()
        {
            var book = (QuestBookAsset)target;
            EditorGUILayout.Space(10);
            EditorGUILayout.LabelField(string.IsNullOrEmpty(book.Title) ? book.name : book.Title, EditorStyles.boldLabel);
            EditorGUILayout.HelpBox("双击此资产编辑任务流程。场景只负责对象绑定，不是打开编辑器的前置条件。", MessageType.Info);
            if (GUILayout.Button("打开任务编辑器", GUILayout.Height(34))) QuestEditorWindow.Open(book);
            serializedObject.Update();
            EditorGUILayout.Space(10);
            EditorGUILayout.PropertyField(serializedObject.FindProperty("Title"), new GUIContent("标题"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("Id"), new GUIContent("稳定标识"));
            serializedObject.ApplyModifiedProperties();
        }
    }
}
