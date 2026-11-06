using System;
using UnityEngine;

namespace DemonWeave.Unity
{
    [CreateAssetMenu(menuName = "DemonWeave/World Fact", fileName = "WorldFact")]
    public sealed class QuestWorldFact : ScriptableObject
    {
        [SerializeField, HideInInspector] private string id;
        public string Label;
        public bool InitialValue;
        public string Id => id;
        private void OnValidate() { if (string.IsNullOrEmpty(id)) id = Guid.NewGuid().ToString("N"); }
    }
}
