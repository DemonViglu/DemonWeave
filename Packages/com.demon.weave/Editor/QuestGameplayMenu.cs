using DemonWeave.Unity;
using UnityEditor;
using UnityEngine;

namespace DemonWeave.Editor
{
    internal static class QuestGameplayMenu
    {
        [MenuItem("Assets/Create/DemonWeave/Gameplay Component", priority = 81)]
        private static void Create() => ProjectWindowUtil.CreateScriptAssetFromTemplateFile(
            "Packages/com.demon.weave/Editor/Templates/QuestGameplay.cs.txt", "QuestGameplay.cs");
    }

    [CustomEditor(typeof(QuestSceneHost))]
    internal sealed class QuestSceneHostInspector : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            var host = (QuestSceneHost)target;
            EditorGUILayout.LabelField("Quest Runtime", EditorStyles.boldLabel);
            serializedObject.Update();
            EditorGUILayout.PropertyField(serializedObject.FindProperty("book"), new GUIContent("Quest Collection"));
            serializedObject.ApplyModifiedProperties();
            if (!host.BookAsset) EditorGUILayout.HelpBox("Assign the Quest Collection this object should run.", MessageType.Warning);
            if (GUILayout.Button("Open Quest Editor")) QuestEditorWindow.Open(host.BookAsset, host);
            EditorGUILayout.HelpBox("Object references are maintained from the Quest Editor. No manual binding table is required.", MessageType.Info);
        }
    }
}
