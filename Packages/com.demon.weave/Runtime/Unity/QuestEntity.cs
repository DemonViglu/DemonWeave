using System;
using UnityEngine;

namespace DemonWeave.Unity
{
    [DisallowMultipleComponent]
    public sealed class QuestEntity : MonoBehaviour
    {
        [SerializeField, HideInInspector] private string entityId;
        public string DisplayName = "World object";
        public string Id => entityId;
        private void Reset() { entityId = Guid.NewGuid().ToString("N"); DisplayName = gameObject.name; }
        private void OnValidate() { if (string.IsNullOrEmpty(entityId)) entityId = Guid.NewGuid().ToString("N"); }
    }
}
