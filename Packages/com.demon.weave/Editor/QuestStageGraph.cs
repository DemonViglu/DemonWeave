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
        public QuestStageGraph(QuestDefinition quest)
        {
            style.flexGrow = 1;
            SetupZoom(ContentZoomer.DefaultMinScale, ContentZoomer.DefaultMaxScale);
            this.AddManipulator(new ContentDragger()); this.AddManipulator(new SelectionDragger());
            Insert(0, new GridBackground());
            var nodes = new Dictionary<string, Node>();
            var inputs = new Dictionary<string, Port>(); var outputs = new Dictionary<string, Port>();
            int i = 0;
            foreach (var stage in quest.Stages)
            {
                if (nodes.ContainsKey(stage.Id)) continue;
                var node = new Node { title = stage.Title };
                node.style.width = 300;
                void Describe(string text)
                {
                    var label = new Label(text); label.style.whiteSpace = WhiteSpace.Normal;
                    label.style.maxWidth = 280; label.style.marginBottom = 5;
                    node.extensionContainer.Add(label);
                }
                var input = node.InstantiatePort(Orientation.Horizontal, Direction.Input, Port.Capacity.Multi, typeof(bool));
                var output = node.InstantiatePort(Orientation.Horizontal, Direction.Output, Port.Capacity.Multi, typeof(bool));
                input.portName = "前置全部完成"; output.portName = "阶段完成";
                input.SetEnabled(false); output.SetEnabled(false);
                node.inputContainer.Add(input); node.outputContainer.Add(output);
                foreach (var objective in stage.Objectives) Describe("目标 · " + objective.Label);
                foreach (var route in stage.Routes) Describe("路线 · " + route.Title);
                foreach (var change in stage.Switches) Describe("可改选 · " + change.From + " → " + change.To);
                node.RefreshExpandedState(); node.RefreshPorts();
                node.SetPosition(new Rect(35 + i++ * 330, 80, 300, 220)); AddElement(node);
                nodes.Add(stage.Id, node); inputs.Add(stage.Id, input); outputs.Add(stage.Id, output);
            }
            foreach (var stage in quest.Stages)
                foreach (var previous in stage.After.Distinct())
                    if (outputs.TryGetValue(previous, out var output) && inputs.TryGetValue(stage.Id, out var input)) AddElement(output.ConnectTo(input));
            schedule.Execute(() => FrameAll()).ExecuteLater(50);
        }
    }
}
