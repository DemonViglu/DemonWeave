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
            EditorGUILayout.HelpBox("Double-click to edit this Quest Collection.", MessageType.Info);
            if (GUILayout.Button("Open Quest Editor", GUILayout.Height(34))) QuestEditorWindow.Open(book);
            serializedObject.Update();
            EditorGUILayout.Space(10);
            EditorGUILayout.PropertyField(serializedObject.FindProperty("Title"), new GUIContent("Title"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("Id"), new GUIContent("Stable ID"));
            serializedObject.ApplyModifiedProperties();
        }
    }
}
