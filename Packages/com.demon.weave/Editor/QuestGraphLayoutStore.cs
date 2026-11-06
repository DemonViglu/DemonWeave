using System.Collections.Generic;
using DemonWeave.Unity;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;

namespace DemonWeave.Editor
{
    internal static class QuestGraphLayoutStore
    {
        public static Dictionary<string, Rect> Read(QuestBookAsset book, string graph)
        {
            var property = new SerializedObject(book).FindProperty("editorLayoutJson");
            if (string.IsNullOrWhiteSpace(property.stringValue)) return null;
            var root = JObject.Parse(property.stringValue);
            if (root[graph] is not JObject nodes) return null;
            var result = new Dictionary<string, Rect>();
            foreach (var item in nodes.Properties())
                if (item.Value is JArray xy && xy.Count == 2)
                    result[item.Name] = new Rect((float)xy[0], (float)xy[1], 300, 160);
            return result;
        }
        public static void Save(QuestBookAsset book, string graph, IReadOnlyDictionary<string, Rect> positions)
        {
            if (!book || EditorApplication.isPlaying || positions == null) return;
            var data = new SerializedObject(book);
            var property = data.FindProperty("editorLayoutJson");
            var root = string.IsNullOrWhiteSpace(property.stringValue) ? new JObject() : JObject.Parse(property.stringValue);
            var nodes = new JObject();
            foreach (var pair in positions) nodes[pair.Key] = new JArray(pair.Value.x, pair.Value.y);
            root[graph] = nodes;
            var json = root.ToString(Formatting.Indented);
            if (json == property.stringValue) return;
            book.OnBeforeSerialize(); Undo.RecordObject(book, "Move Quest Nodes");
            property.stringValue = json; data.ApplyModifiedPropertiesWithoutUndo(); EditorUtility.SetDirty(book);
        }
    }
}
