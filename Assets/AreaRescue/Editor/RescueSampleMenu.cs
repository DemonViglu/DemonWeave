using UnityEditor;
using UnityEditor.SceneManagement;
namespace DemonWeave.AreaRescue.Editor
{
    internal static class RescueSampleMenu
    {
        [MenuItem("DemonWeave/Sample Scene")]
        private static void Open()
        {
            if (EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
                EditorSceneManager.OpenScene("Assets/AreaRescue/Scenes/CourtyardRescue.unity");
        }
    }
}
