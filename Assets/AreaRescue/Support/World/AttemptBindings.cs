using System;
using System.Collections.Generic;
using UnityEngine;

namespace DemonWeave.World
{
    // One world's attempt owns the dynamic role table. Old producers cannot register into a replacement table.
    public sealed class AttemptBindings : IDisposable
    {
        private readonly Dictionary<string, GameObject> objects = new Dictionary<string, GameObject>(StringComparer.Ordinal);
        public Guid Id { get; } = Guid.NewGuid();
        public bool IsClosed { get; private set; }
        public void Bind(string role, GameObject instance)
        {
            if (IsClosed) throw new ObjectDisposedException(nameof(AttemptBindings));
            if (string.IsNullOrWhiteSpace(role) || !instance) throw new ArgumentException("Role and scene instance required.");
            if (objects.ContainsKey(role)) throw new ArgumentException("Role already bound: " + role);
            objects.Add(role, instance);
        }
        public bool BindGenerated(Guid expectedAttempt, string role, GameObject instance)
        {
            if (IsClosed || expectedAttempt != Id) return false;
            Bind(role, instance); return true;
        }
        public GameObject Find(string role) => !IsClosed && role != null && objects.TryGetValue(role, out var obj) && obj ? obj : null;
        public void Dispose() { IsClosed = true; objects.Clear(); }
    }
}
