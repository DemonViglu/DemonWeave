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
            if (condition.Kind == ConditionKind.StageResult)
                return (book.Quests.FirstOrDefault(q => q.Id == selectedId)?.Stages.FirstOrDefault(n => n.Id == condition.Subject)?.Title ?? condition.Subject) + " / " + ObjectNames.NicifyVariableName(condition.Key);
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
            if (selectedStageId == "$start" || selectedStageId == "$finish")
            {
                bool start = selectedStageId == "$start";
                inspectorHeader.Add(Heading(start ? "Start" : "Finish"));
                CommentField(inspector, start ? quest.StartComment : quest.FinishComment, value => Edit("Edit Comment", () => { if (start) quest.StartComment = value; else quest.FinishComment = value; }));
                return;
            }
            var stage = quest.Stages.FirstOrDefault(s => s.Id == selectedStageId);
            var header = ItemRow(inspectorHeader);
            header.Add(new Label((stage == null ? "Quest / " : "Node / ") + (stage?.Title ?? quest.Title)) { style = { flexGrow = 1, unityFontStyleAndWeight = FontStyle.Bold } });
            IconAction(header, "#", stage == null ? "Copy QuestId" : "Copy NodeId", () => EditorGUIUtility.systemCopyBuffer = stage?.Id ?? quest.Id);
            if (stage != null) IconAction(header, "←", "Quest Properties", () => SelectStage(null));
            IconAction(header, "×", stage == null ? "Delete Quest" : "Delete Node", () => { if (stage == null) RemoveQuest(quest); else DeleteStages(quest, new[] { stage.Id }); }).SetEnabled(!EditorApplication.isPlaying);
            BuildAuthorProgress();
            var content = new VisualElement { style = { paddingTop = 8 } }; content.SetEnabled(!EditorApplication.isPlaying); inspector.Add(content);
            Text(content, "Name", stage?.Title ?? quest.Title, value => Edit("Rename", () => { if (stage == null) quest.Title = value; else stage.Title = value; }));
            if (stage == null)
            {
                var conditions = new Foldout { text = "Activation Conditions", value = false }; content.Add(conditions);
                BuildCondition(conditions, quest.Accept, value => quest.Accept = value, 0);
                BuildExecutionRules(content, quest.RunWhen, value => quest.RunWhen = value, quest.InterruptWhen, value => quest.InterruptWhen = value);
                return;
            }
            if (stage.Control != QuestControl.None)
            {
                content.Add(new Label(stage.Control == QuestControl.All ? "Wait for every connected input." : stage.Control == QuestControl.Any ? "Continue on the first input; other running nodes are not cancelled." : "Activate all connected successors.") { style = { whiteSpace = WhiteSpace.Normal } });
                BuildExecutionRules(content, stage.RunWhen, value => stage.RunWhen = value, stage.InterruptWhen, value => stage.InterruptWhen = value);
                CommentField(content, stage.Comment ?? "", value => Edit("Edit Comment", () => stage.Comment = value));
                return;
            }
            BuildNodeLogic(content, stage);
            BuildExecutionRules(content, stage.RunWhen, value => stage.RunWhen = value, stage.InterruptWhen, value => stage.InterruptWhen = value);
            var noteRow = ItemRow(content);
            CommentField(noteRow, stage.Comment ?? QuestLogicCatalog.Find(stage.LogicId)?.Comment ?? "", value => Edit("Edit Comment", () => stage.Comment = value));
            IconAction(noteRow, "↺", "Use code comment", () => Edit("Reset Comment", () => stage.Comment = null));
        }
        private static void CommentField(VisualElement parent, string value, Action<string> changed)
        {
            var field = new TextField("Comment") { multiline = true, isDelayed = true, value = value ?? "" };
            field.style.flexGrow = 1; field.style.minWidth = 0;
            field.SetEnabled(!EditorApplication.isPlaying);
            field.RegisterValueChangedCallback(evt => changed(evt.newValue)); parent.Add(field);
        }
        private void BuildAuthorProgress()
        {
            if (!EditorApplication.isPlaying) return;
            inspector.Add(Heading("Progress"));
            live = new Label { name = "quest-live-progress", style = { whiteSpace = WhiteSpace.Normal } };
            inspector.Add(live); UpdateStatus();
        }
        private void AddStage(QuestDefinition quest, Vector2? position = null, QuestControl control = QuestControl.None)
        {
            if (EditorApplication.isPlaying) return;
            position ??= stageGraph?.PointerPosition;
            var node = new QuestStage { Id = "node-" + Guid.NewGuid().ToString("N"), Title = "New Node", Required = false, OpenWhen = new QuestCondition { Kind = ConditionKind.Any } };
            { int suffix = 2; while (quest.Stages.Any(s => s.Title == node.Title)) node.Title = "New Node " + suffix++; }
            if (control != QuestControl.None) {
                node.Control = control; node.Results.Add("Next");
                node.Title = control == QuestControl.All ? "AND" : control == QuestControl.Any ? "OR" : "Parallel";
            }
            selectedId = quest.Id; selectedStageId = node.Id; showStages = true;
            Edit("Add Node", () => quest.Stages.Add(node));
            if (position.HasValue) stageGraph?.PlaceNode(node.Id, position.Value);
            if (stageGraph != null) QuestGraphLayoutStore.Save(book, quest.Id, stageGraph.CapturePositions());
            stageGraph?.SelectStage(node.Id);
        }
    }
}
