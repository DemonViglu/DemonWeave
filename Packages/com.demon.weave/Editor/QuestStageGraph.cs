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
        public QuestStageGraph(QuestDefinition quest, System.Action<string> selected = null, System.Func<QuestStage, QuestStage> outcomeSource = null, System.Func<QuestCondition, string> outcomeLabel = null)
        {
            style.flexGrow = 1;
            SetupZoom(ContentZoomer.DefaultMinScale, ContentZoomer.DefaultMaxScale);
            this.AddManipulator(new ContentDragger()); this.AddManipulator(new SelectionDragger());
            var grid = new GridBackground(); Insert(0, grid); grid.StretchToParentSize();
            style.backgroundColor = new Color(.075f, .09f, .12f);
            var nodes = new Dictionary<string, Node>();
            var inputs = new Dictionary<string, Port>(); var outputs = new Dictionary<string, Port>();
            int i = 0, branchRow = 0;
            foreach (var stage in quest.Stages)
            {
                if (nodes.ContainsKey(stage.Id)) continue;
                var node = new Node { title = (i + 1).ToString("00") + "   " + stage.Title };
                node.titleContainer.style.backgroundColor = new Color(.16f, .21f, .29f);
                node.titleContainer.style.height = 44;
                node.titleContainer.style.fontSize = 17;
                node.extensionContainer.style.backgroundColor = new Color(.10f, .13f, .18f);
                node.extensionContainer.style.paddingLeft = 14; node.extensionContainer.style.paddingRight = 14;
                node.extensionContainer.style.paddingTop = 12; node.extensionContainer.style.paddingBottom = 12;
                node.extensionContainer.style.minHeight = 92;
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
                input.portName = producer == null ? "前置全部完成" : "遭遇结果"; output.portName = "阶段完成";
                input.pickingMode = PickingMode.Ignore; output.pickingMode = PickingMode.Ignore;
                input.portColor = output.portColor = new Color(.35f, .73f, .72f);
                node.inputContainer.Add(input); node.outputContainer.Add(output);
                foreach (var objective in stage.Objectives) Describe("目标 · " + objective.Label);
                foreach (var route in stage.Routes) Describe("路线 · " + route.Title);
                foreach (var change in stage.Switches) Describe("可改选 · " + change.From + " → " + change.To);
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
                port.portName = outcomeLabel?.Invoke(stage.OpenWhen) ?? "结果分支";
                port.portColor = new Color(1,.65f,.35f); port.pickingMode = PickingMode.Ignore;
                source.outputContainer.Add(port); source.RefreshPorts(); source.RefreshExpandedState();
                AddElement(port.ConnectTo(inputs[stage.Id]));
            }
            foreach (var stage in quest.Stages)
                foreach (var previous in stage.After.Distinct())
                    if (outputs.TryGetValue(previous, out var output) && inputs.TryGetValue(stage.Id, out var input)) AddElement(output.ConnectTo(input));
            schedule.Execute(() => FrameAll()).ExecuteLater(50);
        }
        public void UpdateStates(QuestInstance instance)
        {
            foreach (var pair in stageNodes)
            {
                var state = instance?.Stage(pair.Key)?.State;
                pair.Value.titleContainer.style.backgroundColor = state == QuestStatus.Active ? new Color(.39f,.30f,.14f) : state == QuestStatus.Completed ? new Color(.14f,.32f,.28f) : new Color(.16f,.21f,.29f);
                pair.Value.tooltip = state.HasValue ? QuestAuthoringChecks.Status(state.Value) : "尚未运行";
            }
        }
        public void SelectStage(string id)
        {
            ClearSelection();
            if (id != null && stageNodes.TryGetValue(id, out var node)) AddToSelection(node);
        }
    }
}
