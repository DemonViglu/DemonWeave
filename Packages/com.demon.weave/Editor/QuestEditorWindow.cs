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
        private QuestBookAsset book;
        private string selectedId;
        private VisualElement body;
        private ScrollView inspector;
        private Label diagnostics, live;
        private QuestRelationGraph graph;
        private string notice = "";
        private string lastDocument;
        [MenuItem("DemonWeave/任务编排")]
        public static void Open()
        {
            var window = GetWindow<QuestEditorWindow>("DemonWeave · 任务编排");
            window.minSize = new Vector2(1080, 720);
            if (window.position.width < 1200) window.position = new Rect(50, 60, 1500, 850);
            window.Show();
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
            if (!hostObject) hostObject = UnityEngine.Object.FindObjectsByType<MonoBehaviour>().FirstOrDefault(x => x is IQuestAuthoringHost);
            if (!book && Host != null) book = Host.BookAsset;
        }
        public void CreateGUI()
        {
            LocateHost(); rootVisualElement.Clear();
            var toolbar = new Toolbar();
            var host = new ObjectField("场景宿主") { objectType = typeof(MonoBehaviour), allowSceneObjects = true, value = hostObject };
            host.style.width = 300;
            host.RegisterValueChangedCallback(e => {
                var value = e.newValue as MonoBehaviour;
                if (value && !(value is IQuestAuthoringHost)) { notice = "组件未提供任务作者接口。"; host.SetValueWithoutNotify(hostObject); return; }
                hostObject = value; book = Host?.BookAsset; Rebuild();
            }); toolbar.Add(host);
            var asset = new ObjectField("任务集") { objectType = typeof(QuestBookAsset), allowSceneObjects = false, value = book }; asset.style.width = 310;
            asset.RegisterValueChangedCallback(e => { book = e.newValue as QuestBookAsset; selectedId = null; Rebuild(); }); toolbar.Add(asset);
            toolbar.Add(new ToolbarButton(() => graph?.FrameAll()) { text = "总览" });
            toolbar.Add(new ToolbarButton(AddQuest) { text = "+ 独立任务" });
            toolbar.Add(new ToolbarButton(() => QuestProposalWindow.Open(book, hostObject)) { text = "AI / 局部修改" });
            toolbar.Add(new ToolbarButton(() => { if (book) { book.OnBeforeSerialize(); EditorUtility.SetDirty(book); AssetDatabase.SaveAssets(); notice = "任务定义已保存。场景绑定请 Ctrl+S 保存。"; UpdateStatus(); } }) { text = "保存定义" });
            rootVisualElement.Add(toolbar);
            var instruction = new HelpBox("卡片是独立任务，连线是接取依赖，不是执行顺序。拖动“状态”到另一任务“接取条件”：新增一个必须已完成的前置；任一/全部组合在右侧编辑。点击卡片查看规则、行为与运行原因。", HelpBoxMessageType.Info);
            rootVisualElement.Add(instruction);
            body = new VisualElement { style = { flexGrow = 1 } }; rootVisualElement.Add(body);
            diagnostics = new Label { style = { whiteSpace = WhiteSpace.Normal, paddingLeft = 12, paddingRight = 12, paddingTop = 8, paddingBottom = 8, maxHeight = 100 } };
            rootVisualElement.Add(diagnostics); Rebuild();
            rootVisualElement.schedule.Execute(UpdateStatus).Every(350);
        }
        private void Rebuild()
        {
            if (body == null) return;
            var oldPositions = graph?.CapturePositions();
            var oldPan = graph?.viewTransform.position ?? Vector3.zero;
            var oldZoom = graph?.viewTransform.scale ?? Vector3.one;
            var oldScroll = inspector?.scrollOffset ?? Vector2.zero;
            body.Clear(); graph = null; live = null;
            if (!book) { body.Add(new HelpBox("打开灯塔任务样本，选择 LighthouseScenario 组件，或选择一个任务集资产。", HelpBoxMessageType.Info)); return; }
            try
            {
                lastDocument = book.ExportDefinitions();
                if (selectedId == null || !book.Quests.Any(q => q.Id == selectedId)) selectedId = book.Quests.FirstOrDefault()?.Id;
                var split = new TwoPaneSplitView(1, 400, TwoPaneSplitViewOrientation.Horizontal) { style = { flexGrow = 1 } };
                var graphArea = new VisualElement { style = { flexGrow = 1, minWidth = 250 } }; split.Add(graphArea);
                inspector = new ScrollView { style = { minWidth = 320, paddingLeft = 14, paddingRight = 14, paddingBottom = 20 } }; split.Add(inspector); body.Add(split);
                if (book.Quests.Any(q => q == null || string.IsNullOrWhiteSpace(q.Id)) || book.Quests.Select(q => q.Id).Distinct().Count() != book.Quests.Count)
                    graphArea.Add(new HelpBox("任务标识缺失或重复，关系图暂不可用。请先修正定义。", HelpBoxMessageType.Error));
                else
                {
                    graph = new QuestRelationGraph(book, id => { selectedId = id; inspector.scrollOffset = Vector2.zero; BuildInspector(); }, Rebuild, oldPositions == null);
                    graphArea.Add(graph);
                    if (oldPositions != null) { graph.RestorePositions(oldPositions); graph.UpdateViewTransform(oldPan, oldZoom); }
                }
                BuildInspector(); UpdateStatus();
                inspector.schedule.Execute(() => inspector.scrollOffset = oldScroll).ExecuteLater(1);
            }
            catch (Exception e) { body.Add(new HelpBox(e.Message, HelpBoxMessageType.Error)); }
        }
        private void BuildInspector()
        {
            inspector.Clear();
            var editable = new VisualElement(); editable.SetEnabled(!EditorApplication.isPlaying); inspector.Add(editable);
            BuildBindings(editable);
            var quest = book.Quests.FirstOrDefault(q => q.Id == selectedId);
            if (quest != null)
            {
                editable.Add(Heading(quest.Title));
                Text(editable, "任务名称", quest.Title, value => Edit("修改任务名称", () => quest.Title = value));
                editable.Add(new Label("稳定标识：" + quest.Id) { style = { fontSize = 10, color = Color.gray } });
                QuestChoice(editable, "父任务（仅归属）", quest.ParentId, value => Edit("修改父任务", () => quest.ParentId = value), true, quest.Id);
                var cancel = new Toggle("结束时关闭未完成子任务") { value = quest.CancelChildrenOnEnd };
                cancel.RegisterValueChangedCallback(e => Edit("修改子任务结束政策", () => quest.CancelChildrenOnEnd = e.newValue)); editable.Add(cancel);
                editable.Add(Heading("接取条件 · Cond")); BuildCondition(editable, quest.Accept, value => quest.Accept = value, 0);
                BuildContents(editable, quest, false); BuildContents(editable, quest, true);
                BuildExecs(editable, quest.BeginExec, "开始时 · Begin Exec");
                BuildExecs(editable, quest.FinishExec, "完成时 · Finish Exec");
                BuildExecs(editable, quest.FailExec, "失败时 · Fail Exec");
                editable.Add(new Button(() => RemoveQuest(quest)) { text = "删除此任务（被引用时拒绝）" });
            }
            inspector.Add(Heading("运行状态与原因"));
            live = new Label { style = { whiteSpace = WhiteSpace.Normal, marginBottom = 12 } }; inspector.Add(live);
            UpdateStatus();
        }
        private void BuildBindings(VisualElement parent)
        {
            var foldout = new Foldout { text = "场景对象绑定 · 更换对象不改任务规则", value = false }; parent.Add(foldout);
            if (Host == null) { foldout.Add(new Label("未连接场景宿主。")); return; }
            if (Host.BookAsset != book) foldout.Add(new Button(() => {
                Undo.RecordObject(hostObject, "使用任务集"); Host.BookAsset = book; MarkHost(); Rebuild();
            }) { text = "将此任务集绑定到当前场景" });
            foreach (var binding in Host.BindingEntries)
            {
                var field = new ObjectField(binding.Role) { objectType = typeof(QuestEntity), allowSceneObjects = true, value = binding.Entity };
                field.RegisterValueChangedCallback(e => { Undo.RegisterCompleteObjectUndo(hostObject, "更换任务对象"); binding.Entity = e.newValue as QuestEntity; MarkHost(); UpdateStatus(); }); foldout.Add(field);
            }
            foldout.Add(new HelpBox("仅替换逻辑角色与场景实例的对应关系。电池插槽、发电机转子等组件内部引用由该玩法宿主管理。", HelpBoxMessageType.Info));
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
            var references = book.Quests.Where(q => q.Id != quest.Id && (q.ParentId == quest.Id || QuestAuthoringChecks.Dependencies(q.Accept).Any(c => c.Subject == quest.Id)
                || q.Finish.Concat(q.Fail).Any(c => c.Kind == ContentKind.QuestState && c.Subject == quest.Id))).Select(q => q.Title).ToArray();
            if (references.Length > 0) { notice = "无法删除，仍被引用：" + string.Join("、", references); UpdateStatus(); return; }
            Edit("删除独立任务", () => book.Quests.Remove(quest));
        }
        private void UpdateStatus()
        {
            if (!book || diagnostics == null) return;
            var errors = QuestAuthoringChecks.Validate(book, Host);
            diagnostics.text = errors.Count == 0 ? (Host == null ? "结构校验通过；连接宿主后才校验对象和能力。" : "结构、对象、事实和行为参数校验通过。") + " " + notice
                : string.Join("\n", errors.Take(3)) + (errors.Count > 3 ? $"\n另有 {errors.Count - 3} 项。" : "");
            var run = Host?.BookAsset == book ? Host.ActiveRun : null;
            graph?.UpdateStates(run);
            if (live != null)
                live.text = run?.Find(selectedId) == null ? "进入 Play 后显示当前任务的条件与进度。" : QuestAuthoringChecks.Status(run.Find(selectedId).State) + "\n"
                    + string.Join("\n", run.Explain(selectedId)) + "\n\n最近行为：\n" + string.Join("\n", Host.Activity.Skip(Math.Max(0, Host.Activity.Count - 8)));
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
