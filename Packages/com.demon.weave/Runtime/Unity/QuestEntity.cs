using System;
using UnityEngine;

namespace DemonWeave.Unity
{
    [DisallowMultipleComponent]
    public abstract class QuestEntity : MonoBehaviour
    {
        [SerializeField, HideInInspector] private string entityId;
        public string Id => entityId;
        protected virtual void Reset() { entityId = Guid.NewGuid().ToString("N"); }
        protected virtual void OnValidate() { if (string.IsNullOrEmpty(entityId)) entityId = Guid.NewGuid().ToString("N"); }
    }
}
