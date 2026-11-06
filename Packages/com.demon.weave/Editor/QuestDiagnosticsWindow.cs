using System.Linq;
using DemonWeave.Unity;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace DemonWeave.Editor
{
    public sealed class QuestDiagnosticsWindow : EditorWindow
    {
        private QuestBookAsset book;
        private MonoBehaviour runner;
        private Label output;
        public static void Open(QuestBookAsset book, MonoBehaviour runner)
        {
            var window = GetWindow<QuestDiagnosticsWindow>("Quest Diagnostics");
            window.book = book; window.runner = runner; window.Show(); window.Refresh();
        }
        public void CreateGUI()
        {
            var scroll = new ScrollView { style = { flexGrow = 1 } };
            output = new Label { style = { whiteSpace = WhiteSpace.Normal, paddingLeft = 12, paddingTop = 12, paddingRight = 12 } };
            scroll.Add(output); rootVisualElement.Add(scroll);
            rootVisualElement.schedule.Execute(Refresh).Every(500);
        }
        private void Refresh()
        {
            if (output == null) return;
            if (!book) { output.text = "Open Diagnostics from a Quest Collection."; return; }
            var host = runner ? runner as IQuestAuthoringHost : null;
            var errors = QuestAuthoringChecks.Validate(book, host);
            output.text = book.name + "\n\n" + (errors.Count == 0 ? "No definition errors." : string.Join("\n\n", errors));
            if (host == null) output.text += "\n\nNo scene runner selected. Scene binding and runtime checks are unavailable.";
            else if (host.Activity.Count > 0) output.text += "\n\nRuntime activity\n" + string.Join("\n", host.Activity);
        }
    }
}
