using UnityEditor;
using UnityEngine.UIElements;

namespace DemonWeave.Editor
{
    internal static class QuestEditorTheme
    {
        public static void Apply(VisualElement element)
        {
            var sheet = AssetDatabase.LoadAssetAtPath<StyleSheet>("Packages/com.demon.weave/Editor/Styles/Weave.uss");
            if (sheet != null) element.styleSheets.Add(sheet);
        }
    }
}
