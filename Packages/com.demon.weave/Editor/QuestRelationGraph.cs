using System;
using System.Collections.Generic;
using System.Linq;
using DemonWeave.Quests;
using DemonWeave.Unity;
using UnityEditor.Experimental.GraphView;
using UnityEngine;
using UnityEngine.UIElements;

namespace DemonWeave.Editor
{
    internal sealed class QuestRelationGraph : GraphView
    {
        private sealed class Card : Node
        {
            public string Id;
            public Port Input, Output;
            public Action<string> Selected;
            public override void OnSelected() { base.OnSelected(); Selected?.Invoke(Id); }
        }
        private sealed class Link { public string To; public QuestCondition Condition; }
        private readonly QuestBookAsset book;
        private readonly Action changed;
        private readonly Action<IReadOnlyList<string>> deleted;
        private readonly Dictionary<string, Card> cards = new Dictionary<string, Card>();
        public QuestRelationGraph(QuestBookAsset book, Action<string> selected, Action changed, bool initialView = true, Action<IReadOnlyList<string>> deleted = null, Action<string> opened = null)
        {
            this.book = book; this.changed = changed; this.deleted = deleted;
            style.flexGrow = 1; style.backgroundColor = new Color(.10f, .12f, .15f);
            SetupZoom(.15f, 1.5f); this.AddManipulator(new ContentDragger()); this.AddManipulator(new SelectionDragger()); this.AddManipulator(new RectangleSelector());
            QuestEditorTheme.Apply(this);
            var grid = new GridBackground(); grid.AddToClassList("weave-grid"); Insert(0, grid); grid.StretchToParentSize();
            var depths = book.Quests.ToDictionary(q => q.Id, q => 0);
            for (int pass = 0; pass < book.Quests.Count; pass++)
                foreach (var q in book.Quests)
                {
                    var parents = QuestAuthoringChecks.Dependencies(q.Accept).Where(c => c.Subject != q.Id && depths.ContainsKey(c.Subject)).ToArray();
                    if (parents.Length > 0) depths[q.Id] = Math.Min(book.Quests.Count, parents.Max(c => depths[c.Subject]) + 1);
                }
            var rows = new Dictionary<int, int>();
            foreach (var quest in book.Quests)
            {
                var card = new Card { Id = quest.Id, title = quest.Title, Selected = selected };
                card.tooltip = "Double-click to open this Quest";
                card.RegisterCallback<MouseDownEvent>(evt => {
                    if (evt.button != 0 || evt.clickCount != 2) return;
                    evt.StopPropagation(); schedule.Execute(() => opened?.Invoke(quest.Id));
                }, TrickleDown.TrickleDown);
                card.style.width = 270;
                card.titleContainer.Q<Label>().style.fontSize = 18;
                card.Input = card.InstantiatePort(Orientation.Horizontal, Direction.Input, Port.Capacity.Multi, typeof(bool)); card.Input.portName = "Activation Conditions";
                card.Output = card.InstantiatePort(Orientation.Horizontal, Direction.Output, Port.Capacity.Multi, typeof(bool)); card.Output.portName = "State";
                card.inputContainer.Add(card.Input); card.outputContainer.Add(card.Output);
                var text = new Label(QuestBookAsset.IsDraft(quest) ? "Draft" : QuestAuthoringChecks.ConditionText(quest.Accept, Title)) { style = { whiteSpace = WhiteSpace.Normal, maxWidth = 250, paddingLeft = 8, paddingRight = 8, paddingBottom = 8 } };
                card.extensionContainer.Add(text);
                if (!string.IsNullOrEmpty(quest.ParentId)) card.extensionContainer.Add(new Label("Parent: " + Title(quest.ParentId)));
                card.RefreshExpandedState(); card.RefreshPorts();
                int depth = depths[quest.Id]; int row = rows.TryGetValue(depth, out var existing) ? existing : 0; rows[depth] = row + 1;
                card.SetPosition(new Rect(50 + depth * 330, 60 + row * 230, 270, 170));
                cards.Add(quest.Id, card); AddElement(card);
            }
            foreach (var quest in book.Quests)
                foreach (var condition in QuestAuthoringChecks.Dependencies(quest.Accept))
                {
                    if (!cards.TryGetValue(condition.Subject, out var source)) continue;
                    var edge = source.Output.ConnectTo(cards[quest.Id].Input);
                    edge.userData = new Link { To = quest.Id, Condition = condition };
                    edge.tooltip = "Requires: " + Title(condition.Subject) + " / " + QuestAuthoringChecks.Status(condition.State) + ". See Quest properties for combination rules.";
                    AddElement(edge);
                }
            graphViewChanged = OnChanged;
            if (initialView) schedule.Execute(() => { FrameAll(); if (viewTransform.scale.x < .65f) UpdateViewTransform(new Vector3(25, 80, 0), Vector3.one * .65f); }).ExecuteLater(100);
        }
        public Dictionary<string, Rect> CapturePositions()
        {
            var positions = cards.ToDictionary(x => x.Key, x => x.Value.GetPosition());
            // Before the first layout pass GraphView returns zero rects. Never replace the authored view with them.
            return positions.Count > 1 && positions.Values.Select(p => p.position).Distinct().Count() < 2 ? null : positions;
        }
        public void RestorePositions(Dictionary<string, Rect> positions)
        { foreach (var entry in positions) if (cards.TryGetValue(entry.Key, out var card)) card.SetPosition(entry.Value); }
        private string Title(string id) => book.Quests.FirstOrDefault(q => q.Id == id)?.Title ?? id;
        public override List<Port> GetCompatiblePorts(Port start, NodeAdapter adapter) => ports.Where(p => p.direction != start.direction && p.node != start.node).ToList();
        private GraphViewChange OnChanged(GraphViewChange change)
        {
            if (UnityEditor.EditorApplication.isPlaying) { change.edgesToCreate?.Clear(); change.elementsToRemove?.Clear(); return change; }
            var removedTasks = change.elementsToRemove?.OfType<Card>().Select(c => c.Id).ToArray();
            if (removedTasks != null && removedTasks.Length > 0)
            {
                change.elementsToRemove.Clear();
                schedule.Execute(() => deleted?.Invoke(removedTasks)); return change;
            }
            bool modified = false;
            if (change.edgesToCreate != null)
            {
                foreach (var edge in change.edgesToCreate)
                {
                    var from = ((Card)edge.output.node).Id; var to = ((Card)edge.input.node).Id;
                    var quest = book.Quests.First(q => q.Id == to);
                    if (QuestAuthoringChecks.Dependencies(quest.Accept).Any(c => c.Subject == from && c.State == QuestStatus.Completed)) continue;
                    QuestAuthoringChecks.Edit(book, "Add Quest Dependency", _ => {
                        if (quest.Accept.Kind != ConditionKind.All) quest.Accept = new QuestCondition { Kind = ConditionKind.All, Children = new List<QuestCondition> { quest.Accept } };
                        quest.Accept.Children.Add(new QuestCondition { Kind = ConditionKind.QuestState, Subject = from, State = QuestStatus.Completed });
                    }); modified = true;
                }
                change.edgesToCreate.Clear();
            }
            if (change.elementsToRemove != null)
                foreach (var edge in change.elementsToRemove.OfType<Edge>())
                    if (edge.userData is Link link)
                    { QuestAuthoringChecks.Edit(book, "Remove Quest Dependency", _ => QuestAuthoringChecks.RemoveCondition(book.Quests.First(q => q.Id == link.To), link.Condition)); modified = true; }
            if (modified) schedule.Execute(changed).ExecuteLater(1);
            return change;
        }
        public void UpdateStates(QuestRun run)
        {
            foreach (var entry in cards)
            {
                var state = run?.Find(entry.Key)?.State;
                entry.Value.titleContainer.style.backgroundColor = state == QuestStatus.Completed ? new Color(.15f, .38f, .27f)
                    : state == QuestStatus.Active ? new Color(.16f, .34f, .52f) : state == QuestStatus.Failed ? new Color(.5f, .18f, .18f) : new Color(.2f, .23f, .28f);
            }
        }
    }
}
