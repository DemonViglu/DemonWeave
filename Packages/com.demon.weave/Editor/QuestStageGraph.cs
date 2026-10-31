using System.Collections.Generic;
using System.Linq;
using DemonWeave.Quests;
using UnityEditor.Experimental.GraphView;
using UnityEngine;
using UnityEngine.UIElements;

namespace DemonWeave.Editor
{
    // A projection of stage dependencies, not a second executable graph.
    internal sealed class QuestStageGraph : GraphView
    {
        private readonly Dictionary<string, Node> stageNodes = new Dictionary<string, Node>();
        private readonly System.Action createNode;
        private readonly System.Action fromTemplate;
        public QuestStageGraph(QuestDefinition quest, System.Action<string> selected = null, System.Func<QuestStage, QuestStage> outcomeSource = null, System.Func<QuestCondition, string> outcomeLabel = null, System.Action<IReadOnlyList<string>> deleted = null, System.Action<string,string> unlinked = null, System.Action<string,string> linked = null, System.Action createNode = null, bool frameOnOpen = true, System.Action fromTemplate = null)
        {
            this.createNode = createNode;
            this.fromTemplate = fromTemplate;
            style.flexGrow = 1;
            QuestEditorTheme.Apply(this);
            SetupZoom(ContentZoomer.DefaultMinScale, ContentZoomer.DefaultMaxScale);
            this.AddManipulator(new ContentDragger()); this.AddManipulator(new SelectionDragger());
            var grid = new GridBackground(); grid.AddToClassList("weave-grid"); Insert(0, grid); grid.StretchToParentSize();
            style.backgroundColor = new Color(.075f, .09f, .12f);
            var nodes = new Dictionary<string, Node>();
            var inputs = new Dictionary<string, Port>(); var outputs = new Dictionary<string, Port>();
            int i = 0, branchRow = 0;
            foreach (var stage in quest.Stages)
            {
                if (nodes.ContainsKey(stage.Id)) continue;
                var node = new Node { title = stage.Title, userData = stage.Id };
                node.titleContainer.style.backgroundColor = new Color(.16f, .21f, .29f);
                node.titleContainer.style.height = 32;
                node.titleContainer.style.fontSize = 17;
                node.extensionContainer.style.backgroundColor = new Color(.10f, .13f, .18f);
                node.extensionContainer.style.paddingLeft = 14; node.extensionContainer.style.paddingRight = 14;
                node.extensionContainer.style.paddingTop = 12; node.extensionContainer.style.paddingBottom = 12;
                node.extensionContainer.style.minHeight = 48;
                node.RegisterCallback<MouseDownEvent>(_ => selected?.Invoke(stage.Id));
                node.style.width = 300;
                void Describe(string text)
                {
                    var label = new Label(text); label.style.whiteSpace = WhiteSpace.Normal;
                    label.style.maxWidth = 280; label.style.marginBottom = 5;
                    label.style.fontSize = 14; label.style.color = new Color(.78f, .83f, .91f);
                    node.extensionContainer.Add(label);
                }
                var input = node.InstantiatePort(Orientation.Horizontal, Direction.Input, Port.Capacity.Multi, typeof(bool));
                var output = node.InstantiatePort(Orientation.Horizontal, Direction.Output, Port.Capacity.Multi, typeof(bool));
                var producer = outcomeSource?.Invoke(stage);
                input.portName = producer == null ? "Ready" : "Condition"; output.portName = "Completed";
                input.userData = output.userData = stage.Id;
                input.portColor = output.portColor = new Color(.35f, .73f, .72f);
                node.inputContainer.Add(input); node.outputContainer.Add(output);
                if (!stage.Required) Describe("Optional");
                if (stage.OpenWhen.Kind != ConditionKind.All || stage.OpenWhen.Children.Count > 0) Describe("Condition: " + outcomeLabel?.Invoke(stage.OpenWhen));
                foreach (var objective in stage.Objectives) Describe("Objective: " + objective.Label);
                foreach (var route in stage.Routes) Describe("Route: " + route.Title);
                foreach (var change in stage.Switches) Describe("Switch: " + change.From + " → " + change.To);
                node.RefreshExpandedState(); node.RefreshPorts();
                node.SetPosition(producer == null ? new Rect(35 + i++ * 330, 80, 300, 220) : new Rect(365, 420 + branchRow++ * 280, 300, 220)); AddElement(node);
                nodes.Add(stage.Id, node); inputs.Add(stage.Id, input); outputs.Add(stage.Id, output);
                stageNodes.Add(stage.Id, node);
            }
            foreach (var stage in quest.Stages)
            {
                var producer = outcomeSource?.Invoke(stage);
                if (producer == null || !nodes.TryGetValue(producer.Id, out var source)) continue;
                var port = source.InstantiatePort(Orientation.Horizontal, Direction.Output, Port.Capacity.Multi, typeof(bool));
                port.portName = outcomeLabel?.Invoke(stage.OpenWhen) ?? "Outputs";
                port.portColor = new Color(1,.65f,.35f); port.pickingMode = PickingMode.Ignore;
                source.outputContainer.Add(port); source.RefreshPorts(); source.RefreshExpandedState();
                var outcomeEdge = port.ConnectTo(inputs[stage.Id]);
                outcomeEdge.capabilities &= ~Capabilities.Deletable;
                outcomeEdge.tooltip = "Condition link. Edit the node condition to change this dependency.";
                AddElement(outcomeEdge);
            }
            foreach (var stage in quest.Stages)
                foreach (var previous in stage.After.Distinct())
                    if (outputs.TryGetValue(previous, out var output) && inputs.TryGetValue(stage.Id, out var input))
                    { var edge = output.ConnectTo(input); edge.userData = new KeyValuePair<string,string>(previous, stage.Id); AddElement(edge); }
            graphViewChanged = change => {
                if (change.edgesToCreate != null)
                {
                    var connections = change.edgesToCreate.Select(e => new KeyValuePair<string,string>(e.output.userData as string, e.input.userData as string)).ToArray();
                    change.edgesToCreate.Clear();
                    if (!UnityEditor.EditorApplication.isPlaying) schedule.Execute(() => { foreach (var pair in connections) if (pair.Key != null && pair.Value != null) linked?.Invoke(pair.Key, pair.Value); });
                }
                if (change.elementsToRemove == null) return change;
                var ids = change.elementsToRemove.OfType<Node>().Select(n => n.userData as string).Where(id => id != null).ToArray();
                var links = change.elementsToRemove.OfType<Edge>().Where(e => e.userData is KeyValuePair<string,string>).Select(e => (KeyValuePair<string,string>)e.userData).ToArray();
                change.elementsToRemove.Clear(); // Only confirmed model edits rebuild the canvas.
                if (!UnityEditor.EditorApplication.isPlaying && ids.Length > 0) schedule.Execute(() => deleted?.Invoke(ids));
                else if (!UnityEditor.EditorApplication.isPlaying && links.Length > 0) schedule.Execute(() => { foreach (var link in links) unlinked?.Invoke(link.Key, link.Value); });
                return change;
            };
            if (frameOnOpen) schedule.Execute(() => FrameAll()).ExecuteLater(50);
        }
        public Dictionary<string, Rect> CapturePositions() => stageNodes.ToDictionary(p => p.Key, p => {
            var node = p.Value;
            var rect = node.GetPosition();
            // SetPosition writes style immediately; layout may still describe the previous frame.
            rect.position = new Vector2(node.style.left.value.value, node.style.top.value.value);
            if (rect.width <= 0) rect.width = 300;
            if (rect.height <= 0) rect.height = 220;
            return rect;
        });
        public void RestorePositions(IReadOnlyDictionary<string, Rect> positions)
        { foreach (var pair in positions) if (stageNodes.TryGetValue(pair.Key, out var node)) node.SetPosition(pair.Value); }
        public void UpdateStates(QuestInstance instance)
        {
            foreach (var pair in stageNodes)
            {
                var state = instance?.Stage(pair.Key)?.State;
                pair.Value.titleContainer.style.backgroundColor = state == QuestStatus.Active ? new Color(.39f,.30f,.14f) : state == QuestStatus.Completed ? new Color(.14f,.32f,.28f) : new Color(.16f,.21f,.29f);
                pair.Value.tooltip = state.HasValue ? QuestAuthoringChecks.Status(state.Value) : "Not running";
            }
        }
        public void SelectStage(string id)
        {
            ClearSelection();
            if (id != null && stageNodes.TryGetValue(id, out var node)) AddToSelection(node);
        }
        public override List<Port> GetCompatiblePorts(Port start, NodeAdapter adapter)
            => ports.Where(p => start.userData is string && p.userData is string && p.direction != start.direction && p.node != start.node).ToList();
        public override void BuildContextualMenu(ContextualMenuPopulateEvent evt)
        {
            base.BuildContextualMenu(evt);
            if (createNode != null) evt.menu.AppendAction("Add Node", _ => createNode(), _ => UnityEditor.EditorApplication.isPlaying ? DropdownMenuAction.Status.Disabled : DropdownMenuAction.Status.Normal);
            if (fromTemplate != null) evt.menu.AppendAction("From Template…", _ => fromTemplate(), _ => UnityEditor.EditorApplication.isPlaying ? DropdownMenuAction.Status.Disabled : DropdownMenuAction.Status.Normal);
        }
    }
}
