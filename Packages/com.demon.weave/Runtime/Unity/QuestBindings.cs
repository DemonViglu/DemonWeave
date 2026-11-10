using System;
using System.Collections.Generic;
using UnityEngine;
namespace DemonWeave.Unity
{
    [Serializable]
    public sealed class ObjectBinding
    {
        public string Role;
        public UnityEngine.Object Object;
        public UnityEngine.Object Target => Object;
    }
    // Per-runner object lookup, not a global service. JSON keeps IDs; logic receives typed fields.
    public sealed class QuestBindings
    {
        private readonly Dictionary<string, UnityEngine.Object> objects = new Dictionary<string, UnityEngine.Object>(StringComparer.Ordinal);
        public GameObject Owner { get; }
        public QuestBindings(IEnumerable<ObjectBinding> bindings, GameObject owner = null)
        {
            Owner = owner;
            foreach (var binding in bindings) {
                if (binding == null || string.IsNullOrWhiteSpace(binding.Role)) throw new ArgumentException("An object binding needs a stable ID.");
                if (objects.ContainsKey(binding.Role)) throw new ArgumentException("Duplicate object binding: " + binding.Role);
                objects.Add(binding.Role, binding.Object);
            }
        }
        public UnityEngine.Object Find(string id) => id != null && objects.TryGetValue(id, out var value) && value ? value : null;
        public T RequireOwner<T>() where T : Component => (T)ComponentOn(Owner, typeof(T), "Quest Runner owner");
        private static Component ComponentOn(GameObject go, Type type, string source)
        {
            if (!go) throw new ArgumentException(source + ": object is unavailable.");
            var matches = go.GetComponents(type);
            if (matches.Length != 1) throw new ArgumentException(source + " / " + go.name + ": expected exactly one " + type.Name + ", found " + matches.Length + ".");
            return matches[0];
        }
        public UnityEngine.Object Require(string id, Type type)
        {
            var value = Find(id);
            if (!value) throw new ArgumentException("Object is missing or destroyed: " + id);
            if (type.IsInstanceOfType(value)) return value;
            if (value is GameObject go && typeof(Component).IsAssignableFrom(type)) return ComponentOn(go, type, id);
            if (value is Component component && type == typeof(GameObject)) return component.gameObject;
            throw new ArgumentException(id + ": expected " + type.Name + ", found " + value.GetType().Name);
        }
    }
}
