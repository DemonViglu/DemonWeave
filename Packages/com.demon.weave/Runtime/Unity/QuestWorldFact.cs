using System;
using UnityEngine;
namespace DemonWeave.Unity
{
    [Serializable]
    public sealed class QuestWorldFact
    {
        [SerializeField, HideInInspector] private string id;
        public string Label;
        public bool InitialValue;
        public string Id => id;
        public void EnsureId() { if (string.IsNullOrEmpty(id)) id = Guid.NewGuid().ToString("N"); }
    }
    // Stores identity, not a table row index or a copy of the initial value.
    [Serializable]
    public struct QuestFactReference
    {
        public string Id;
    }
}
