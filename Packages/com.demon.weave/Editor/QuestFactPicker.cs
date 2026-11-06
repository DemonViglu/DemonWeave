using System;
using System.Collections.Generic;
using System.Linq;
using DemonWeave.Unity;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
namespace DemonWeave.Editor
{
    internal static class QuestFactPicker
    {
        internal static List<(string id, string label)> Entries() => AssetDatabase.FindAssets("t:QuestFactTable")
            .Select(g => AssetDatabase.LoadAssetAtPath<QuestFactTable>(AssetDatabase.GUIDToAssetPath(g)))
            .Where(t => t).SelectMany(t => t.Entries.Where(f => f != null).Select(f => (f.Id, t.name + "/" + f.Label)))
            .OrderBy(f => f.Item2).ToList();
        internal static QuestFactTable Owner(string id)
        {
            if (string.IsNullOrEmpty(id)) return null;
            var tables = AssetDatabase.FindAssets("t:QuestFactTable")
                .Select(g => AssetDatabase.LoadAssetAtPath<QuestFactTable>(AssetDatabase.GUIDToAssetPath(g)))
                .Where(t => t && t.Entries.Any(f => f != null && f.Id == id)).ToArray();
            return tables.Length == 1 ? tables[0] : null;
        }
        internal static void Locate(string id)
        {
            var table = Owner(id);
            if (!table) return;
            Selection.activeObject = table;
            EditorGUIUtility.PingObject(table);
        }
        internal static GUIContent LocateIcon => new GUIContent(EditorGUIUtility.IconContent("d_Search Icon").image, "Select fact table in Inspector and locate it in Project");
        internal static VisualElement Field(string label, string selected, Action<string> changed)
        {
            var entries = Entries();
            var ids = entries.Select(e => e.id).Distinct().ToList(); ids.Insert(0, "");
            selected ??= ""; if (!ids.Contains(selected)) ids.Add(selected);
            string Display(string id) => string.IsNullOrEmpty(id) ? "None" : entries.FirstOrDefault(e => e.id == id).label ?? "Missing: " + id;
            var field = new UnityEngine.UIElements.PopupField<string>(label, ids, ids.IndexOf(selected), Display, Display);
            field.SetEnabled(!EditorApplication.isPlaying);
            var row = new VisualElement { style = { flexDirection = FlexDirection.Row } };
            field.style.flexGrow = 1; field.style.flexShrink = 1; field.style.minWidth = 0;
            row.Add(field);
            var locate = new Button(() => Locate(field.value)) { tooltip = LocateIcon.tooltip };
            locate.style.width = 24; locate.style.flexShrink = 0;
            locate.Add(new Image { image = LocateIcon.image, scaleMode = ScaleMode.ScaleToFit, style = { width = 14, height = 14 } });
            locate.SetEnabled(Owner(selected)); row.Add(locate);
            field.RegisterValueChangedCallback(e => { locate.SetEnabled(Owner(e.newValue)); changed(e.newValue); });
            return row;
        }
    }
    [CustomPropertyDrawer(typeof(QuestFactReference))]
    internal sealed class QuestFactReferenceDrawer : PropertyDrawer
    {
        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            EditorGUI.BeginProperty(position, label, property);
            var id = property.FindPropertyRelative("Id"); var entries = QuestFactPicker.Entries();
            entries.Insert(0, ("", "None"));
            var index = entries.FindIndex(e => e.id == id.stringValue);
            if (index < 0) { index = entries.Count; entries.Add((id.stringValue, "Missing: " + id.stringValue)); }
            EditorGUI.BeginChangeCheck();
            var next = EditorGUI.Popup(new Rect(position.x, position.y, position.width - 26, position.height), label.text, index, entries.Select(e => e.label).ToArray());
            if (EditorGUI.EndChangeCheck()) id.stringValue = entries[next].id;
            using (new EditorGUI.DisabledScope(!QuestFactPicker.Owner(id.stringValue)))
                if (GUI.Button(new Rect(position.xMax - 24, position.y, 24, position.height), QuestFactPicker.LocateIcon))
                    QuestFactPicker.Locate(id.stringValue);
            EditorGUI.EndProperty();
        }
    }
}

