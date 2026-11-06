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
        private ToolbarButton diagnosticsButton;
        private QuestRelationGraph graph;
        private QuestStageGraph stageGraph;
        private string displayedStageQuestId;
        private string notice = "";
        private string lastDocument;
        private bool showStages;
        private bool restoreSavedLayout;
        [MenuItem("DemonWeave/Quest Editor")]
        public static void Open()
        {
            var window = GetWindow<QuestEditorWindow>("DemonWeave");
            window.minSize = new Vector2(1080, 720);
            window.Show();
        }
        public static void Open(QuestBookAsset asset, MonoBehaviour host = null)
        {
            if (!asset) return;
            var window = GetWindow<QuestEditorWindow>("DemonWeave");
            window.book = asset; window.hostObject = host; window.selectedId = null; window.selectedStageId = null; window.detailSection = "Overview"; window.graph = null;
            window.showStages = true;
            window.minSize = new Vector2(1080, 700); window.CreateGUI(); window.Show(); window.Focus();
        }
        private void OnEnable() { Undo.undoRedoPerformed += OnUndoRedo; EditorApplication.playModeStateChanged += OnPlay; EditorApplication.hierarchyChanged += OnHierarchyChanged; }
        private void OnDisable() { Undo.undoRedoPerformed -= OnUndoRedo; EditorApplication.playModeStateChanged -= OnPlay; EditorApplication.hierarchyChanged -= OnHierarchyChanged; }
        private void OnUndoRedo() { restoreSavedLayout = true; try { Rebuild(); } finally { restoreSavedLayout = false; } }
        private void OnHierarchyChanged() { if (body != null && book) { LocateHost(); CreateGUI(); } }
        private void OnFocus()
        {
            if (body == null || !book) return;
            var previousHost = hostObject;
            LocateHost();
            try { if (previousHost != hostObject) CreateGUI(); else if (lastDocument != book.ExportDefinitions()) Rebuild(); } catch { Rebuild(); }
        }
        private void OnPlay(PlayModeStateChange state) { LocateHost(); CreateGUI(); }
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
            body = null; diagnostics = null; live = null; breadcrumbs = null;
            if (!book) { book = null; BuildWelcome(); return; }
            var header = new VisualElement { style = { flexDirection = FlexDirection.Row, alignItems = Align.Center, paddingLeft = 12, height = 36, backgroundColor = new Color(.10f, .12f, .16f) } };
            breadcrumbs = header;
            rootVisualElement.Add(header);
            var toolbar = new Toolbar();
            toolbar.style.height = 32; toolbar.style.paddingLeft = 12;
            toolbar.Add(new ToolbarButton(OpenCollectionAsset) { text = "Open…", tooltip = "Open another Quest Collection asset" });
            toolbar.Add(new ToolbarButton(() => { if (stageGraph != null) stageGraph.FrameAll(); else graph?.FrameAll(); }) { text = "Frame All" });
            var tables = new ToolbarMenu { text = "More" };
            tables.menu.AppendAction("New Quest", _ => AddQuest(), _ => EditorApplication.isPlaying ? DropdownMenuAction.Status.Disabled : DropdownMenuAction.Status.Normal);
            tables.menu.AppendAction("Advanced fields", _ => { technicalView = !technicalView; Rebuild(); },
                _ => technicalView ? DropdownMenuAction.Status.Checked : DropdownMenuAction.Status.Normal);
            tables.menu.AppendAction("AI Changes…", _ => QuestProposalWindow.Open(book, hostObject),
                _ => Host == null ? DropdownMenuAction.Status.Disabled : DropdownMenuAction.Status.Normal);
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
            BuildSceneConnection();
            toolbar.Add(new ToolbarButton(() => EditorUtility.DisplayDialog("Edit Quest", "Select a node to edit its properties. Connect ports to add prerequisites. Custom node types are ScriptableObject templates.", "Close")) { text = "Help" });
            body = new VisualElement { style = { flexGrow = 1, flexDirection = FlexDirection.Row } }; rootVisualElement.Add(body);
            diagnostics = new Label { style = { whiteSpace = WhiteSpace.Normal, paddingLeft = 12, paddingRight = 12, paddingTop = 8, paddingBottom = 8, maxHeight = 100 } };
            diagnosticsButton = new ToolbarButton(() => QuestDiagnosticsWindow.Open(book, hostObject)) { text = "Diagnostics" };
            toolbar.Add(diagnosticsButton);
            Rebuild();
            statusSchedule = rootVisualElement.schedule.Execute(UpdateStatus).Every(350);
        }
        private void Rebuild()
        {
            if (body == null) return;
            var oldPositions = restoreSavedLayout ? null : graph?.CapturePositions();
            var oldPan = graph?.viewTransform.position ?? Vector3.zero;
            var oldZoom = graph?.viewTransform.scale ?? Vector3.one;
            var oldScroll = inspector?.scrollOffset ?? Vector2.zero;
            var nodePositions = !restoreSavedLayout && displayedStageQuestId == selectedId ? stageGraph?.CapturePositions() : null;
            var nodePan = stageGraph?.viewTransform.position ?? Vector3.zero;
            var nodeZoom = stageGraph?.viewTransform.scale ?? Vector3.one;
            body.Clear(); graph = null; stageGraph = null; live = null;
            if (!book) { CreateGUI(); return; }
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
                    graphArea.Add(new QuestStageGraph(new QuestDefinition { Id = "preview", Title = "New Quest" },
                        createNode: () => { AddQuest(); AddStage(book.Quests.Last()); }));
                }
                else if (book.Quests.Any(q => q == null || string.IsNullOrWhiteSpace(q.Id)) || book.Quests.Select(q => q.Id).Distinct().Count() != book.Quests.Count)
                    graphArea.Add(new HelpBox("Missing or duplicate Quest IDs. Fix the definition to display dependencies.", HelpBoxMessageType.Error));
                else
                {
                    var selected = book.Quests.FirstOrDefault(q => q.Id == selectedId);
                    if (!showStages)
                    {
                        oldPositions ??= QuestGraphLayoutStore.Read(book, "$quests");
                        graph = new QuestRelationGraph(book, id => { selectedId = id; selectedStageId = null; BuildInspector(); }, Rebuild, oldPositions == null, DeleteQuests, OpenQuest, positions => QuestGraphLayoutStore.Save(book, "$quests", positions));
                        graphArea.Add(graph);
                        if (oldPositions != null) { graph.RestorePositions(oldPositions); graph.UpdateViewTransform(oldPan, oldZoom); }
                    }
                    else if (selected != null)
                    {
                        nodePositions ??= QuestGraphLayoutStore.Read(book, selected.Id);
                        var graphCapabilities = Host?.CreateCapabilities();
                        stageGraph = new QuestStageGraph(QuestBookAsset.Compile(selected, false), stageId => {
                            selectedStageId = stageId; detailSection = "Objective"; if (stageId == "$start" || stageId == "$finish") { Rebuild(); return; } BuildInspector();
                            inspector.schedule.Execute(() => { var field = inspector.Q<VisualElement>("stage-" + stageId); if (field != null) inspector.ScrollTo(field); }).ExecuteLater(1);
                        }, stage => {
                            var producer = authorProfile?.ProducerFor(Host, stage.OpenWhen);
                            var sources = producer == null ? Array.Empty<QuestStage>() : QuestBookAsset.Compile(selected, false).Stages.Where(s => s.Id != stage.Id && s.BeginExec.Concat(s.FinishExec).Any(e => authorProfile.Produces(Host, e, stage.OpenWhen))).ToArray();
                            return sources.Length == 1 ? sources[0] : null;
                        }, AuthorCondition, ids => DeleteStages(selected, ids), (from, to) => DisconnectStages(selected, from, to), (from, to) => ConnectStages(selected, from, to), () => AddStage(selected), nodePositions == null,
                            (node, action) => graphCapabilities?.Find(action.CapabilityId, node.EntityRole)?.Label ?? action.CapabilityId,
                                                        (id, position) => AddStage(selected, position), node => {
                                if (node.Control != QuestControl.None) { AssetDatabase.OpenAsset(AssetDatabase.LoadAssetAtPath<MonoScript>("Packages/com.demon.weave/Runtime/Core/Quests/QuestRun.Stages.cs")); return; }
                                var entity = Host?.BindingEntries.FirstOrDefault(b => b.Role == node.EntityRole)?.Entity;
                                if (entity) OpenImplementation(entity.GetType());
                                else { notice = "Assign a target Entity to open its code."; UpdateStatus(); }
                            }, node => node.Comment ?? Host?.BindingEntries.FirstOrDefault(b => b.Role == node.EntityRole)?.Entity?.Comment,
                            positions => QuestGraphLayoutStore.Save(book, selected.Id, positions),
                            (control, position) => AddStage(selected, position, control));
                        graphArea.Add(stageGraph);
                        displayedStageQuestId = selectedId;
                        if (nodePositions != null) { stageGraph.RestorePositions(nodePositions); stageGraph.UpdateViewTransform(nodePan, nodeZoom); }
                    }

                }
                if (showStages && !technicalView && selectedStageId != "$start" && selectedStageId != "$finish" && (book.Quests.Count == 0 || book.Quests.FirstOrDefault(q => q.Id == selectedId)?.Stages.Count == 0))
                {
                    body.Remove(split); graphArea.RemoveFromHierarchy(); body.Add(graphArea);
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
        private void Edit(string label, Action action)
        { QuestAuthoringChecks.Edit(book, label, _ => action()); notice = "Unsaved changes. The next Play session uses the new definition."; Rebuild(); }
        private void AddQuest()
        {
            if (!book || EditorApplication.isPlaying) return;
            var title = "New Quest"; int suffix = 2;
            while (book.Quests.Any(q => q.Title == title)) title = "New Quest " + suffix++;
            var quest = new QuestDefinition { Id = "quest-" + Guid.NewGuid().ToString("N"), Title = title, StageCompletion = RuleCombination.Any };
            if (book.Quests.Count > 0)
                quest.Accept = new QuestCondition { Kind = ConditionKind.QuestState, Subject = book.Quests.Last().Id, State = QuestStatus.Completed };
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
            UpdateConnectionStatus();
            var errors = QuestAuthoringChecks.Validate(book, Host);
            if (diagnosticsButton != null) diagnosticsButton.text = errors.Count > 0 ? "Diagnostics (" + errors.Count + ")" : "Diagnostics";
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
                else if (instance.Stage(selectedStageId) != null) live.text = string.Join("\n", run.ExplainStage(selectedId, selectedStageId, (role, key) => {
                    var receiver = Host.BindingEntries.FirstOrDefault(b => b.Role == role)?.Target;
                    return (receiver ? receiver.name : role) + " / " + (Host.Signals.FirstOrDefault(s => s.Role == role && s.Key == key).Label ?? key);
                }));
                else live.text = "This node is not in the running definition snapshot.";
                if (Host != null && Host.BookAsset == book && Host.Activity.Count > 0)
                    {
                    var log = Host.Activity.AsEnumerable();
                    if (!string.IsNullOrEmpty(selectedStageId))
                        log = log.Where(line => line.StartsWith(selectedId + "/" + selectedStageId + " ", StringComparison.Ordinal));
                    var recent = log.Reverse().Take(5).Reverse().ToArray();
                    if (recent.Length > 0) live.text += "\n\nRecent activity\n" + string.Join("\n", recent);
                    else if (instance == null) live.text += "\n" + Host.Activity.Last();
                }
                if (Host is QuestSceneHost sceneHost)
                {
                    if (!string.IsNullOrEmpty(selectedStageId)) live.text += "\n" + sceneHost.ExecutionStatus(selectedId, selectedStageId);
                    if (instance?.IsPaused == true && !string.IsNullOrEmpty(selectedStageId)) live.text += "\n" + string.Join("\n", run.Explain(selectedId));
                    if (sceneHost.WorldState) foreach (var fact in sceneHost.WorldState.Facts.Where(f => f != null)) live.text = live.text.Replace(fact.Id, string.IsNullOrEmpty(fact.Label) ? fact.Id : fact.Label);
                }
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
            var field = new ObjectField(entityOnly ? "Target" : ObjectNames.NicifyVariableName(label)) {
                objectType = typeof(UnityEngine.Object), allowSceneObjects = true,
                value = entityOnly && current?.Entity ? (UnityEngine.Object)current.Entity : current?.Target,
                name = "target-" + label,
                tooltip = "Drop a scene GameObject or its QuestEntity component."
            };
            field.style.flexGrow = 1; field.style.flexBasis = 0; field.style.minWidth = 0;
            field.labelElement.style.minWidth = 45; field.labelElement.style.width = 45;
            field.SetEnabled(Host != null && !EditorApplication.isPlaying); row.Add(field);
            var selector = field.Q(className: "unity-object-field__selector");
            if (selector != null) selector.style.display = DisplayStyle.None;
            Button browse = null;
            browse = IconAction(row, "⌕", "Search scene objects", () => {
                if (!hostObject) return;
                UnityEditor.PopupWindow.Show(browse.worldBound,
                    new QuestScenePicker(hostObject.gameObject.scene, false, current?.Target, go => {
                        if (field.panel != null && !EditorApplication.isPlaying) field.value = go;
                    }));
            });
            browse.SetEnabled(Host != null && !EditorApplication.isPlaying);
            IconAction(row, "◎", "Locate object in Hierarchy", () => {
                Selection.activeGameObject = current.Target; EditorGUIUtility.PingObject(current.Target);
            }).SetEnabled(current?.Target);
            if (entityOnly)
            {
                IconAction(row, "▾", "Choose a QuestEntity component on this object", () => {
                    var entities = current.Target.GetComponents<QuestEntity>();
                    PickCatalog("Components on " + current.Target.name, entities.Select((entity, index) => new QuestCatalogPicker.Entry {
                        Id = entity.Id, Label = entity.GetType().Name + " (" + (index + 1) + ")", Choose = () => field.value = entity
                    }));
                }).SetEnabled(current?.Target && current.Target.GetComponents<QuestEntity>().Length > 0 && !EditorApplication.isPlaying);
                IconAction(row, "⚙", "Inspect this component instance", () => EditorUtility.OpenPropertyEditor(current.Entity)).SetEnabled(current?.Entity);
            }
            field.RegisterValueChangedCallback(e => {
                var component = e.newValue as Component;
                var target = component ? component.gameObject : e.newValue as GameObject;
                if (e.newValue && (!target || !hostObject || target.scene != hostObject.gameObject.scene || !target.scene.IsValid()))
                { field.SetValueWithoutNotify(e.previousValue); notice = "Choose an object from the runner's scene."; UpdateStatus(); return; }
                var entity = entityOnly ? e.newValue as QuestEntity : null;
                if (entityOnly && target && !entity)
                {
                    var candidates = target.GetComponents<QuestEntity>();
                    if (candidates.Length == 1) entity = candidates[0];
                    else if (candidates.Length > 1)
                    {
                        field.SetValueWithoutNotify(e.previousValue);
                        PickCatalog("Components on " + target.name, candidates.Select((candidate, index) => new QuestCatalogPicker.Entry {
                            Id = candidate.Id, Label = candidate.GetType().Name + " (" + (index + 1) + ")", Choose = () => field.value = candidate
                        })); return;
                    }
                }
                Undo.IncrementCurrentGroup(); int group = Undo.GetCurrentGroup();
                if (!target) { changed(""); Undo.CollapseUndoOperations(group); return; }
                if (entity && (string.IsNullOrEmpty(entity.Id) || UnityEngine.Object.FindObjectsByType<QuestEntity>(FindObjectsSortMode.None).Any(other => other != entity && other.Id == entity.Id)))
                {
                    Undo.RecordObject(entity, "Assign Entity Identity");
                    var data = new SerializedObject(entity); data.FindProperty("entityId").stringValue = Guid.NewGuid().ToString("N");
                    data.ApplyModifiedProperties(); PrefabUtility.RecordPrefabInstancePropertyModifications(entity);
                }
                var binding = Host.BindingEntries.FirstOrDefault(b => entity ? b.Entity == entity : !b.Entity && b.Object == target);
                if (binding == null)
                {
                    Undo.RecordObject(hostObject, "Bind Quest Reference");
                    binding = new EntityBinding { Role = entity ? "entity:" + entity.Id : "object:" + Guid.NewGuid().ToString("N"), Entity = entity, Object = entity ? null : target };
                    Host.BindingEntries.Add(binding); MarkHost();
                }
                changed(binding.Role); Undo.CollapseUndoOperations(group);
            });
            if (!string.IsNullOrEmpty(value) && (current == null || !current.Target))
                parent.Add(new HelpBox("Target was removed. Drop a replacement scene object here.", HelpBoxMessageType.Error));
            else if (entityOnly && current?.Target && !current.Entity)
                parent.Add(new HelpBox("This object has no selected QuestEntity. A TD must provide its conditions and actions; use the component arrow once one is attached.", HelpBoxMessageType.Info));
        }
    }
}
