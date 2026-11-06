using System;
using System.Collections.Generic;
using UnityEngine;

namespace DemonWeave.Unity
{
    public abstract class QuestEntity : MonoBehaviour
    {
        [SerializeField, HideInInspector] private string entityId;
        public string Id => entityId;
        public virtual string Comment => "";
        public virtual IReadOnlyList<string> Results => Array.Empty<string>();
        public virtual QuestExecution CreateExecution() => throw new InvalidOperationException("This Entity uses legacy method actions.");
        public virtual string CheckConfiguration() => "";
        protected virtual void Reset() { entityId = Guid.NewGuid().ToString("N"); }
        protected virtual void OnValidate() { if (string.IsNullOrEmpty(entityId)) entityId = Guid.NewGuid().ToString("N"); }
    }
}
