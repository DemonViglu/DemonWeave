using System;
using System.Collections.Generic;
using System.Linq;
using DemonWeave.Quests;
using DemonWeave.Unity;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace DemonWeave.Editor
{
    public sealed partial class QuestEditorWindow : EditorWindow
    {
        private MonoBehaviour hostObject;
        private IQuestAuthoringHost Host => hostObject ? hostObject as IQuestAuthoringHost : null;
        [SerializeField] private QuestBookAsset book;
        private string selectedStageId;
        private IVisualElementScheduledItem statusSchedule;
        private string selectedId;
        private VisualElement body;
        private ScrollView inspector;
        private VisualElement inspectorHeader;
        private Label diagnostics, live;
        private QuestRelationGraph graph;
        private QuestStageGraph stageGraph;
        private string displayedStageQuestId;
        private string notice = "";
        private string lastDocument;
        private bool showStages;
        [MenuItem("DemonWeave/Quest Editor")]
        public static void Open()
        {
            var window = GetWindow<QuestEditorWindow>("DemonWeave");
            window.minSize = new Vector2(1080, 720);
            if (window.position.width < 1200) window.position = new Rect(50, 60, 1500, 850);
            window.Show();
        }
        public static void Open(QuestBookAsset asset, MonoBehaviour host = null)
        {
            if (!asset) return;
            var window = GetWindow<QuestEditorWindow>("DemonWeave");
            window.book = asset; window.hostObject = host; window.selectedId = null; window.selectedStageId = null; window.detailSection = "Overview"; window.graph = null;
            window.showStages = false;
            window.minSize = new Vector2(1080, 700); window.CreateGUI(); window.Show(); window.Focus();
        }
        private void OnEnable() { Undo.undoRedoPerformed += Rebuild; EditorApplication.playModeStateChanged += OnPlay; }
        private void OnDisable() { Undo.undoRedoPerformed -= Rebuild; EditorApplication.playModeStateChanged -= OnPlay; }
        private void OnFocus()
        {
            if (body == null || !book) return;
            var previousHost = hostObject;
            LocateHost();
            try { if (previousHost != hostObject || lastDocument != book.ExportDefinitions()) Rebuild(); } catch { Rebuild(); }
        }
        private void OnPlay(PlayModeStateChange state) { LocateHost(); Rebuild(); }
        private void LocateHost()
        {
            if (!hostObject) hostObject = UnityEngine.Object.FindObjectsByType<MonoBehaviour>().FirstOrDefault(x => x is IQuestAuthoringHost h && (!book || h.BookAsset == book));
            if (!book && Host != null) book = Host.BookAsset;
        }
        public void CreateGUI()
        {
            LocateHost(); rootVisualElement.Clear();
            rootVisualElement.AddToClassList("weave-window"); QuestEditorTheme.Apply(rootVisualElement);
            statusSchedule?.Pause();
            rootVisualElement.style.backgroundColor = new Color(.075f, .09f, .12f);
            rootVisualElement.style.fontSize = 13;
            var header = new VisualElement { style = { flexDirection = FlexDirection.Row, alignItems = Align.Center, paddingLeft = 12, height = 36, backgroundColor = new Color(.10f, .12f, .16f) } };
            breadcrumbs = header;
            rootVisualElement.Add(header);
            var toolbar = new Toolbar();
            if (Host == null && book)
            {
                var attach = new ToolbarButton(AttachToScene) { text = "Use in Scene", tooltip = "Create the scene runtime for this collection. No sample Scenario is required." };
                attach.SetEnabled(!EditorApplication.isPlaying); toolbar.Add(attach);
            }
            var context = new Foldout { text = "Preview Context", value = false };
            toolbar.style.height = 32; toolbar.style.paddingLeft = 12;
            var host = new ObjectField("Scene Host") { objectType = typeof(MonoBehaviour), allowSceneObjects = true, value = hostObject };
            host.style.width = 300;
            host.RegisterValueChangedCallback(e => {
                var value = e.newValue as MonoBehaviour;
                if (value && !(value is IQuestAuthoringHost)) { notice = "Component does not implement the Quest authoring interface."; host.SetValueWithoutNotify(hostObject); return; }
                hostObject = value; if (!book && Host != null) book = Host.BookAsset; Rebuild();
            }); context.Add(host);
            var asset = new ObjectField("Quest Collection") { objectType = typeof(QuestBookAsset), allowSceneObjects = false, value = book }; asset.style.width = 310;
            asset.RegisterValueChangedCallback(e => { book = e.newValue as QuestBookAsset; hostObject = null; LocateHost(); selectedId = null; Rebuild(); }); context.Add(asset);
            var technicalToggle = new ToolbarToggle { text = "Advanced", value = technicalView };
            technicalToggle.RegisterValueChangedCallback(e => { technicalView = e.newValue; Rebuild(); }); toolbar.Add(technicalToggle);
            toolbar.Add(new ToolbarButton(() => { if (stageGraph != null) stageGraph.FrameAll(); else graph?.FrameAll(); }) { text = "Frame All" });
            toolbar.Add(new ToolbarButton(() => QuestProposalWindow.Open(book, hostObject)) { text = "AI Changes" });
            var tables = new ToolbarMenu { text = "Tables" };
            tables.menu.AppendAction("Export CSV", _ => {
                if (!book || EditorApplication.isPlaying) return;
                var directory = EditorUtility.OpenFolderPanel("Export Quest Tables", "", ""); if (string.IsNullOrEmpty(directory)) return;
                try { QuestTables.Export(book,directory); notice="CSV records exported."; UpdateStatus(); }
                catch(Exception error) { notice=error.Message; UpdateStatus(); }
            });
            tables.menu.AppendAction("Import CSV...", _ => {
                if (!book || EditorApplication.isPlaying) return;
                var directory = EditorUtility.OpenFolderPanel("Import Quest Tables", "", ""); if (string.IsNullOrEmpty(directory)) return;
                try { QuestTableWindow.Review(book,Host,directory); }
                catch(Exception error) { notice=error.Message; UpdateStatus(); }
            }); toolbar.Add(tables);
            toolbar.Add(new ToolbarButton(() => {
                if (!book || EditorApplication.isPlaying) return;
                book.OnBeforeSerialize(); EditorUtility.SetDirty(book); AssetDatabase.SaveAssets();
                bool sceneSaved = !hostObject || !hostObject.gameObject.scene.IsValid() || EditorSceneManager.SaveScene(hostObject.gameObject.scene);
                notice = sceneSaved ? "Quest and scene bindings saved." : "Quest saved; scene bindings were not saved."; UpdateStatus();
            }) { text = "Save" });
            rootVisualElement.Add(toolbar);
            rootVisualElement.Add(context);
            toolbar.Add(new ToolbarButton(() => EditorUtility.DisplayDialog("Edit Quest", "Select a node to edit its properties. Connect ports to add prerequisites. Custom node types are ScriptableObject templates.", "Close")) { text = "Help" });
            body = new VisualElement { style = { flexGrow = 1, flexDirection = FlexDirection.Row } }; rootVisualElement.Add(body);
            diagnostics = new Label { style = { whiteSpace = WhiteSpace.Normal, paddingLeft = 12, paddingRight = 12, paddingTop = 8, paddingBottom = 8, maxHeight = 100 } };
            rootVisualElement.Add(diagnostics); Rebuild();
            statusSchedule = rootVisualElement.schedule.Execute(UpdateStatus).Every(350);
        }
        private void Rebuild()
        {
            if (body == null) return;
            var oldPositions = graph?.CapturePositions();
            var oldPan = graph?.viewTransform.position ?? Vector3.zero;
            var oldZoom = graph?.viewTransform.scale ?? Vector3.one;
            var oldScroll = inspector?.scrollOffset ?? Vector2.zero;
            var nodePositions = displayedStageQuestId == selectedId ? stageGraph?.CapturePositions() : null;
            var nodePan = stageGraph?.viewTransform.position ?? Vector3.zero;
            var nodeZoom = stageGraph?.viewTransform.scale ?? Vector3.one;
            body.Clear(); graph = null; stageGraph = null; live = null;
            if (!book) { body.Add(new HelpBox("Open a Quest Collection asset from the Project window.", HelpBoxMessageType.Info)); return; }
            try
            {
                lastDocument = book.ExportDefinitions();
                LoadAuthoringProfile();
                if (selectedId == null || !book.Quests.Any(q => q.Id == selectedId)) selectedId = book.Quests.FirstOrDefault()?.Id;
                UpdateBreadcrumbs();
                body.Add(TaskList());
                var split = new TwoPaneSplitView(1, 400, TwoPaneSplitViewOrientation.Horizontal) { style = { flexGrow = 1 } };
                var graphArea = new VisualElement { style = { flexGrow = 1, minWidth = 250 } }; split.Add(graphArea);
                if (Host != null && Host.BookAsset != book)
                {
                    var binding = new VisualElement { style = { flexDirection = FlexDirection.Row, alignItems = Align.Center } };
                    binding.Add(new Label("This collection is not used by the Scene Host.") { style = { flexGrow = 1, whiteSpace = WhiteSpace.Normal } });
                    var assign = new Button(AssignCollectionToHost) { text = "Use Collection in Scene", name = "assign-collection" };
                    assign.SetEnabled(!EditorApplication.isPlaying); binding.Add(assign); graphArea.Add(binding);
                }
                if (technicalView && Host is IQuestRecoveryAuthoring recovery)
                {
                    var recoveryButton = new Button(() => { detailSection = "Recovery"; BuildInspector(); }) { text = "Recovery  ·  " + recovery.RecoveryTrigger + "  > Inspect", tooltip = recovery.RecoveryOutcome };
                    recoveryButton.style.height = 40; recoveryButton.style.color = new Color(1,.78f,.42f); recoveryButton.style.backgroundColor = new Color(.18f,.13f,.12f); graphArea.Add(recoveryButton);
                }
                var detailPane = new VisualElement { style = { minWidth = 320, flexGrow = 1 } };
                inspectorHeader = new VisualElement { style = { paddingLeft = 14, paddingRight = 14, backgroundColor = new Color(.10f,.13f,.18f) } }; detailPane.Add(inspectorHeader);
                inspector = new ScrollView { style = { flexGrow = 1, paddingLeft = 14, paddingRight = 14, paddingBottom = 20 } }; detailPane.Add(inspector); split.Add(detailPane); body.Add(split);
                if (book.Quests.Count == 0)
                {
                    graphArea.Add(Heading("No quests yet"));
                    graphArea.Add(new Button(AddQuest) { text = "Create First Quest" });
                }
                else if (book.Quests.Any(q => q == null || string.IsNullOrWhiteSpace(q.Id)) || book.Quests.Select(q => q.Id).Distinct().Count() != book.Quests.Count)
                    graphArea.Add(new HelpBox("Missing or duplicate Quest IDs. Fix the definition to display dependencies.", HelpBoxMessageType.Error));
                else
                {
                    var selected = book.Quests.FirstOrDefault(q => q.Id == selectedId);
                    if (showStages && selected != null)
                    {
                        if (selected.Stages.Count == 0)
                        {
                            var empty = new VisualElement { style = { paddingLeft = 32, paddingTop = 40 } };
                            empty.Add(Heading("Empty Quest"));
                            empty.Add(new Button(() => AddStage(selected, null)) { text = "Add Node", name = "add-first-node" });
                            empty.Add(new Button(() => ShowStageMenu(selected)) { text = "From Template…" });
                            graphArea.Add(empty);
                        }
                        else {
                        stageGraph = new QuestStageGraph(QuestBookAsset.Compile(selected, false), stageId => {
                            selectedStageId = stageId; detailSection = "Objective"; BuildInspector();
                            inspector.schedule.Execute(() => { var field = inspector.Q<VisualElement>("stage-" + stageId); if (field != null) inspector.ScrollTo(field); }).ExecuteLater(1);
                        }, stage => {
                            var producer = authorProfile?.ProducerFor(Host, stage.OpenWhen);
                            var sources = producer == null ? Array.Empty<QuestStage>() : QuestBookAsset.Compile(selected, false).Stages.Where(s => s.Id != stage.Id && s.BeginExec.Concat(s.FinishExec).Any(e => authorProfile.Produces(Host, e, stage.OpenWhen))).ToArray();
                            return sources.Length == 1 ? sources[0] : null;
                        }, AuthorCondition, ids => DeleteStages(selected, ids), (from, to) => Edit("Remove Node Dependency", () => selected.Stages.Single(s => s.Id == to).After.Remove(from)), (from, to) => ConnectStages(selected, from, to), () => AddStage(selected, null), nodePositions == null, () => ShowStageMenu(selected));
                        graphArea.Add(stageGraph);
                        displayedStageQuestId = selectedId;
                        if (nodePositions != null) { stageGraph.RestorePositions(nodePositions); stageGraph.UpdateViewTransform(nodePan, nodeZoom); }
                        }
                    }
                    else
                    {
                    graph = new QuestRelationGraph(book, id => { selectedId = id; selectedStageId = null; inspector.scrollOffset = Vector2.zero; BuildInspector(); }, Rebuild, oldPositions == null, DeleteQuests, OpenQuest);
                    graphArea.Add(graph);
                    if (oldPositions != null) { graph.RestorePositions(oldPositions); graph.UpdateViewTransform(oldPan, oldZoom); }
                    }
                }
                BuildInspector(); UpdateStatus();
                inspector.schedule.Execute(() => inspector.scrollOffset = oldScroll).ExecuteLater(1);
            }
            catch (Exception e) { body.Add(new HelpBox(e.Message, HelpBoxMessageType.Error)); }
        }
        private void BuildInspector()
        {
            inspector.Clear(); inspectorHeader.Clear(); live = null;
            var quest = book.Quests.FirstOrDefault(q => q.Id == selectedId);
            if (quest == null) return;
            if (!technicalView) { BuildAuthorInspector(quest); return; }
            var stage = quest.Stages.FirstOrDefault(s => s.Id == selectedStageId);
            inspectorHeader.Add(Heading(stage == null ? quest.Title : stage.Title));
            if (stage != null) inspectorHeader.Add(new Button(() => { selectedStageId = null; detailSection = "Overview"; BuildInspector(); }) { text = "Back to Quest" });
            var sections = stage == null ? new[] { "Overview", "Activation", "Failure", "Recovery", "Runtime" } : new[] { "Objective", "Conditions", "Action", "Routes", "Recovery", "Runtime" };
            if (!sections.Contains(detailSection)) detailSection = sections[0];
            Tabs(inspectorHeader, sections, detailSection, value => { detailSection = value; inspector.scrollOffset = Vector2.zero; BuildInspector(); });
            var editable = new VisualElement(); editable.SetEnabled(!EditorApplication.isPlaying); inspector.Add(editable);
            if (detailSection == "Recovery") BuildRecovery(editable);
            else if (detailSection == "Runtime")
            { inspector.Add(new HelpBox("Inspect the running instance. Exit Play to edit content.", HelpBoxMessageType.Info)); live = new Label { style = { whiteSpace = WhiteSpace.Normal } }; inspector.Add(live); }
            else if (stage != null) BuildStages(editable, quest);
            else if (detailSection == "Overview")
            {
                Text(editable, "Quest Name", quest.Title, value => Edit("Rename Quest", () => quest.Title = value));
                BuildStages(editable, quest);
                if (quest.Stages.Count == 0) BuildContents(editable, quest, false);
                var advanced = new Foldout { text = "Ownership and Lifecycle", value = false }; editable.Add(advanced);
                QuestChoice(advanced, "Parent Quest", quest.ParentId, value => Edit("Change Parent", () => quest.ParentId = value), true, quest.Id);
                var cancel = new Toggle("Cancel children on exit") { value = quest.CancelChildrenOnEnd }; cancel.RegisterValueChangedCallback(e => Edit("Change Exit Policy", () => quest.CancelChildrenOnEnd = e.newValue)); advanced.Add(cancel);
                BuildExecs(advanced, quest.BeginExec, "On Quest Enter"); BuildExecs(advanced, quest.FinishExec, "On Quest Complete");
            }
            else if (detailSection == "Activation") BuildCondition(editable, quest.Accept, value => quest.Accept = value, 0);
            else if (detailSection == "Failure")
            {
                if (quest.Fail.Count == 0) editable.Add(new HelpBox("No quest failure conditions configured.", HelpBoxMessageType.Info));
                BuildContents(editable, quest, true); BuildExecs(editable, quest.FailExec, "On Quest Fail");
            }
            UpdateStatus();
        }
        private void BuildRecovery(VisualElement parent)
        {
            parent.Add(Heading("Recovery"));
            if (!(Host is IQuestRecoveryAuthoring recovery))
            { parent.Add(new HelpBox("No recovery provider is connected.", HelpBoxMessageType.Info)); return; }
            parent.Add(new Label("Trigger: " + recovery.RecoveryTrigger) { style = { whiteSpace = WhiteSpace.Normal, marginBottom = 16 } });
            var data = new SerializedObject(hostObject); var property = data.FindProperty(recovery.RecoveryProperty);
            if (property != null)
            {
                var lastValue = property.contentHash;
                var field = new PropertyField(property, "Policy"); parent.Add(field);
                field.RegisterCallback<SerializedPropertyChangeEvent>(_ => {
                    data.ApplyModifiedProperties();
                    var current = data.FindProperty(recovery.RecoveryProperty).contentHash;
                    if (current == lastValue) return;
                    lastValue = current; MarkHost();
                    rootVisualElement.schedule.Execute(Rebuild);
                });
                field.Bind(data);
            }
            parent.Add(new Label("Result: " + recovery.RecoveryOutcome) { style = { whiteSpace = WhiteSpace.Normal, marginTop = 16, marginBottom = 16 } });
            parent.Add(new HelpBox("Host recovery cancels the attempt and restores a checkpoint.", HelpBoxMessageType.Info));
        }
        private void MarkHost()
        {
            EditorUtility.SetDirty(hostObject); PrefabUtility.RecordPrefabInstancePropertyModifications(hostObject);
            EditorSceneManager.MarkSceneDirty(hostObject.gameObject.scene);
        }
        private void AssignCollectionToHost()
        {
            if (!book || Host == null || EditorApplication.isPlaying) return;
            Undo.RecordObject(hostObject, "Assign Quest Collection"); Host.BookAsset = book;
            MarkHost(); notice = "Scene Host now uses this collection. Save the scene bindings before running."; Rebuild();
        }
        private void AttachToScene()
        {
            if (!book || EditorApplication.isPlaying) return;
            var scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
            if (!scene.IsValid() || !scene.isLoaded) { notice = "Open a scene first."; UpdateStatus(); return; }
            var root = new GameObject("Quest Runtime — " + book.name);
            Undo.RegisterCreatedObjectUndo(root, "Use Quest in Scene");
            var host = Undo.AddComponent<QuestSceneHost>(root); host.BookAsset = book;
            hostObject = host; MarkHost(); CreateGUI();
        }
        private void Edit(string label, Action action)
        { QuestAuthoringChecks.Edit(book, label, _ => action()); notice = "Unsaved changes. The next Play session uses the new definition."; Rebuild(); }
        private void AddQuest()
        {
            if (!book || EditorApplication.isPlaying) return;
            var title = "New Quest"; int suffix = 2;
            while (book.Quests.Any(q => q.Title == title)) title = "New Quest " + suffix++;
            var quest = new QuestDefinition { Id = "quest-" + Guid.NewGuid().ToString("N"), Title = title };
            QuestAuthoringChecks.Edit(book, "Create Quest", quests => quests.Add(quest));
            notice = ""; OpenQuest(quest.Id);
        }
        private void RemoveQuest(QuestDefinition quest)
        {
            DeleteQuests(new[] { quest.Id });
        }
        private void UpdateStatus()
        {
            if (!book || diagnostics == null) return;
            var errors = QuestAuthoringChecks.Validate(book, Host);
            diagnostics.text = errors.Count == 0 ? notice
                : string.Join("\n", errors.Take(3)) + (errors.Count > 3 ? $"\n{errors.Count - 3} more issues." : "");
            diagnostics.style.display = string.IsNullOrWhiteSpace(diagnostics.text) ? DisplayStyle.None : DisplayStyle.Flex;
            var run = Host?.BookAsset == book ? Host.ActiveRun : null;
            graph?.UpdateStates(run);
            stageGraph?.UpdateStates(run?.Find(selectedId));
            if (!technicalView && live != null)
            {
                var instance = run?.Find(selectedId);
                if (Host == null) live.text = "No Scene Host connected.";
                else if (Host.BookAsset != book) live.text = "The Scene Host is running a different collection.";
                else if (QuestBookAsset.IsDraft(book.Quests.FirstOrDefault(q => q.Id == selectedId))) live.text = "Draft — not submitted to the runtime.";
                else if (instance == null) live.text = "No active instance. The attempt may be stopped, recovering, or waiting for retry. Check recent activity below.";
                else if (string.IsNullOrEmpty(selectedStageId))
                    live.text = QuestAuthoringChecks.Status(instance.State) + "\n" + string.Join("\n", run.Explain(selectedId)) + "\n"
                        + string.Join("\n", instance.Stages.Select(s => s.Title + ": " + QuestAuthoringChecks.Status(s.State)));
                else if (instance.Stage(selectedStageId) != null) live.text = string.Join("\n", run.ExplainStage(selectedId, selectedStageId));
                else live.text = "This node is not in the running definition snapshot.";
                if (Host != null && Host.BookAsset == book && Host.Activity.Count > 0)
                    live.text += "\n\nRecent activity\n" + string.Join("\n", Host.Activity.Skip(Math.Max(0, Host.Activity.Count - 5)));
                return;
            }
            if (live != null)
                live.text = run?.Find(selectedId) == null ? "Enter Play to inspect this quest." : QuestAuthoringChecks.Status(run.Find(selectedId).State) + "\n"
                    + string.Join("\n", run.Explain(selectedId)) + "\n\nRecent actions:\n" + string.Join("\n", Host.Activity.Skip(Math.Max(0, Host.Activity.Count - 8)));
            if (live != null && run?.Find(selectedId) != null)
                live.text += "\n\n" + string.Join("\n", run.Find(selectedId).Stages.SelectMany(s => run.ExplainStage(selectedId, s.Id)));
        }
        private static Label Heading(string title) => new Label(title) { style = { fontSize = 15, unityFontStyleAndWeight = FontStyle.Bold, marginTop = 18, marginBottom = 8 } };
        private static void Text(VisualElement parent, string label, string value, Action<string> changed)
        { var field = new TextField(label) { value = value, isDelayed = true }; field.RegisterValueChangedCallback(e => changed(e.newValue)); parent.Add(field); }
        private static void Choice(VisualElement parent, string label, List<string> values, string selected, Action<string> changed, Func<string, string> display = null)
        {
            if (!values.Contains(selected)) values.Insert(0, selected ?? "");
            var field = new PopupField<string>(label, values, Math.Max(0, values.IndexOf(selected)), display, display);
            field.RegisterValueChangedCallback(e => changed(e.newValue)); parent.Add(field);
        }
        private void QuestChoice(VisualElement parent, string label, string value, Action<string> changed, bool none = false, string exclude = null)
        {
            var ids = book.Quests.Where(q => q.Id != exclude).Select(q => q.Id).ToList(); if (none) ids.Insert(0, "");
            Choice(parent, label, ids, value, changed, id => string.IsNullOrEmpty(id) ? "None" : book.Quests.FirstOrDefault(q => q.Id == id)?.Title ?? "Missing: " + id);
        }
        private QuestStage SelectedNode => book?.Quests.FirstOrDefault(q => q.Id == selectedId)?.Stages.FirstOrDefault(n => n.Id == selectedStageId);
        private void RoleChoice(VisualElement parent, string label, string value, Action<string> changed, bool entityOnly = false)
        {
            var current = Host?.BindingEntries.FirstOrDefault(b => b.Role == value);
            var row = ItemRow(parent);
            var field = new ObjectField(label) { objectType = entityOnly ? typeof(QuestEntity) : typeof(GameObject), allowSceneObjects = true,
                value = entityOnly ? (UnityEngine.Object)current?.Entity : current?.Target, name = "target-" + label,
                tooltip = entityOnly ? "Choose the business component that inherits QuestEntity." : "An explicit scene-object argument. No identity component is required." };
            field.style.flexGrow = 1; field.SetEnabled(Host != null && !EditorApplication.isPlaying); row.Add(field);
            GameObject ObjectOf(UnityEngine.Object obj) => obj is Component c ? c.gameObject : obj as GameObject;
            IconAction(row, "◎", "Select in Hierarchy", () => { var go = ObjectOf(field.value); if(go) {Selection.activeGameObject = go; EditorGUIUtility.PingObject(go);} }).SetEnabled(current?.Target);
            if(entityOnly) IconAction(row,"{}","Open Entity implementation",()=>OpenImplementation(current.Entity.GetType())).SetEnabled(current?.Entity);
            field.RegisterValueChangedCallback(e => {
                var target = ObjectOf(e.newValue);
                var entity = target ? target.GetComponent<QuestEntity>() : null;
                if(target && (target.scene != hostObject.gameObject.scene || !target.scene.IsValid() || entityOnly && (!entity || entity.GetType() == typeof(QuestEntity))))
                {
                    notice = entityOnly ? "Choose a logic component inheriting QuestEntity in this scene." : "Choose an object in this scene.";
                    field.SetValueWithoutNotify(e.previousValue); UpdateStatus(); return;
                }
                Undo.IncrementCurrentGroup(); int group = Undo.GetCurrentGroup();
                if(!target) { changed(""); return; }
                if(entity && (string.IsNullOrEmpty(entity.Id) || UnityEngine.Object.FindObjectsByType<QuestEntity>(FindObjectsSortMode.None).Any(other=>other!=entity&&other.Id==entity.Id)))
                {
                    Undo.RecordObject(entity,"Assign Entity Identity");var data=new SerializedObject(entity);data.FindProperty("entityId").stringValue=Guid.NewGuid().ToString("N");data.ApplyModifiedProperties();PrefabUtility.RecordPrefabInstancePropertyModifications(entity);
                }
                var binding=Host.BindingEntries.FirstOrDefault(b=>b.Target==target);
                if(binding==null)
                {
                    Undo.RecordObject(hostObject,"Bind Quest Reference");binding=new EntityBinding{Role=entity?"entity:"+entity.Id:"object:"+Guid.NewGuid().ToString("N"),Entity=entity,Object=entity?null:target};Host.BindingEntries.Add(binding);MarkHost();
                }
                EditorSceneManager.MarkSceneDirty(target.scene);changed(binding.Role);Undo.CollapseUndoOperations(group);
            });
            if(current==null && !string.IsNullOrEmpty(value)) parent.Add(new HelpBox("Unresolved reference: "+value,HelpBoxMessageType.Warning));
        }
    }
}
