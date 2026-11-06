using System;
using System.Linq;
using DemonWeave.Unity;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;
using DemonWeave.Quests;

namespace DemonWeave.Editor
{
    public sealed partial class QuestEditorWindow
    {
        private void BuildWelcome()
        {
            var page = new VisualElement { style = { maxWidth = 620, flexGrow = 1, paddingTop = 60,
                paddingLeft = 32, paddingRight = 32, alignSelf = Align.Center, width = Length.Percent(100) } };
            rootVisualElement.Add(page);
            page.Add(new Label("Create your first quest") { style = { fontSize = 26, marginBottom = 16 } });
            page.Add(new Label("Start with a Quest Collection asset. It stores your tasks and their nodes.") {
                style = { whiteSpace = WhiteSpace.Normal, marginBottom = 20 } });
            var create = new Button(CreateCollectionAsset) { text = "Create Quest Collection…", name = "create-collection" };
            create.style.height = 36; create.SetEnabled(!EditorApplication.isPlaying); page.Add(create);
            var open = new Button(OpenCollectionAsset) { text = "Open Existing Collection…", name = "open-collection" };
            open.style.height = 32; page.Add(open);
            page.Add(new Label("You can also double-click a Quest Collection in the Project window.") {
                style = { whiteSpace = WhiteSpace.Normal, marginTop = 14 } });
            var assets = AssetDatabase.FindAssets("t:QuestBookAsset");
            if (assets.Length == 0) return;
            page.Add(Heading("Collections in this project"));
            var list = new ScrollView { style = { flexGrow = 1 } }; page.Add(list);
            foreach (var guid in assets)
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                var asset = AssetDatabase.LoadAssetAtPath<QuestBookAsset>(path);
                if (!asset) continue;
                list.Add(new Button(() => Open(asset)) { text = asset.name, tooltip = path });
            }
        }

        private void CreateCollectionAsset()
        {
            if (EditorApplication.isPlaying) return;
            var path = EditorUtility.SaveFilePanelInProject("Create Quest Collection", "QuestCollection", "asset", "Choose where to save your task definitions.");
            if (string.IsNullOrEmpty(path)) return;
            if (AssetDatabase.LoadMainAssetAtPath(path)) { EditorUtility.DisplayDialog("Collection already exists", "Open the existing asset or choose a new filename.", "OK"); return; }
            var asset = CreateInstance<QuestBookAsset>();
            asset.Id = Guid.NewGuid().ToString("N");
            asset.Title = System.IO.Path.GetFileNameWithoutExtension(path);
            AssetDatabase.CreateAsset(asset, path); AssetDatabase.SaveAssets();
            Selection.activeObject = asset; Open(asset);
        }

        private void OpenCollectionAsset()
        {
            var path = EditorUtility.OpenFilePanel("Open Quest Collection", Application.dataPath, "asset");
            if (string.IsNullOrEmpty(path)) return;
            var asset = AssetDatabase.LoadAssetAtPath<QuestBookAsset>(FileUtil.GetProjectRelativePath(path));
            if (asset) Open(asset);
            else EditorUtility.DisplayDialog("Cannot open collection", "Choose a Quest Collection asset inside this Unity project.", "OK");
        }

        private Label connectionStatus;
        private void BuildSceneConnection()
        {
            var panel = new VisualElement { style = { paddingLeft = 12, paddingRight = 12, paddingTop = 4, paddingBottom = 4 } };
            rootVisualElement.Add(panel);
            var runner = new ObjectField("Scene runner") { objectType = typeof(QuestSceneHost), allowSceneObjects = true, value = hostObject,
                tooltip = "Add Component → DemonWeave → Quest Runner on a scene object. Assign this Quest Collection in its Inspector." };
            runner.style.maxWidth = 520;
            runner.RegisterValueChangedCallback(e => {
                var candidate = e.newValue as QuestSceneHost;
                if (candidate && candidate.BookAsset != book) {
                    EditorUtility.DisplayDialog("Different Quest Collection", "Assign this collection in the runner Inspector first.", "OK");
                    runner.SetValueWithoutNotify(e.previousValue); return;
                }
                hostObject = candidate; CreateGUI();
            });
            panel.Add(runner);
            connectionStatus = new Label { style = { whiteSpace = WhiteSpace.Normal, color = new Color(1, .7f, .35f) } };
            panel.Add(connectionStatus);
            UpdateConnectionStatus();
        }
        private void UpdateConnectionStatus()
        {
            if (connectionStatus == null) return;
            connectionStatus.text = Host == null
                ? "No scene runner. Add Quest Runner to a scene object and assign this collection in its Inspector."
                : Host.BookAsset != book ? "Runner collection changed. Select a runner assigned to this collection." : "";
            connectionStatus.style.display = string.IsNullOrEmpty(connectionStatus.text) ? DisplayStyle.None : DisplayStyle.Flex;
        }

        private VisualElement breadcrumbs;
        private void OpenQuest(string id)
        {
            selectedStageId = null; selectedId = id; showStages = true;
            detailSection = "Overview"; Rebuild();
        }
        private void OpenCollection()
        {
            showStages = false; selectedStageId = null; Rebuild();
        }
        private void UpdateBreadcrumbs()
        {
            if (breadcrumbs == null) return;
            breadcrumbs.Clear();
            var quest = book?.Quests.FirstOrDefault(q => q.Id == selectedId);
            if (showStages && quest != null)
            {
                breadcrumbs.Add(new Button(OpenCollection) { text = "← Quests", tooltip = "View the task chain" });
                breadcrumbs.Add(new Label(quest.Title) { tooltip = book ? AssetDatabase.GetAssetPath(book) : "" });
            }
            else breadcrumbs.Add(new Label(book ? (string.IsNullOrEmpty(book.Title) ? book.name : book.Title) : "Quests"));
        }
    }
}
