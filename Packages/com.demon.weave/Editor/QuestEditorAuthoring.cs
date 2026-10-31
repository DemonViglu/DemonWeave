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
        private QuestAuthoringProfile authorProfile;
        private void LoadAuthoringProfile()
        {
            authorProfile = null;
            if (Host != null)
                foreach (var type in TypeCache.GetTypesDerivedFrom<QuestAuthoringProfile>().Where(t => !t.IsAbstract))
                {
                    var candidate = (QuestAuthoringProfile)Activator.CreateInstance(type);
                    if (candidate.Supports(Host)) { authorProfile = candidate; break; }
                }
        }
        private VisualElement TaskList()
        {
            var list = new ScrollView { style = { width = 230, minWidth = 190, paddingLeft = 12, paddingRight = 12, backgroundColor = new Color(.09f,.11f,.15f) } };
            list.Add(Heading("Quests"));
            foreach (var quest in book.Quests)
            {
                var button = new Button(() => OpenQuest(quest.Id)) { text = quest.Title + (QuestBookAsset.IsDraft(quest) ? "  · Draft" : ""), tooltip = "Open Quest" };
                button.style.height = 36; button.style.unityTextAlign = TextAnchor.MiddleLeft;
                if (quest.Id == selectedId) button.style.backgroundColor = new Color(.20f,.32f,.40f);
                button.AddManipulator(new ContextualMenuManipulator(evt => evt.menu.AppendAction("Delete Quest", _ => RemoveQuest(quest), _ => EditorApplication.isPlaying ? DropdownMenuAction.Status.Disabled : DropdownMenuAction.Status.Normal)));
                list.Add(button);
            }
            var create = new Button(AddQuest) { text = "+ New Quest", name = "new-quest" }; create.SetEnabled(!EditorApplication.isPlaying); list.Add(create);
            return list;
        }
        private void SelectStage(string id)
        { selectedStageId = id; stageGraph?.SelectStage(id); BuildInspector(); }
        private string AuthorCondition(QuestCondition condition)
        {
            if (condition == null) return "No activation condition";
            if (condition.Kind == ConditionKind.Fact)
            {
                var signal = Host?.Signals.FirstOrDefault(s => s.Role == condition.Subject && s.Key == condition.Key);
                var name = signal?.Label ?? condition.Key;
                return condition.Expected ? name : "Not: " + name;
            }
            if (condition.Kind == ConditionKind.All || condition.Kind == ConditionKind.Any)
                return condition.Children.Count == 0 ? "No additional conditions" : string.Join(condition.Kind == ConditionKind.All ? " AND " : " OR ", condition.Children.Select(AuthorCondition));
            return QuestAuthoringChecks.ConditionText(condition, id => book.Quests.FirstOrDefault(q => q.Id == id)?.Title ?? id);
        }
        private VisualElement ItemRow(VisualElement parent)
        {
            var row = new VisualElement { style = { flexDirection = FlexDirection.Row, alignItems = Align.Center, minHeight = 24, marginTop = 2 } };
            parent.Add(row); return row;
        }
        private Button IconAction(VisualElement row, string icon, string hint, Action action)
        {
            var button = new Button(action) { text = icon, tooltip = hint };
            button.style.width = 24; button.style.height = 22; button.style.flexShrink = 0; row.Add(button); return button;
        }
        private void BuildAuthorInspector(QuestDefinition quest)
        {
            var stage = quest.Stages.FirstOrDefault(s => s.Id == selectedStageId);
            var header = ItemRow(inspectorHeader);
            header.Add(new Label((stage == null ? "Quest / " : "Node / ") + (stage?.Title ?? quest.Title)) { style = { flexGrow = 1, unityFontStyleAndWeight = FontStyle.Bold } });
            IconAction(header, "#", stage == null ? "Copy QuestId" : "Copy NodeId", () => EditorGUIUtility.systemCopyBuffer = stage?.Id ?? quest.Id);
            if (stage != null) IconAction(header, "←", "Quest Properties", () => SelectStage(null));
            IconAction(header, "×", stage == null ? "Delete Quest" : "Delete Node", () => { if (stage == null) RemoveQuest(quest); else DeleteStages(quest, new[] { stage.Id }); }).SetEnabled(!EditorApplication.isPlaying);
            var content = new VisualElement { style = { paddingTop = 8 } }; content.SetEnabled(!EditorApplication.isPlaying); inspector.Add(content);
            Text(content, "Name", stage?.Title ?? quest.Title, value => Edit("Rename", () => { if (stage == null) quest.Title = value; else stage.Title = value; }));
            if (stage == null)
            {
                var conditions = new Foldout { text = "Activation Conditions", value = false }; content.Add(conditions);
                BuildCondition(conditions, quest.Accept, value => quest.Accept = value, 0);
                var row = ItemRow(content); row.Add(new Label("Nodes") { style = { flexGrow = 1 } });
                IconAction(row, "+", "Add Node", () => AddStage(quest, null));
                row.Add(new Button(() => ShowStageMenu(quest)) { text = "From Template…" });
                if (QuestBookAsset.IsDraft(quest)) content.Add(new Label("Draft"));
                BuildAuthorProgress();
                return;
            }
            if (Host == null) content.Add(new HelpBox("No Scene Host is connected. Node definitions remain editable; open the gameplay scene or select a Scene Host in Preview Context to choose registered actions, facts and scene objects.", HelpBoxMessageType.Info));
            if (stage.TypeId != "core.stage") try
            {
                var types = QuestNodeCatalog.Read(); types.TryGetValue(stage.TypeId ?? "", out var type);
                var row = ItemRow(content);
                row.Add(new TextField("TypeId") { value = stage.TypeId, isReadOnly = true, tooltip = type ? type.DisplayName : "Built-in / custom node", style = { flexGrow = 1 } });
                IconAction(row, "↗", "Open Node Type asset", () => AssetDatabase.OpenAsset(type)).SetEnabled(type);
                var script = type ? MonoScript.FromScriptableObject(type) : stage.TypeId == "core.stage" ? AssetDatabase.LoadAssetAtPath<MonoScript>("Packages/com.demon.weave/Runtime/Core/Quests/QuestStages.cs") : null;
                IconAction(row, "{}", "Open Node Type implementation", () => AssetDatabase.OpenAsset(script)).SetEnabled(script);
            }
            catch (Exception error) { content.Add(new HelpBox(error.Message, HelpBoxMessageType.Error)); }
            RoleChoice(content, "Entity", stage.EntityRole, role => Edit("Change Node Entity", () => BindNodeEntity(stage, role)), true);
            if(string.IsNullOrEmpty(stage.EntityRole)) content.Add(new HelpBox("Choose one Entity for this node. Conditions, objectives and actions use this component only.",HelpBoxMessageType.Info));
            var required = new Toggle("Required for Quest completion") { value = stage.Required, name = "node-required", tooltip = "When enabled, this node must complete before the Quest can complete. Disable for optional response nodes such as player defeat." };
            required.RegisterValueChangedCallback(e => Edit("Change Required Node", () => stage.Required = e.newValue)); content.Add(required);
            var hasCondition = stage.OpenWhen.Kind != ConditionKind.All || stage.OpenWhen.Children.Count > 0;
            var additional = new Foldout { text = "Additional Conditions", value = hasCondition }; content.Add(additional);
            BuildCondition(additional, stage.OpenWhen, value => stage.OpenWhen = value, 0);
            BuildExecs(content, stage.BeginExec, "On Enter");
            {
                var heading = ItemRow(content); heading.style.marginTop = 8;
                heading.Add(new Label("Objective") { tooltip = "What must be achieved for this node to complete.", style = { flexGrow = 1, unityFontStyleAndWeight = FontStyle.Bold } });
                IconAction(heading, "+", "Add Objective", () => Edit("Add Objective", () => stage.Objectives.Add(
                    new QuestContent { Id = Guid.NewGuid().ToString("N"), Label = "New Objective", Kind = ContentKind.Fact })));
                if (stage.Objectives.Count == 0)
                    content.Add(new HelpBox("Choose an objective with +. This node can be saved now, but needs an objective before running.", HelpBoxMessageType.Info));
                if (stage.Objectives.Count > 1)
                    Choice(content, "Complete", new List<string> { "All objectives", "Any objective" },
                        stage.Completion == RuleCombination.All ? "All objectives" : "Any objective",
                        value => Edit("Change Objective Combination", () => stage.Completion = value == "All objectives" ? RuleCombination.All : RuleCombination.Any));
                foreach (var objective in stage.Objectives.ToArray())
                {
                    var row = ItemRow(content); var valueArea = new VisualElement { style = { flexGrow = 1 } }; row.Add(valueArea);
                    if (objective.Kind == ContentKind.Fact || objective.Kind == ContentKind.EventCount)
                    {
                        BuildObjectCondition(valueArea, objective.Subject, objective.Key, objective.Kind == ContentKind.EventCount,
                            (role, key) => Edit("Change Objective Condition", () => { objective.Subject = role; objective.Key = key; objective.Label = ConditionLabel(role, key); }));
                    }
                    else valueArea.Add(new Label(objective.Label));
                    row.tooltip = objective.Subject + "/" + objective.Key;
                    if (objective.Kind == ContentKind.EventCount)
                    {
                        var count = new IntegerField { value = objective.Required, isDelayed = true, name = "objective-count-" + objective.Id, tooltip = "Required event count" };
                        count.style.width = 60;
                        count.RegisterValueChangedCallback(e => Edit("Change Objective Count", () => objective.Required = Math.Max(1, e.newValue)));
                        row.Add(count);
                    }
                    else if (objective.Kind == ContentKind.Fact)
                    {
                        var expected = new Toggle { value = objective.Expected, tooltip = "Required fact value (checked = true)", name = "objective-expected-" + objective.Id };
                        expected.RegisterValueChangedCallback(e => Edit("Change Objective Fact", () => objective.Expected = e.newValue)); row.Add(expected);
                    }
                    IconAction(row, "×", "Remove Objective", () => {
                        if (stage.Objectives.Count == 1) { notice = "A node needs at least one objective. Change the objective or delete the node."; UpdateStatus(); return; }
                        Edit("Remove Objective", () => stage.Objectives.Remove(objective));
                    });
                }
            }
            if (stage.Routes.Count > 0)
            {
                var routes = new Foldout { text = "Routes", value = false }; content.Add(routes);
                var saved = detailSection; detailSection = "Routes"; BuildStages(routes, quest); detailSection = saved;
            }
            BuildExecs(content, stage.FinishExec, "On Complete");
            BuildAuthorProgress();
        }
        private static void BindNodeEntity(QuestStage node, string role)
        {
            void Condition(QuestCondition c) { if(c==null)return; if(c.Kind==ConditionKind.Fact)c.Subject=""; if(c.Children!=null)foreach(var child in c.Children)Condition(child); }
            void Goals(List<QuestContent> goals) { foreach(var goal in goals)if(goal.Kind==ContentKind.Fact||goal.Kind==ContentKind.EventCount)goal.Subject=""; }
            void Actions(List<QuestExec> actions) { foreach(var action in actions){var args=Newtonsoft.Json.Linq.JObject.Parse(action.ArgumentsJson);args.Remove(CapabilityCatalog.TargetKey);action.ArgumentsJson=args.ToString(Newtonsoft.Json.Formatting.None);} }
            Condition(node.OpenWhen); Goals(node.Objectives); Actions(node.BeginExec); Actions(node.FinishExec);
            foreach(var route in node.Routes){Condition(route.AvailableWhen);Goals(route.Objectives);Actions(route.SelectedExec);}
            foreach(var change in node.Switches)Condition(change.When);
            node.EntityRole=role;
        }
        private void BuildAuthorProgress()
        {
            if (!EditorApplication.isPlaying) return;
            inspector.Add(Heading("Progress"));
            live = new Label { name = "quest-live-progress", style = { whiteSpace = WhiteSpace.Normal } };
            inspector.Add(live); UpdateStatus();
        }
        private void ShowStageMenu(QuestDefinition quest)
        {
            Dictionary<string, QuestNodeType> types;
            try { types = QuestNodeCatalog.Read(); }
            catch (Exception error) { notice = error.Message; UpdateStatus(); return; }
            PickCatalog("Node Types", types.Values.Select(template => new QuestCatalogPicker.Entry {
                Id = template.TypeId, Label = template.DisplayName, Choose = () => AddStage(quest, template)
            }));
        }
        private void AddStage(QuestDefinition quest, QuestNodeType template)
        {
            if (EditorApplication.isPlaying) return;
            var node = template ? template.CreateNode() : new QuestStage { Id = "node-" + Guid.NewGuid().ToString("N"), Title = "New Node" };
            if (!template) { int suffix = 2; while (quest.Stages.Any(s => s.Title == node.Title)) node.Title = "New Node " + suffix++; }
            selectedId = quest.Id; selectedStageId = node.Id; showStages = true;
            Edit("Add Node", () => quest.Stages.Add(node));
            stageGraph?.SelectStage(node.Id);
            stageGraph?.schedule.Execute(() => stageGraph?.FrameSelection()).ExecuteLater(60);
        }
    }
}
