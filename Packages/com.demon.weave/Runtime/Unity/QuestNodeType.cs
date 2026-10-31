using System;
using DemonWeave.Quests;
using UnityEngine;

namespace DemonWeave.Unity
{
    // Authoring templates create existing Quest definitions; the runtime never interprets this asset.
    public abstract class QuestNodeType : ScriptableObject
    {
        public string DisplayName = "Node";
        public string TypeId = "";
        protected abstract QuestStage BuildDefinition();
        public QuestStage CreateNode()
        {
            var node = BuildDefinition()?.Copy() ?? throw new InvalidOperationException("Node template returned no definition.");
            if (string.IsNullOrWhiteSpace(TypeId)) throw new InvalidOperationException("Node Type needs a stable TypeId.");
            node.TypeId = TypeId;
            node.Id = "node-" + Guid.NewGuid().ToString("N");
            if (string.IsNullOrWhiteSpace(node.Title)) node.Title = DisplayName;
            return node;
        }
    }
}
