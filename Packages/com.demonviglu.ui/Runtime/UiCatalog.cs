using System;
using System.Collections.Generic;
using UnityEngine;

namespace DemonViglu.UI
{
    public enum UiLayer
    {
        Hud,
        Screen,
        Modal
    }

    public enum UiStackPolicy
    {
        Normal,
        Stack,
        HideOther
    }

    [CreateAssetMenu(fileName = "UiCatalog", menuName = "DemonViglu/UI Catalog")]
    public sealed class UiCatalog : ScriptableObject
    {
        [Serializable]
        public sealed class Entry
        {
            public Entry() { }
            public Entry(string id, GameObject prefab, UiLayer layer = UiLayer.Screen,
                bool modal = false, bool blocksInput = true, UiStackPolicy policy = UiStackPolicy.Stack)
            { _uiId = id; _prefab = prefab; _layer = layer; _modal = modal; _blocksInput = blocksInput; _stackPolicy = policy; }
            [SerializeField] private string _uiId;
            [SerializeField] private GameObject _prefab;
            [SerializeField] private UiLayer _layer;
            [SerializeField] private bool _modal;
            [SerializeField] private bool _blocksInput;
            [SerializeField] private UiStackPolicy _stackPolicy;

            public string UiId => _uiId;
            public GameObject Prefab => _prefab;
            public UiLayer Layer => _layer;
            public bool Modal => _modal;
            public bool BlocksInput => _blocksInput;
            public UiStackPolicy StackPolicy => _stackPolicy;
        }

        [SerializeField] private Entry[] _entries = Array.Empty<Entry>();

        private Dictionary<string, Entry> _byId;

        public IReadOnlyList<Entry> Entries => _entries;

        public bool TryGet(string uiId, out Entry entry)
        {
            EnsureIndex();
            entry = null;
            return !string.IsNullOrWhiteSpace(uiId) && _byId.TryGetValue(uiId, out entry);
        }

        public bool Validate(out string error)
        {
            EnsureIndex();
            if (_byId.Count != _entries.Length)
            {
                error = "Ui Catalog 包含空白或重复 UiId。";
                return false;
            }

            foreach (var entry in _entries)
            {
                if (entry.Prefab == null)
                {
                    error = $"Ui Catalog 的 {entry.UiId} 未绑定 Prefab。";
                    return false;
                }

                if (entry.Prefab.GetComponentInChildren<UiView>(true) == null)
                {
                    error = $"Ui Catalog 的 {entry.UiId} Prefab 缺少 UiView。";
                    return false;
                }
            }

            error = string.Empty;
            return true;
        }

        private void OnEnable() => _byId = null;
        private void OnValidate() => _byId = null;

        private void EnsureIndex()
        {
            if (_byId != null) return;

            _byId = new Dictionary<string, Entry>(StringComparer.Ordinal);
            foreach (var entry in _entries)
            {
                if (entry == null || string.IsNullOrWhiteSpace(entry.UiId))
                    continue;

                _byId.TryAdd(entry.UiId.Trim(), entry);
            }
        }
    }
}
