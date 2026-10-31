using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace DemonWeave.Editor
{
    // Editor-only catalog selection; selecting an entry invokes the existing author operation.
    internal sealed class QuestCatalogPicker : EditorWindow
    {
        internal sealed class Entry
        {
            public string Id, Label;
            public Action Choose;
        }
        internal static void Open(string title, IEnumerable<Entry> entries, Func<bool> canApply)
        {
            var window = CreateInstance<QuestCatalogPicker>();
            window.titleContent = new GUIContent(title);
            window.minSize = new Vector2(440, 280);
            window.position = new Rect(180, 180, 520, 400);
            var items = entries.OrderBy(e => e.Label).ToArray();
            var root = window.rootVisualElement;
            root.style.paddingLeft = root.style.paddingRight = 10;
            root.style.paddingTop = root.style.paddingBottom = 10;
            var search = new TextField("Search") { name = "catalog-search" }; root.Add(search);
            var results = new ScrollView { style = { flexGrow = 1 } }; root.Add(results);
            void Refresh()
            {
                results.Clear();
                var query = search.value ?? "";
                foreach (var item in items.Where(e => (e.Label + " " + e.Id).IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0))
                {
                    var button = new Button(() => {
                        if (!canApply()) { window.Close(); return; }
                        window.Close(); item.Choose();
                    }) { text = item.Label + "\n" + item.Id, tooltip = item.Id, name = "catalog-entry-" + item.Id };
                    button.style.height = 48; button.style.unityTextAlign = TextAnchor.MiddleLeft;
                    results.Add(button);
                }
                if (results.childCount == 0) results.Add(new Label(items.Length == 0 ? "No registered entries." : "No matches."));
            }
            search.RegisterValueChangedCallback(_ => Refresh());
            root.RegisterCallback<KeyDownEvent>(e => { if (e.keyCode == KeyCode.Escape) { window.Close(); e.StopPropagation(); } });
            Refresh(); window.ShowUtility(); search.Focus();
        }
    }

    public sealed partial class QuestEditorWindow
    {
        private void PickCatalog(string title, IEnumerable<QuestCatalogPicker.Entry> entries)
        {
            var source = book; var document = book.ExportDefinitions(); var sourceHost = hostObject;
            QuestCatalogPicker.Open(title, entries, () => {
                if (!this || !source || EditorApplication.isPlaying) return false;
                if (book == source && hostObject == sourceHost && source.ExportDefinitions() == document) return true;
                notice = "Content or Scene Host changed. Open the selection again."; UpdateStatus(); return false;
            });
        }
    }
}
