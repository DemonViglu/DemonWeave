using System;
using System.Collections.Generic;
using UnityEngine;
namespace DemonWeave.Unity
{
    [CreateAssetMenu(menuName = "DemonWeave/Fact Table", fileName = "WorldFacts")]
    public sealed class QuestFactTable : ScriptableObject
    {
        [SerializeField] private List<QuestWorldFact> entries = new List<QuestWorldFact>();
        public IReadOnlyList<QuestWorldFact> Entries => entries;
#if UNITY_EDITOR
        [SerializeField, HideInInspector] private string generatedCodePath;
        public string GeneratedCodePath { get => generatedCodePath; set => generatedCodePath = value; }
#endif
        private void OnValidate()
        {
            foreach (var entry in entries) entry?.EnsureId();
        }
        public string CheckConfiguration()
        {
            var ids = new HashSet<string>();
            foreach (var entry in entries)
                if (entry == null || string.IsNullOrWhiteSpace(entry.Id) || !ids.Add(entry.Id))
                    return "Fact table contains a missing or duplicate stable ID.";
            return "";
        }
    }
}
