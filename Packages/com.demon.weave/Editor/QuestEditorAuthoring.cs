using System;
using System.Collections.Generic;
using System.Linq;
using DemonWeave.Quests;
using DemonWeave.Unity;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace DemonWeave.Editor
{
    public sealed partial class QuestEditorWindow
    {
        private bool technicalView;
        private string resourceSelection;
        private HelpBox resourceValidation;
        private QuestAuthoringProfile authorProfile;
        private IReadOnlyList<QuestAuthoringResource> authorResources = Array.Empty<QuestAuthoringResource>();
        private void LoadAuthoringProfile()
        {
            authorProfile = null;
            if (Host != null)
                foreach (var type in TypeCache.GetTypesDerivedFrom<QuestAuthoringProfile>().Where(t => !t.IsAbstract))
                {
                    var candidate = (QuestAuthoringProfile)Activator.CreateInstance(type);
                    if (candidate.Supports(Host)) { authorProfile = candidate; break; }
                }
            authorResources = authorProfile?.Resources(Host) ?? Array.Empty<QuestAuthoringResource>();
        }
        private VisualElement ResourceList()
        {
            var list = new ScrollView { style = { width = 230, minWidth = 190, paddingLeft = 12, paddingRight = 12, backgroundColor = new Color(.09f,.11f,.15f) } };
            list.Add(Heading("对象与资源"));
            foreach (var resource in authorResources)
            {
                var button = new Button(() => { resourceSelection = resource.Id; BuildInspector(); }) { text = resource.Label };
                button.style.height = 40; button.style.unityTextAlign = TextAnchor.MiddleLeft; list.Add(button);
            }
            if (authorResources.Count == 0)
                list.Add(new Label("连接场景预览后可查看对象与资源。") { style = { whiteSpace = WhiteSpace.Normal } });
            return list;
        }
        private VisualElement AuthorCard(VisualElement parent, string title)
        {
            var card = new VisualElement { style = { backgroundColor = new Color(.11f,.14f,.19f), paddingLeft = 14, paddingRight = 14, paddingBottom = 14, marginTop = 12, borderTopLeftRadius = 6, borderTopRightRadius = 6, borderBottomLeftRadius = 6, borderBottomRightRadius = 6 } };
            card.Add(Heading(title)); parent.Add(card); return card;
        }
        private void Describe(VisualElement parent, string text)
        { if (!string.IsNullOrEmpty(text)) parent.Add(new Label(text) { style = { whiteSpace = WhiteSpace.Normal, marginTop = 6, marginBottom = 8 } }); }
        private void SelectStage(string id)
        { resourceSelection = null; selectedStageId = id; stageGraph?.SelectStage(id); BuildInspector(); }
        private string AuthorCondition(QuestCondition condition)
        {
            if (condition == null) return "未配置开始条件";
            if (condition.Kind == ConditionKind.Fact)
            {
                var signal = Host?.Signals.FirstOrDefault(s => s.Role == condition.Subject && s.Key == condition.Key);
                var name = signal?.Label ?? condition.Key;
                return condition.Expected ? name : "尚未发生：" + name;
            }
            if (condition.Kind == ConditionKind.All || condition.Kind == ConditionKind.Any)
                return condition.Children.Count == 0 ? "无额外限制" : string.Join(condition.Kind == ConditionKind.All ? "，并且 " : "，或者 ", condition.Children.Select(AuthorCondition));
            return QuestAuthoringChecks.ConditionText(condition, id => book.Quests.FirstOrDefault(q => q.Id == id)?.Title ?? id);
        }
        private void BuildAuthorInspector(QuestDefinition quest)
        {
            var resource = authorResources.FirstOrDefault(r => r.Id == resourceSelection);
            if (resource != null) { BuildResource(resource, quest); return; }
            var stage = quest.Stages.FirstOrDefault(s => s.Id == selectedStageId);
            inspectorHeader.Add(Heading(stage?.Title ?? quest.Title));
            if (stage != null) inspectorHeader.Add(new Button(() => SelectStage(null)) { text = "← 任务概览" });
            var content = new VisualElement(); content.SetEnabled(!EditorApplication.isPlaying); inspector.Add(content);
            if (stage == null)
            {
                Text(content, "任务名称", quest.Title, value => Edit("修改任务名称", () => quest.Title = value));
                var start = AuthorCard(content, "开始任务");
                Describe(start, AuthorCondition(quest.Accept));
                var startEdit = new Foldout { text = "修改开始条件", value = false }; start.Add(startEdit); BuildCondition(startEdit, quest.Accept, value => quest.Accept = value, 0);
                var flow = AuthorCard(content, "任务内容");
                foreach (var step in quest.Stages) flow.Add(new Button(() => SelectStage(step.Id)) { text = step.Title + "  →" });
                flow.Add(new Button(() => ShowStageMenu(quest)) { text = "+ 添加任务内容" });
                return;
            }
            Text(content, "名称", stage.Title, value => Edit("修改阶段名称", () => stage.Title = value));
            bool outcomeHandler = authorProfile?.ProducerFor(Host, stage.OpenWhen) != null && stage.Objectives.Count == 1
                && stage.Objectives[0].Kind == ContentKind.Fact && stage.Objectives[0].Subject == stage.OpenWhen.Subject
                && stage.Objectives[0].Key == stage.OpenWhen.Key && stage.Objectives[0].Expected == stage.OpenWhen.Expected;
            var when = AuthorCard(content, "何时开始");
            foreach (var previous in stage.After) Describe(when, "在“" + (quest.Stages.FirstOrDefault(s => s.Id == previous)?.Title ?? previous) + "”之后");
            if (stage.OpenWhen.Kind != ConditionKind.All || stage.OpenWhen.Children.Count > 0) Describe(when, AuthorCondition(stage.OpenWhen));
            if (stage.After.Count == 0 && stage.OpenWhen.Kind == ConditionKind.All && stage.OpenWhen.Children.Count == 0) Describe(when, "任务开始后开放");
            var dependencies = new Foldout { text = "修改开始时机", value = false }; when.Add(dependencies);
            foreach (var other in quest.Stages.Where(s => s != stage))
            {
                var toggle = new Toggle(other.Title) { value = stage.After.Contains(other.Id) };
                toggle.RegisterValueChangedCallback(e => Edit("修改前置", () => { if (e.newValue) stage.After.Add(other.Id); else stage.After.Remove(other.Id); })); dependencies.Add(toggle);
            }
            BuildCondition(dependencies, stage.OpenWhen, value => stage.OpenWhen = value, 0);
            var actions = AuthorCard(content, "做什么");
            if (!outcomeHandler || stage.BeginExec.Count > 0) BuildExecs(actions, stage.BeginExec, "进入此阶段时执行");
            if (stage.FinishExec.Count > 0) BuildExecs(actions, stage.FinishExec, "结果确认后执行");
            if (authorProfile != null)
            {
                foreach (var signal in stage.BeginExec.SelectMany(e => authorProfile.Outcomes(Host, e.CapabilityId)).Where(s => !s.IsEvent))
                {
                    var existing = quest.Stages.FirstOrDefault(s => s != stage && s.OpenWhen.Kind == ConditionKind.Fact && s.OpenWhen.Subject == signal.Role && s.OpenWhen.Key == signal.Key && s.OpenWhen.Expected);
                    if (existing != null)
                        actions.Add(new Button(() => SelectStage(existing.Id)) { text = signal.Label + "  →  " + existing.Title });
                    else
                        actions.Add(new Button(() => {
                            var menu = new GenericMenu();
                            foreach (var capability in Host.CreateCapabilities().Entries)
                            {
                                var selected = capability;
                                menu.AddItem(new GUIContent("结果发生后 / " + selected.Label), false, () => {
                                    var branch = QuestOutcomeAuthoring.CreateBranch(signal, selected);
                                    selectedStageId = branch.Id;
                                    Edit("添加结果处理分支", () => quest.Stages.Add(branch));
                                });
                            }
                            menu.ShowAsContext();
                        }) { text = signal.Label + "  →  添加处理分支" });
                }
            }
            var used = authorProfile?.ResourcesFor(Host, stage).Distinct().ToArray() ?? Array.Empty<string>();
            if (used.Length > 0)
            {
                var uses = AuthorCard(content, "使用的对象与资源");
                foreach (var id in used)
                {
                    var item = authorResources.FirstOrDefault(r => r.Id == id); if (item == null) continue;
                    uses.Add(new Button(() => { resourceSelection = id; BuildInspector(); }) { text = item.Label + "  →" });
                }
            }
            var results = AuthorCard(content, outcomeHandler ? "结果处理" : "什么结果后继续");
            if (outcomeHandler) Describe(results, "响应已确认的遭遇结果；具体执行由上方选择的行为负责。");
            else
            {
            foreach (var objective in stage.Objectives.ToArray())
            {
                var signals = Host?.Signals.ToArray() ?? Array.Empty<WorldSignal>();
                var current = Array.FindIndex(signals, s => s.Role == objective.Subject && s.Key == objective.Key && s.IsEvent == (objective.Kind == ContentKind.EventCount));
                if (current >= 0 && objective.Expected)
                {
                    var options = signals.Select(s => s.Role + "/" + s.Key).ToList();
                    Choice(results, "等待", options, options[current], value => Edit("修改等待结果", () => {
                        var signal = signals[options.IndexOf(value)]; objective.Subject = signal.Role; objective.Key = signal.Key;
                        objective.Kind = signal.IsEvent ? ContentKind.EventCount : ContentKind.Fact; objective.Label = signal.Label;
                    }), value => signals[options.IndexOf(value)].Label);
                }
                else Describe(results, objective.Label);
            }
            if (stage.Objectives.Count == 0) Describe(results, "无额外等待目标；行为请求受理不代表异步动作已完成。");
            var extra = new Foldout { text = "编辑目标规则", value = false }; results.Add(extra);
            if (Host != null) results.Add(new Button(() => {
                var menu = new GenericMenu();
                foreach (var signal in Host.Signals)
                {
                    var chosen = signal;
                    menu.AddItem(new GUIContent(chosen.Label), false, () => Edit("添加等待结果", () => stage.Objectives.Add(GoalFor(chosen))));
                }
                menu.ShowAsContext();
            }) { text = "+ 等待结果" });
            BuildObjectives(extra, stage.Objectives, stage.Completion, value => stage.Completion = value, "目标");
            }
            foreach (var next in quest.Stages.Where(s => s.After.Contains(stage.Id))) results.Add(new Button(() => SelectStage(next.Id)) { text = "继续到：" + next.Title });
            var branches = new Foldout { text = "剧情路线（" + stage.Routes.Count + "）", value = false }; content.Add(branches);
            var savedSection = detailSection; detailSection = "路线"; BuildStages(branches, quest); detailSection = savedSection;
            if (stage.FinishExec.Count == 0) { var after = new Foldout { text = "完成后行为", value = false }; content.Add(after); BuildExecs(after, stage.FinishExec, "完成后执行"); }
            inspector.Add(Heading("运行进度")); live = new Label { style = { whiteSpace = WhiteSpace.Normal } }; inspector.Add(live); UpdateStatus();
        }
        private void BuildResource(QuestAuthoringResource resource, QuestDefinition quest)
        {
            inspectorHeader.Add(Heading(resource.Label));
            inspectorHeader.Add(new Button(() => { resourceSelection = null; BuildInspector(); }) { text = "← 返回任务内容" });
            var panel = new VisualElement(); panel.SetEnabled(!EditorApplication.isPlaying); inspector.Add(panel);
            Describe(panel, resource.Description);
            if (resource.Owner)
            {
                var owner = new SerializedObject(resource.Owner);
                var property = string.IsNullOrEmpty(resource.PropertyPath) ? null : owner.FindProperty(resource.PropertyPath);
                if (property != null) AddResourceProperty(panel, owner, property, "使用对象 / 资产");
                var target = property?.propertyType == SerializedPropertyType.ObjectReference ? property.objectReferenceValue : resource.Owner;
                if (target)
                {
                    panel.Add(new Button(() => { Selection.activeObject = target; EditorGUIUtility.PingObject(target); }) { text = "在项目 / 场景中定位" });
                    if (AssetDatabase.Contains(target)) panel.Add(new Button(() => AssetDatabase.OpenAsset(target)) { text = "打开资源编辑器" });
                    var settings = new SerializedObject(target);
                    for (int i = 0; i < resource.Settings.Length; i++)
                    {
                        var field = settings.FindProperty(resource.Settings[i]);
                        if (field != null) AddResourceProperty(panel, settings, field, i < resource.SettingLabels.Length ? resource.SettingLabels[i] : field.displayName);
                    }
                }
            }
            if (!string.IsNullOrEmpty(resource.ProducerCapability))
            {
                foreach (var producer in quest.Stages.Where(s => s.BeginExec.Any(e => e.CapabilityId == resource.ProducerCapability)))
                    panel.Add(new Button(() => SelectStage(producer.Id)) { text = "来自：" + producer.Title });
            }
            resourceValidation = new HelpBox("", HelpBoxMessageType.Error); panel.Add(resourceValidation); RefreshResourceError();
            panel.Add(Heading("使用位置"));
            foreach (var stage in quest.Stages.Where(s => authorProfile.ResourcesFor(Host, s).Contains(resource.Id))) panel.Add(new Button(() => SelectStage(stage.Id)) { text = stage.Title + "  →" });
        }
        private void AddResourceProperty(VisualElement parent, SerializedObject data, SerializedProperty property, string label)
        {
            var path = property.propertyPath; var hash = property.contentHash;
            if (property.propertyType == SerializedPropertyType.Integer)
            {
                var number = new IntegerField(label) { value = property.intValue, isDelayed = true, name = "resource-" + path };
                number.RegisterValueChangedCallback(e => {
                    data.Update(); data.FindProperty(path).intValue = e.newValue;
                    data.ApplyModifiedProperties(); ResourceChanged(data.targetObject);
                });
                parent.Add(number); return;
            }
            var field = new PropertyField(property, label) { name = "resource-" + path }; parent.Add(field);
            field.RegisterCallback<SerializedPropertyChangeEvent>(_ => {
                // Let Unity finish its binding transaction before reading or rebuilding the field.
                field.schedule.Execute(() => {
                    data.Update(); var current = data.FindProperty(path).contentHash;
                    if (current == hash) return; hash = current;
                    ResourceChanged(data.targetObject);
                    if (data.FindProperty(path).propertyType == SerializedPropertyType.ObjectReference) Rebuild();
                });
            }); field.Bind(data);
        }
        private void ResourceChanged(UnityEngine.Object owner)
        {
            EditorUtility.SetDirty(owner);
            if (owner is Component component) UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(component.gameObject.scene);
            notice = "有未保存修改。保存将写入任务资产及当前绑定场景。"; UpdateStatus();
            RefreshResourceError();
        }
        private static QuestContent GoalFor(WorldSignal signal) => new QuestContent {
            Id = Guid.NewGuid().ToString("N"), Label = signal.Label, Subject = signal.Role, Key = signal.Key,
            Kind = signal.IsEvent ? ContentKind.EventCount : ContentKind.Fact
        };
        private void ShowStageMenu(QuestDefinition quest)
        {
            var menu = new GenericMenu();
            if (Host == null) { menu.AddDisabledItem(new GUIContent("连接场景预览以选择已有行为和结果")); menu.ShowAsContext(); return; }
            void Add(WorldSignal signal, CapabilityDescriptor action)
            {
                var stage = new QuestStage { Id = Guid.NewGuid().ToString("N"), Title = action?.Label ?? signal.Label };
                stage.Objectives.Add(GoalFor(signal));
                if (action != null) stage.BeginExec.Add(new QuestExec { Id = Guid.NewGuid().ToString("N"), CapabilityId = action.Id, ArgumentsJson = action.DefaultJson() });
                selectedStageId = stage.Id; Edit("添加任务内容", () => quest.Stages.Add(stage));
            }
            foreach (var signal in Host.Signals) { var chosen = signal; menu.AddItem(new GUIContent("等待 / " + signal.Label), false, () => Add(chosen, null)); }
            foreach (var action in Host.CreateCapabilities().Entries)
                foreach (var outcome in authorProfile?.Outcomes(Host, action.Id) ?? Array.Empty<WorldSignal>())
                {
                    var chosen = outcome; var capability = action;
                    menu.AddItem(new GUIContent("执行 / " + capability.Label + " / 等待：" + chosen.Label), false, () => Add(chosen, capability));
                }
            menu.ShowAsContext();
        }
        private void RefreshResourceError()
        {
            if (resourceValidation == null || resourceSelection == null) return;
            var error = authorProfile?.ValidateResource(Host, resourceSelection);
            resourceValidation.text = error ?? "";
            resourceValidation.style.display = string.IsNullOrEmpty(error) ? DisplayStyle.None : DisplayStyle.Flex;
        }
    }
}
