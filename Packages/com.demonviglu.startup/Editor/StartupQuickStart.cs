using UnityEditor;
using UnityEditor.SceneManagement;
namespace DemonViglu.Startup.Editor
{
    public static class StartupQuickStart
    {
        public const string BasicScene = "Packages/com.demonviglu.startup/Samples/Scenes/Startup_Basic.unity";
        [MenuItem("Tools/Startup/Open Basic Example")]
        public static void OpenBasic() => Open(BasicScene);
        [MenuItem("Tools/Startup/Open Loop and Scheduler Example")]
        public static void OpenTiming() => Open("Packages/com.demonviglu.startup/Samples/Scenes/Startup_Minimal.unity");
        private static void Open(string path)
        {
            if (EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) EditorSceneManager.OpenScene(path);
        }
    }
}
