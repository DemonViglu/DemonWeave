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
        private readonly System.Action<QuestControl, Vector2> createControl;
        private readonly System.Action<string, Vector2> createAt;
        private readonly Dictionary<string, Node> guides = new Dictionary<string, Node>();
        public Vector2 PointerPosition { get; private set; } = new Vector2(60, 200);
        public QuestStageGraph(QuestDefinition quest, System.Action<string> selected = null, System.Func<QuestStage, QuestStage> outcomeSource = null, System.Func<QuestCondition, string> outcomeLabel = null, System.Action<IReadOnlyList<string>> deleted = null, System.Action<string,string> unlinked = null, System.Action<string,string> linked = null, System.Action createNode = null, bool frameOnOpen = true, System.Func<QuestStage, QuestExec, string> actionLabel = null, System.Action<string, Vector2> createAt = null, System.Action<QuestStage> opened = null, System.Func<QuestStage, string> comment = null, System.Action<IReadOnlyDictionary<string, Rect>> moved = null, System.Action<QuestControl, Vector2> createControl = null)
        {
            this.createAt = createAt; this.createControl = createControl;
            this.createNode = createNode;
            RegisterCallback<MouseMoveEvent>(evt => PointerPosition = contentViewContainer.WorldToLocal(evt.mousePosition));
            style.flexGrow = 1;
            QuestEditorTheme.Apply(this);
            SetupZoom(ContentZoomer.DefaultMinScale, ContentZoomer.DefaultMaxScale);
            var pan = new ContentDragger();
            pan.activators.Clear();
            pan.activators.Add(new ManipulatorActivationFilter { button = MouseButton.MiddleMouse });
            this.AddManipulator(pan); this.AddManipulator(new QuestGraphPan()); this.AddManipulator(new SelectionDragger()); this.AddManipulator(new RectangleSelector());
            var grid = new GridBackground(); grid.AddToClassList("weave-grid"); Insert(0, grid); grid.StretchToParentSize();
            style.backgroundColor = new Color(.075f, .09f, .12f);
            var nodes = new Dictionary<string, Node>();
            var resultPorts = new Dictionary<string, Port>();
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
                node.tooltip = "Double-click to open Entity code";
                node.RegisterCallback<MouseDownEvent>(evt => {
                    if (evt.button != 0) return;
                    selected?.Invoke(stage.Id);
                    if (evt.clickCount == 2) { opened?.Invoke(stage); evt.StopPropagation(); }
                });
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
                output.tooltip = "Drag to another node to set its prerequisite.";
                var producer = outcomeSource?.Invoke(stage);
                input.portName = stage.Control == QuestControl.All ? "All inputs" : stage.Control == QuestControl.Any ? "Any input" : producer == null ? "Ready" : "Condition"; output.portName = "Completed";
                input.userData = output.userData = stage.Id;
                input.portColor = output.portColor = new Color(.35f, .73f, .72f);
                node.inputContainer.Add(input);
                if (stage.Results.Count == 0) node.outputContainer.Add(output);
                foreach (var result in stage.Results) {
                    var port = node.InstantiatePort(Orientation.Horizontal, Direction.Output, Port.Capacity.Multi, typeof(bool));
                    port.portName = UnityEditor.ObjectNames.NicifyVariableName(result); port.userData = ResultPort(stage.Id, result);
                    node.outputContainer.Add(port); resultPorts.Add((string)port.userData, port);
                    if (result == stage.Results[0]) output = port;
                }
                var note = comment?.Invoke(stage) ?? stage.Comment;
                if (!string.IsNullOrWhiteSpace(note)) Describe(note);
                else node.extensionContainer.style.display = DisplayStyle.None;
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
                port.portName = "Starts monitoring"; port.tooltip = outcomeLabel?.Invoke(stage.OpenWhen);
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
            foreach (var target in quest.Stages)
                foreach (var rule in ResultConditions(target.OpenWhen)) {
                    var key = ResultPort(rule.Subject, rule.Key);
                    if (!resultPorts.TryGetValue(key, out var port)) continue;
                    var edge = port.ConnectTo(inputs[target.Id]); edge.userData = new KeyValuePair<string, string>(key, target.Id); AddElement(edge);
                }
            Node Guide(string title, Vector2 position, string description)
            {
                var guide = new Node { title = title, name = "guide-" + title.ToLowerInvariant() };
                guide.capabilities = Capabilities.Movable | Capabilities.Selectable;
                guides.Add("$guide:" + title, guide);
                guide.RegisterCallback<MouseDownEvent>(evt => { if (evt.button == 0 && evt.clickCount == 2) selected?.Invoke(title == "Start" ? "$start" : "$finish"); });
                guide.style.width = 240;
                guide.titleContainer.style.height = 32;
                guide.titleContainer.style.fontSize = 17;
                guide.titleContainer.style.backgroundColor = new Color(.16f, .21f, .29f);
                guide.extensionContainer.style.backgroundColor = new Color(.10f, .13f, .18f);
                guide.extensionContainer.style.paddingTop = 10;
                guide.extensionContainer.style.paddingBottom = 10;
                guide.extensionContainer.style.fontSize = 14;
                if (!string.IsNullOrEmpty(description)) guide.extensionContainer.Add(new Label(description) {
                    style = { whiteSpace = WhiteSpace.Normal, paddingLeft = 10, paddingRight = 10 }
                });
                if (string.IsNullOrWhiteSpace(description)) guide.extensionContainer.style.display = DisplayStyle.None;
                guide.RefreshExpandedState(); guide.SetPosition(new Rect(position, new Vector2(210, 100))); AddElement(guide);
                return guide;
            }
            var start = Guide("Start", new Vector2(-245, 80), quest.StartComment);
            var finish = Guide("Finish", new Vector2(quest.Stages.Count == 0 ? 360 : 35 + i * 330, 80), quest.FinishComment);
            var startPort = start.InstantiatePort(Orientation.Horizontal, Direction.Output, Port.Capacity.Multi, typeof(bool));
            startPort.portName = "Start"; startPort.userData = "$start";
            startPort.tooltip = "Connect to remove node prerequisites. Additional conditions still apply.";
            start.outputContainer.Add(startPort); start.RefreshPorts();
            var finishPort = finish.InstantiatePort(Orientation.Horizontal, Direction.Input, Port.Capacity.Multi, typeof(bool));
            finishPort.portName = "Finish"; finishPort.userData = "$finish";
            finishPort.tooltip = "Connected nodes are required for Quest completion.";
            finish.inputContainer.Add(finishPort); finish.RefreshPorts();
            foreach (var stage in quest.Stages)
            {
                if (stage.After.Count == 0 && !ResultConditions(stage.OpenWhen).Any() && !EntryBlocked(stage.OpenWhen))
                {
                    var edge = startPort.ConnectTo(inputs[stage.Id]); edge.userData = new KeyValuePair<string, string>("$start", stage.Id);
                    edge.tooltip = "Entry node. Alt-click to disconnect."; AddElement(edge);
                }
                if (stage.Required)
                {
                    var key = string.IsNullOrEmpty(stage.FinishResult) ? stage.Id : ResultPort(stage.Id, stage.FinishResult);
                    var source = resultPorts.TryGetValue(key, out var resultPort) ? resultPort : outputs[stage.Id];
                    var edge = source.ConnectTo(finishPort);
                    edge.userData = new KeyValuePair<string, string>(key, "$finish"); AddElement(edge);
                }
            }
            foreach (var edge in edges.ToList())
            {
                if (edge.userData is not KeyValuePair<string, string> link) continue;
                edge.RegisterCallback<MouseDownEvent>(evt => {
                    if (evt.button != 0 || !evt.altKey || UnityEditor.EditorApplication.isPlaying) return;
                    evt.StopImmediatePropagation();
                    schedule.Execute(() => unlinked?.Invoke(link.Key, link.Value));
                }, TrickleDown.TrickleDown);
            }
            graphViewChanged = change => {
                if (change.movedElements?.Count > 0) moved?.Invoke(CapturePositions());
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
            if (frameOnOpen) UpdateViewTransform(new Vector3(300, 80), Vector3.one);
        }
        internal static string ResultPort(string stage, string result) => "$result:" + System.Uri.EscapeDataString(stage) + "/" + System.Uri.EscapeDataString(result);
        internal static bool ParseResultPort(string key, out string stage, out string result)
        {
            stage = result = "";
            if (!key.StartsWith("$result:")) return false;
            var split = key.Substring(8).Split('/');
            if (split.Length != 2) return false;
            stage = System.Uri.UnescapeDataString(split[0]); result = System.Uri.UnescapeDataString(split[1]); return true;
        }
        internal static IEnumerable<QuestCondition> ResultConditions(QuestCondition rule)
        {
            if (rule == null) yield break;
            if (rule.Kind == ConditionKind.StageResult) yield return rule;
            foreach (var child in rule.Children) foreach (var result in ResultConditions(child)) yield return result;
        }
        internal static bool EntryBlocked(QuestCondition rule)
            => rule.Kind == ConditionKind.Any && rule.Children.Count == 0 || rule.Kind == ConditionKind.All && rule.Children.Any(EntryBlocked);
        public void PlaceNode(string id, Vector2 position)
        {
            if (stageNodes.TryGetValue(id, out var node))
            {
                var rect = node.GetPosition(); rect.position = position; node.SetPosition(rect);
            }
        }

        public Dictionary<string, Rect> CapturePositions() => stageNodes.Concat(guides).ToDictionary(p => p.Key, p => {
            var node = p.Value;
            var rect = node.GetPosition();
            // SetPosition writes style immediately; layout may still describe the previous frame.
            rect.position = new Vector2(node.style.left.value.value, node.style.top.value.value);
            if (rect.width <= 0) rect.width = 300;
            if (rect.height <= 0) rect.height = 220;
            return rect;
        });
        public void RestorePositions(IReadOnlyDictionary<string, Rect> positions)
        { foreach (var pair in positions) if (stageNodes.TryGetValue(pair.Key, out var node) || guides.TryGetValue(pair.Key, out node)) { var rect = node.GetPosition(); rect.position = pair.Value.position; node.SetPosition(rect); } }
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
            => ports.Where(p => start.userData is string && p.userData is string && p.direction != start.direction && p.node != start.node && !(p.userData as string == "$finish" && start.userData as string == "$start") && !(p.userData as string == "$start" && start.userData as string == "$finish")).ToList();
        public override void BuildContextualMenu(ContextualMenuPopulateEvent evt)
        {
            base.BuildContextualMenu(evt);
            var point = contentViewContainer.WorldToLocal(evt.mousePosition);
            if (createControl != null)
                foreach (var control in new[] { QuestControl.Parallel, QuestControl.All, QuestControl.Any }) {
                    var choice = control;
                    evt.menu.AppendAction("Add Control/" + (control == QuestControl.All ? "AND" : control == QuestControl.Any ? "OR" : "Parallel"),
                        _ => createControl(choice, point), _ => UnityEditor.EditorApplication.isPlaying ? DropdownMenuAction.Status.Disabled : DropdownMenuAction.Status.Normal);
                }
            if (createNode != null) evt.menu.AppendAction("Add Node", _ => { if (createAt != null) createAt(null, point); else createNode(); }, _ => UnityEditor.EditorApplication.isPlaying ? DropdownMenuAction.Status.Disabled : DropdownMenuAction.Status.Normal);
        }
    }
}
