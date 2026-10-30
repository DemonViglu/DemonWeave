using System;
using System.Collections.Generic;

namespace DemonViglu.Dialogue
{
    public sealed class DialogueChoice
    {
        public string Id { get; }
        public string Text { get; }
        public string NextNodeId { get; }
        public string ConditionId { get; }
        public string CommandId { get; }
        public DialogueChoice(string id, string text, string nextNodeId = null, string conditionId = null, string commandId = null)
        {
            Id = DialogueGraph.RequireId(id);
            Text = text ?? string.Empty;
            NextNodeId = nextNodeId == null ? null : DialogueGraph.RequireId(nextNodeId);
            ConditionId = conditionId == null ? null : DialogueGraph.RequireId(conditionId);
            CommandId = commandId == null ? null : DialogueGraph.RequireId(commandId);
        }
    }
    public sealed class DialogueNode
    {
        public string Id { get; }
        public string Text { get; }
        public IReadOnlyList<DialogueChoice> Choices { get; }
        public string NextNodeId { get; }
        public DialogueNode(string id, string text, params DialogueChoice[] choices)
            : this(id, text, null, choices) { }
        public DialogueNode(string id, string text, string nextNodeId, DialogueChoice[] choices)
        {
            Id = DialogueGraph.RequireId(id);
            Text = text ?? string.Empty;
            NextNodeId = nextNodeId == null ? null : DialogueGraph.RequireId(nextNodeId);
            var copy = new List<DialogueChoice>(choices ?? throw new ArgumentNullException(nameof(choices)));
            var ids = new HashSet<string>(StringComparer.Ordinal);
            foreach (var choice in copy)
                if (choice == null || !ids.Add(choice.Id)) throw new ArgumentException("Null or duplicate dialogue choice.");
            Choices = copy.AsReadOnly();
        }
    }
    public sealed class DialogueGraph
    {
        private readonly Dictionary<string, DialogueNode> nodes = new(StringComparer.Ordinal);
        public DialogueNode Start { get; }
        public DialogueGraph(string startNodeId, params DialogueNode[] definitions)
        {
            RequireId(startNodeId);
            if (definitions == null) throw new ArgumentNullException(nameof(definitions));
            foreach (var node in definitions)
            {
                if (node == null || nodes.ContainsKey(node.Id)) throw new ArgumentException("Null or duplicate dialogue node.");
                nodes.Add(node.Id, node);
            }
            if (!nodes.TryGetValue(startNodeId, out var start)) throw new ArgumentException("Start node does not exist.");
            Start = start;
            foreach (var node in nodes.Values)
            {
                if (node.NextNodeId != null && !nodes.ContainsKey(node.NextNodeId))
                    throw new ArgumentException($"Line {node.Id} references missing node {node.NextNodeId}.");
                foreach (var choice in node.Choices)
                    if (choice.NextNodeId != null && !nodes.ContainsKey(choice.NextNodeId))
                        throw new ArgumentException($"Choice {node.Id}/{choice.Id} references missing node {choice.NextNodeId}.");
            }
        }
        internal DialogueNode Get(string id) => nodes[id];
        internal static string RequireId(string id)
        {
            if (string.IsNullOrWhiteSpace(id) || id != id.Trim()) throw new ArgumentException("A nonblank, unpadded stable ID is required.");
            return id;
        }
    }
}
