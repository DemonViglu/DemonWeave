using System;
using System.Collections.Generic;
using DemonWeave.Unity;
using UnityEditor;
using UnityEditor.IMGUI.Controls;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace DemonWeave.Editor
{
    internal sealed class QuestScenePicker : PopupWindowContent
    {
        private readonly Scene scene;
        private readonly bool entitiesOnly;
        private readonly Action<GameObject> choose;
        private readonly HashSet<Transform> expanded = new HashSet<Transform>();
        private readonly HashSet<Transform> visible = new HashSet<Transform>();
        private readonly SearchField search = new SearchField();
        private string query = "";
        private Vector2 scroll;

        internal QuestScenePicker(Scene scene, bool entitiesOnly, GameObject current, Action<GameObject> choose)
        {
            this.scene = scene;
            this.entitiesOnly = entitiesOnly;
            this.choose = choose;
            foreach (var root in scene.GetRootGameObjects())
            {
                expanded.Add(root.transform);
                if (!entitiesOnly) continue;
                foreach (var entity in root.GetComponentsInChildren<QuestEntity>(true))
                    for (var t = entity.transform; t && visible.Add(t); t = t.parent) { }
            }
            for (var t = current ? current.transform : null; t; t = t.parent) expanded.Add(t);
        }

        public override Vector2 GetWindowSize() => new Vector2(430, 360);
        public override void OnOpen() => search.SetFocus();
        public override void OnGUI(Rect rect)
        {
            if (!scene.isLoaded) { editorWindow.Close(); return; }
            GUILayout.Label(scene.name + (entitiesOnly ? "  /  Quest Entities" : "  /  Scene Objects"), EditorStyles.boldLabel);
            query = search.OnGUI(query);
            scroll = EditorGUILayout.BeginScrollView(scroll);
            bool found = false;
            foreach (var root in scene.GetRootGameObjects()) found |= Draw(root.transform, root.name, 0);
            if (!found) EditorGUILayout.LabelField("No matching objects in this scene.", EditorStyles.centeredGreyMiniLabel);
            EditorGUILayout.EndScrollView();
            if (GUILayout.Button("Clear reference")) Select(null);
        }

        private bool Draw(Transform item, string path, int depth)
        {
            if (entitiesOnly && !visible.Contains(item)) return false;
            bool searching = !string.IsNullOrWhiteSpace(query);
            bool selectable = !entitiesOnly || item.GetComponent<QuestEntity>();
            bool show = !searching || selectable && path.IndexOf(query.Trim(), StringComparison.OrdinalIgnoreCase) >= 0;
            if (show)
            {
                EditorGUILayout.BeginHorizontal();
                if (!searching)
                {
                    GUILayout.Space(12 * depth);
                    bool hasChildren = false;
                    foreach (Transform child in item)
                        if (!entitiesOnly || visible.Contains(child)) { hasChildren = true; break; }
                    if (hasChildren)
                    {
                        bool open = EditorGUI.Foldout(GUILayoutUtility.GetRect(14, 18, GUILayout.Width(14)), expanded.Contains(item), GUIContent.none);
                        if (open) expanded.Add(item); else expanded.Remove(item);
                    }
                    else GUILayout.Space(18);
                }
                using (new EditorGUI.DisabledScope(!selectable))
                    if (GUILayout.Button(searching ? path : item.name, EditorStyles.label)) Select(item.gameObject);
                EditorGUILayout.EndHorizontal();
            }
            if (searching || expanded.Contains(item))
                foreach (Transform child in item) show |= Draw(child, path + "/" + child.name, depth + 1);
            return show;
        }

        private void Select(GameObject selected)
        {
            editorWindow.Close();
            choose(selected);
            GUIUtility.ExitGUI();
        }
    }
}
