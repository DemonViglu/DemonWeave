using DemonWeave.Unity;
using UnityEditor;
using UnityEngine;

namespace DemonWeave.Editor
{
    [CustomEditor(typeof(QuestWorldState))]
    internal sealed class QuestWorldStateInspector : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            if (!Application.isPlaying) { DrawDefaultInspector(); return; }
            var state = QuestWorldState.Current;
            if (!state) { EditorGUILayout.HelpBox("No active World State.", MessageType.Info); return; }
            EditorGUILayout.LabelField("Live Values", EditorStyles.boldLabel);
            EditorGUILayout.LabelField("Play Mode only. Initial values are unchanged.", EditorStyles.wordWrappedMiniLabel);
            foreach (var fact in state.RuntimeFacts)
            {
                EditorGUI.BeginChangeCheck();
                var value = EditorGUILayout.Toggle(new GUIContent(state.LabelFor(fact.Key), fact.Key), fact.Value);
                if (EditorGUI.EndChangeCheck()) state.Set(fact.Key, value);
            }
        }
        public override bool RequiresConstantRepaint() => Application.isPlaying;
    }
}
