using System;
using System.Collections.Generic;
using UnityEngine;

namespace DemonWeave.Unity
{
    [Serializable]
    public sealed class EntityBinding
    {
        public string Role;
        public QuestEntity Entity;
    }

    public sealed class QuestBindings
    {
        private readonly Dictionary<string, QuestEntity> entities = new Dictionary<string, QuestEntity>(StringComparer.Ordinal);
        public QuestBindings(IEnumerable<EntityBinding> bindings)
        {
            var ids = new Dictionary<string, QuestEntity>();
            foreach (var binding in bindings)
            {
                if (binding == null || string.IsNullOrWhiteSpace(binding.Role) || !binding.Entity)
                    throw new ArgumentException("bindings: every role needs a scene object");
                if (entities.ContainsKey(binding.Role)) throw new ArgumentException("bindings." + binding.Role + ": duplicate role");
                var id = binding.Entity.Id;
                if (string.IsNullOrWhiteSpace(id)) throw new ArgumentException("bindings." + binding.Role + ": missing entity ID");
                if (ids.TryGetValue(id, out var other) && other != binding.Entity)
                    throw new ArgumentException("bindings." + binding.Role + ": duplicate entity ID on different objects");
                entities.Add(binding.Role, binding.Entity); ids[id] = binding.Entity;
            }
        }
        public QuestEntity Find(string role) => role != null && entities.TryGetValue(role, out var entity) && entity ? entity : null;
        public QuestEntity Require(string role) => Find(role) ?? throw new ArgumentException("bindings." + role + ": object missing or destroyed");
    }
}
