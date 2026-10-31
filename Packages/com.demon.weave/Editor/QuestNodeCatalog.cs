using System;
using System.Collections.Generic;
using UnityEditor;
using DemonWeave.Unity;

namespace DemonWeave.Editor
{
    internal static class QuestNodeCatalog
    {
        public static Dictionary<string, QuestNodeType> Read()
        {
            var types = new Dictionary<string, QuestNodeType>(StringComparer.Ordinal);
            foreach (var guid in AssetDatabase.FindAssets("t:QuestNodeType"))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid); var asset = AssetDatabase.LoadAssetAtPath<QuestNodeType>(path);
                if (!asset) continue;
                if (string.IsNullOrWhiteSpace(asset.TypeId)) continue; // A newly created type asset may still be a draft.
                if (asset.TypeId == "core.stage" || types.ContainsKey(asset.TypeId))
                    throw new InvalidOperationException(path + ": Node Type requires a unique TypeId.");
                types.Add(asset.TypeId, asset);
            }
            return types;
        }
    }
}
