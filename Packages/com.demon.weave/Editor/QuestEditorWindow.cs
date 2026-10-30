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
        private IQuestAuthoringHost Host => hostObject as IQuestAuthoringHost;
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
        private string notice = "";
        private string lastDocument;
        private bool showStages;
        [MenuItem("DemonWeave/任务编辑器")]
        public static void Open()
        {
            var window = GetWindow<QuestEditorWindow>("DemonWeave · 任务编排");
            window.minSize = new Vector2(1080, 720);
            if (window.position.width < 1200) window.position = new Rect(50, 60, 1500, 850);
            window.Show();
        }
        public static void Open(QuestBookAsset asset, MonoBehaviour host = null)
        {
            if (!asset) return;
            var window = GetWindow<QuestEditorWindow>("DemonWeave");
            window.book = asset; window.hostObject = host; window.selectedId = null; window.selectedStageId = null; window.detailSection = "概览"; window.graph = null;
            window.minSize = new Vector2(1080, 700); window.CreateGUI(); window.Show(); window.Focus();
        }
        private void OnEnable() { Undo.undoRedoPerformed += Rebuild; EditorApplication.playModeStateChanged += OnPlay; }
        private void OnDisable() { Undo.undoRedoPerformed -= Rebuild; EditorApplication.playModeStateChanged -= OnPlay; }
        private void OnFocus()
        {
            if (body == null || !book) return;
            try { if (lastDocument != book.ExportDefinitions()) Rebuild(); } catch { Rebuild(); }
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
            statusSchedule?.Pause();
            rootVisualElement.style.backgroundColor = new Color(.075f, .09f, .12f);
            rootVisualElement.style.fontSize = 13;
            var header = new VisualElement { style = { flexDirection = FlexDirection.Row, alignItems = Align.Center, paddingLeft = 20, height = 62, backgroundColor = new Color(.10f, .12f, .16f) } };
            header.Add(new Label(book ? (string.IsNullOrEmpty(book.Title) ? book.name : book.Title) : "打开任务资产") { style = { fontSize = 20, flexGrow = 1 } });
            rootVisualElement.Add(header);
            if (book && book.Quests.Count == 1 && book.Quests[0].Stages.Count > 0) showStages = true;
            var toolbar = new Toolbar();
            var context = new Foldout { text = "预览场景与任务资产", value = false };
            toolbar.style.height = 32; toolbar.style.paddingLeft = 12;
            var host = new ObjectField("运行预览") { objectType = typeof(MonoBehaviour), allowSceneObjects = true, value = hostObject };
            host.style.width = 300;
            host.RegisterValueChangedCallback(e => {
                var value = e.newValue as MonoBehaviour;
                if (value && !(value is IQuestAuthoringHost)) { notice = "组件未提供任务作者接口。"; host.SetValueWithoutNotify(hostObject); return; }
                hostObject = value; book = Host?.BookAsset; Rebuild();
            }); context.Add(host);
            var asset = new ObjectField("任务集") { objectType = typeof(QuestBookAsset), allowSceneObjects = false, value = book }; asset.style.width = 310;
            asset.RegisterValueChangedCallback(e => { book = e.newValue as QuestBookAsset; selectedId = null; Rebuild(); }); context.Add(asset);
            var technicalToggle = new ToolbarToggle { text = "技术视图", value = technicalView };
            technicalToggle.RegisterValueChangedCallback(e => { technicalView = e.newValue; resourceSelection = null; Rebuild(); }); toolbar.Add(technicalToggle);
            toolbar.Add(new ToolbarButton(() => graph?.FrameAll()) { text = "总览" });
            toolbar.Add(new ToolbarButton(() => { showStages = !showStages; Rebuild(); }) { text = "切换任务关系 / 内部阶段" });
            toolbar.Add(new ToolbarButton(AddQuest) { text = "+ 独立任务" });
            toolbar.Add(new ToolbarButton(() => QuestProposalWindow.Open(book, hostObject)) { text = "AI / 局部修改" });
            toolbar.Add(new ToolbarButton(() => {
                if (!book || EditorApplication.isPlaying) return;
                book.OnBeforeSerialize(); EditorUtility.SetDirty(book); AssetDatabase.SaveAssets();
                bool sceneSaved = !hostObject || !hostObject.gameObject.scene.IsValid() || EditorSceneManager.SaveScene(hostObject.gameObject.scene);
                notice = sceneSaved ? "任务与场景绑定已保存。" : "任务资产已保存；场景绑定尚未保存。"; UpdateStatus();
            }) { text = "保存" });
            rootVisualElement.Add(toolbar);
            rootVisualElement.Add(context);
            toolbar.Add(new ToolbarButton(() => EditorUtility.DisplayDialog("编辑任务", "点击阶段卡片编辑目标、前置和行为。任务关系视图中，可拖动状态端口到接取条件端口添加完成前置。场景对象与演出资源在宿主中绑定。", "关闭")) { text = "帮助" });
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
            body.Clear(); graph = null; stageGraph = null; live = null;
            if (!book) { body.Add(new HelpBox("在 Project 中双击 QuestBook 任务资产，或将资产拖入上方任务集。无需先打开样本场景。", HelpBoxMessageType.Info)); return; }
            try
            {
                lastDocument = book.ExportDefinitions();
                LoadAuthoringProfile();
                if (!technicalView) body.Add(ResourceList());
                if (selectedId == null || !book.Quests.Any(q => q.Id == selectedId)) selectedId = book.Quests.FirstOrDefault()?.Id;
                var split = new TwoPaneSplitView(1, 400, TwoPaneSplitViewOrientation.Horizontal) { style = { flexGrow = 1 } };
                var graphArea = new VisualElement { style = { flexGrow = 1, minWidth = 250 } }; split.Add(graphArea);
                if (technicalView && Host is IQuestRecoveryAuthoring recovery)
                {
                    var recoveryButton = new Button(() => { detailSection = "恢复"; BuildInspector(); }) { text = "战败与恢复  ·  " + recovery.RecoveryTrigger + "  →  查看处理", tooltip = recovery.RecoveryOutcome };
                    recoveryButton.style.height = 40; recoveryButton.style.color = new Color(1,.78f,.42f); recoveryButton.style.backgroundColor = new Color(.18f,.13f,.12f); graphArea.Add(recoveryButton);
                }
                var detailPane = new VisualElement { style = { minWidth = 320, flexGrow = 1 } };
                inspectorHeader = new VisualElement { style = { paddingLeft = 14, paddingRight = 14, backgroundColor = new Color(.10f,.13f,.18f) } }; detailPane.Add(inspectorHeader);
                inspector = new ScrollView { style = { flexGrow = 1, paddingLeft = 14, paddingRight = 14, paddingBottom = 20 } }; detailPane.Add(inspector); split.Add(detailPane); body.Add(split);
                if (book.Quests.Any(q => q == null || string.IsNullOrWhiteSpace(q.Id)) || book.Quests.Select(q => q.Id).Distinct().Count() != book.Quests.Count)
                    graphArea.Add(new HelpBox("任务标识缺失或重复，关系图暂不可用。请先修正定义。", HelpBoxMessageType.Error));
                else
                {
                    var selected = book.Quests.FirstOrDefault(q => q.Id == selectedId);
                    if (showStages && selected != null)
                    {
                        stageGraph = new QuestStageGraph(selected, stageId => {
                            resourceSelection = null; selectedStageId = stageId; detailSection = "目标"; BuildInspector();
                            inspector.schedule.Execute(() => { var field = inspector.Q<VisualElement>("stage-" + stageId); if (field != null) inspector.ScrollTo(field); }).ExecuteLater(1);
                        }, stage => {
                            var producer = authorProfile?.ProducerFor(Host, stage.OpenWhen);
                            return producer == null ? null : selected.Stages.FirstOrDefault(s => s.BeginExec.Any(e => e.CapabilityId == producer));
                        }, condition => Host?.Signals.FirstOrDefault(s => s.Role == condition.Subject && s.Key == condition.Key).Label ?? condition.Key);
                        graphArea.Add(stageGraph);
                    }
                    else
                    {
                    graph = new QuestRelationGraph(book, id => { selectedId = id; inspector.scrollOffset = Vector2.zero; BuildInspector(); }, Rebuild, oldPositions == null);
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
            if (stage != null) inspectorHeader.Add(new Button(() => { selectedStageId = null; detailSection = "概览"; BuildInspector(); }) { text = "← 返回任务概览" });
            var sections = stage == null ? new[] { "概览", "接取", "失败", "恢复", "绑定", "运行" } : new[] { "目标", "条件", "行为", "路线", "恢复", "运行" };
            if (!sections.Contains(detailSection)) detailSection = sections[0];
            Tabs(inspectorHeader, sections, detailSection, value => { detailSection = value; inspector.scrollOffset = Vector2.zero; BuildInspector(); });
            var editable = new VisualElement(); editable.SetEnabled(!EditorApplication.isPlaying); inspector.Add(editable);
            if (detailSection == "恢复") BuildRecovery(editable);
            else if (detailSection == "绑定") BuildBindings(editable);
            else if (detailSection == "运行")
            { inspector.Add(new HelpBox("这里只展示当前运行实例；内容编辑在退出 Play 后进行。", HelpBoxMessageType.Info)); live = new Label { style = { whiteSpace = WhiteSpace.Normal } }; inspector.Add(live); }
            else if (stage != null) BuildStages(editable, quest);
            else if (detailSection == "概览")
            {
                Text(editable, "任务名称", quest.Title, value => Edit("修改任务名称", () => quest.Title = value));
                BuildStages(editable, quest);
                if (quest.Stages.Count == 0) BuildContents(editable, quest, false);
                var advanced = new Foldout { text = "高级：归属与生命周期", value = false }; editable.Add(advanced);
                QuestChoice(advanced, "父任务", quest.ParentId, value => Edit("修改归属", () => quest.ParentId = value), true, quest.Id);
                var cancel = new Toggle("结束时关闭未完成子任务") { value = quest.CancelChildrenOnEnd }; cancel.RegisterValueChangedCallback(e => Edit("修改结束政策", () => quest.CancelChildrenOnEnd = e.newValue)); advanced.Add(cancel);
                BuildExecs(advanced, quest.BeginExec, "任务开始行为"); BuildExecs(advanced, quest.FinishExec, "任务完成行为");
            }
            else if (detailSection == "接取") BuildCondition(editable, quest.Accept, value => quest.Accept = value, 0);
            else if (detailSection == "失败")
            {
                if (quest.Fail.Count == 0) editable.Add(new HelpBox("此任务没有内容失败条件。玩家战败后的处理请查看“恢复”，它不等于任务内容判定失败。", HelpBoxMessageType.Info));
                BuildContents(editable, quest, true); BuildExecs(editable, quest.FailExec, "任务失败行为");
            }
            UpdateStatus();
        }
        private void BuildRecovery(VisualElement parent)
        {
            parent.Add(Heading("战败与恢复"));
            if (!(Host is IQuestRecoveryAuthoring recovery))
            { parent.Add(new HelpBox("当前资产没有连接提供恢复政策的场景宿主。恢复由游戏宿主管理，不自动推断为任务路线。", HelpBoxMessageType.Info)); return; }
            parent.Add(new Label("触发  ·  " + recovery.RecoveryTrigger) { style = { whiteSpace = WhiteSpace.Normal, marginBottom = 16 } });
            var data = new SerializedObject(hostObject); var property = data.FindProperty(recovery.RecoveryProperty);
            if (property != null)
            {
                var lastValue = property.contentHash;
                var field = new PropertyField(property, "处理方式"); parent.Add(field);
                field.RegisterCallback<SerializedPropertyChangeEvent>(_ => {
                    data.ApplyModifiedProperties();
                    var current = data.FindProperty(recovery.RecoveryProperty).contentHash;
                    if (current == lastValue) return;
                    lastValue = current; MarkHost();
                    rootVisualElement.schedule.Execute(Rebuild);
                });
                field.Bind(data);
            }
            parent.Add(new Label("结果  ·  " + recovery.RecoveryOutcome) { style = { whiteSpace = WhiteSpace.Normal, marginTop = 16, marginBottom = 16 } });
            parent.Add(new HelpBox("这是整个任务范围的宿主恢复政策。回档会取消旧尝试、清理动态对象并重建安全点；剧情选路则保留世界后果。", HelpBoxMessageType.Info));
        }
        private void BuildBindings(VisualElement parent)
        {
            var foldout = new Foldout { text = "场景对象绑定", value = true }; parent.Add(foldout);
            if (Host == null) { foldout.Add(new Label("未连接场景宿主。")); return; }
            if (Host.BookAsset != book) foldout.Add(new Button(() => {
                Undo.RecordObject(hostObject, "使用任务集"); Host.BookAsset = book; MarkHost(); Rebuild();
            }) { text = "将此任务集绑定到当前场景" });
            foreach (var binding in Host.BindingEntries)
            {
                var field = new ObjectField(binding.Role) { objectType = typeof(QuestEntity), allowSceneObjects = true, value = binding.Entity };
                field.RegisterValueChangedCallback(e => { Undo.RegisterCompleteObjectUndo(hostObject, "更换任务对象"); binding.Entity = e.newValue as QuestEntity; MarkHost(); UpdateStatus(); }); foldout.Add(field);
            }
            foldout.Add(new HelpBox("此处替换逻辑角色对应的场景实例。对象内部的组件与资源引用，在相应 Gameplay 组件中配置。", HelpBoxMessageType.Info));
        }
        private void MarkHost()
        {
            EditorUtility.SetDirty(hostObject); PrefabUtility.RecordPrefabInstancePropertyModifications(hostObject);
            EditorSceneManager.MarkSceneDirty(hostObject.gameObject.scene);
        }
        private void Edit(string label, Action action)
        { QuestAuthoringChecks.Edit(book, label, _ => action()); notice = "有未保存修改；运行实例在下次 Play 读取。"; Rebuild(); }
        private void AddQuest()
        {
            if (!book || EditorApplication.isPlaying) return;
            var id = "quest-" + Guid.NewGuid().ToString("N").Substring(0, 10);
            QuestAuthoringChecks.Edit(book, "新增独立任务", quests => quests.Add(new QuestDefinition { Id = id, Title = "新任务" }));
            selectedId = id; Rebuild();
        }
        private void RemoveQuest(QuestDefinition quest)
        {
            var references = book.Quests.Where(q => q.Id != quest.Id && QuestAuthoringChecks.References(q, quest.Id)).Select(q => q.Title).ToArray();
            if (references.Length > 0) { notice = "无法删除，仍被引用：" + string.Join("、", references); UpdateStatus(); return; }
            Edit("删除独立任务", () => book.Quests.Remove(quest));
        }
        private void UpdateStatus()
        {
            if (!book || diagnostics == null) return;
            var errors = QuestAuthoringChecks.Validate(book, Host);
            diagnostics.text = errors.Count == 0 ? notice
                : string.Join("\n", errors.Take(3)) + (errors.Count > 3 ? $"\n另有 {errors.Count - 3} 项。" : "");
            diagnostics.style.display = string.IsNullOrWhiteSpace(diagnostics.text) ? DisplayStyle.None : DisplayStyle.Flex;
            var run = Host?.BookAsset == book ? Host.ActiveRun : null;
            graph?.UpdateStates(run);
            stageGraph?.UpdateStates(run?.Find(selectedId));
            if (!technicalView && live != null)
            {
                var stage = run?.Find(selectedId)?.Stage(selectedStageId);
                live.text = stage == null ? "进入 Play 后查看此阶段的进度。" : QuestAuthoringChecks.Status(stage.State) + "\n" + string.Join("\n", run.ExplainStage(selectedId, selectedStageId));
                return;
            }
            if (live != null)
                live.text = run?.Find(selectedId) == null ? "进入 Play 后显示当前任务的条件与进度。" : QuestAuthoringChecks.Status(run.Find(selectedId).State) + "\n"
                    + string.Join("\n", run.Explain(selectedId)) + "\n\n最近行为：\n" + string.Join("\n", Host.Activity.Skip(Math.Max(0, Host.Activity.Count - 8)));
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
            Choice(parent, label, ids, value, changed, id => string.IsNullOrEmpty(id) ? "无" : book.Quests.FirstOrDefault(q => q.Id == id)?.Title ?? "缺失：" + id);
        }
        private void RoleChoice(VisualElement parent, string label, string value, Action<string> changed)
        { Choice(parent, label, Host?.BindingEntries.Select(b => b.Role).ToList() ?? new List<string>(), value, changed,
            role => Host?.BindingEntries.FirstOrDefault(b => b.Role == role)?.Entity is QuestEntity entity && entity ? entity.DisplayName + "（" + role + "）" : role + "（未绑定）"); }
    }
}
