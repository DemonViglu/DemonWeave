using DemonWeave.Editor;
using System.Linq;
using DemonWeave.Unity;
using UnityEditor;
using UnityEngine;

namespace DemonWeave.AreaRescue.Editor
{
    [CustomEditor(typeof(AreaRescueScenario))]
    public sealed class AreaRescueScenarioEditor : UnityEditor.Editor
    {
        private bool implementation;
        public override void OnInspectorGUI()
        {
            var host = (AreaRescueScenario)target;
            serializedObject.Update();
            EditorGUILayout.LabelField("区域营救 · 任务配置", EditorStyles.boldLabel);
            using (new EditorGUI.DisabledScope(EditorApplication.isPlaying))
            {
                EditorGUILayout.PropertyField(serializedObject.FindProperty("Book"), new GUIContent("任务定义"));
                if (GUILayout.Button("打开任务阶段与目标")) QuestEditorWindow.Open(host.Book, host);
                EditorGUILayout.Space(); EditorGUILayout.LabelField("场景对象槽位", EditorStyles.boldLabel);
                var bindings = serializedObject.FindProperty("Bindings");
                for (int i = 0; i < bindings.arraySize; i++)
                {
                    var binding = bindings.GetArrayElementAtIndex(i); var role = binding.FindPropertyRelative("Role").stringValue;
                    var label = role == host.ZoneRole ? "进入区域" : role == host.DoorRole ? "Timeline 控制的门" : role == host.EncounterRole ? "遭遇宿主" : role;
                    EditorGUILayout.PropertyField(binding.FindPropertyRelative("Entity"), new GUIContent(label));
                }
                EditorGUILayout.Space(); EditorGUILayout.LabelField("本次遭遇", EditorStyles.boldLabel);
                EditorGUILayout.PropertyField(serializedObject.FindProperty("Encounter"), new GUIContent("生成配置"));
                if (host.Encounter)
                {
                    var encounter = new SerializedObject(host.Encounter); encounter.Update();
                    EditorGUILayout.PropertyField(encounter.FindProperty("EnemyCount"), new GUIContent("必需敌人数"));
                    EditorGUILayout.PropertyField(encounter.FindProperty("EnemyPrefab"), new GUIContent("敌人 Prefab"));
                    EditorGUILayout.PropertyField(encounter.FindProperty("CompanionPrefab"), new GUIContent("被捕同伴 Prefab"));
                    encounter.ApplyModifiedProperties();
                    var error = host.Encounter.Validate(host.Spawner ? host.Spawner.SpawnPoints?.Length ?? 0 : 0);
                    if (!string.IsNullOrEmpty(error)) EditorGUILayout.HelpBox(error, MessageType.Error);
                }
                if (host.Spawner && GUILayout.Button("调整敌人和同伴出生点")) Selection.activeObject = host.Spawner;
                EditorGUILayout.Space(); EditorGUILayout.LabelField("开门演出", EditorStyles.boldLabel);
                EditorGUILayout.PropertyField(serializedObject.FindProperty("Opening"), new GUIContent("Timeline 资产"));
                if (host.Opening && GUILayout.Button("编辑 Timeline 时间轴")) AssetDatabase.OpenAsset(host.Opening);
                EditorGUILayout.HelpBox("Timeline 控制开门与释放信号；任务只等待准备结果。死亡恢复到本次 Play 启动时的玩家位置、门状态及未进入任务。", MessageType.None);
                if (GUILayout.Button("在任务中编排遭遇结果与战败分支")) QuestEditorWindow.Open(host.Book, host);
                implementation = EditorGUILayout.Foldout(implementation, "高级装配与逻辑角色（通常无需修改）", true);
                if (implementation)
                    foreach (var name in new[] { "ApplicationHost", "Player", "Spawner", "Timeline", "DoorTrack", "ReleaseSignal", "DoorRole", "ZoneRole", "EncounterRole" }) EditorGUILayout.PropertyField(serializedObject.FindProperty(name));
            }
            serializedObject.ApplyModifiedProperties();
            var doorError = host.ValidateDoorBinding();
            if (!string.IsNullOrEmpty(doorError))
            {
                EditorGUILayout.HelpBox(doorError, MessageType.Error);
                var door = host.Bindings?.FirstOrDefault(b => b != null && b.Role == host.DoorRole)?.Entity;
                if (door)
                {
                    if (GUILayout.Button("定位当前绑定的门")) EditorGUIUtility.PingObject(door.gameObject);
                    using (new EditorGUI.DisabledScope(EditorApplication.isPlaying))
                        if (GUILayout.Button("为当前绑定的门添加 Animator")) Undo.AddComponent<Animator>(door.gameObject);
                }
            }
            if (EditorApplication.isPlaying)
            {
                EditorGUILayout.Space(); EditorGUILayout.LabelField("本次运行", EditorStyles.boldLabel);
                EditorGUILayout.LabelField("恢复次数", host.RecoveryCount.ToString());
                EditorGUILayout.LabelField("已确认死亡", (host.Spawner?.Attempt?.ConfirmedDeaths ?? 0).ToString());
                using (new EditorGUI.DisabledScope(true)) EditorGUILayout.ObjectField("本次生成的同伴", host.Companion, typeof(GameObject), true);
                EditorGUILayout.HelpBox(host.Feedback, MessageType.Info);
            }
        }
        public static void SelectHost()
        {
            var host = Object.FindAnyObjectByType<AreaRescueScenario>();
            if (host) { Selection.activeObject = host; QuestEditorWindow.Open(host.Book, host); }
            else { var book = AssetDatabase.LoadAssetAtPath<QuestBookAsset>("Assets/AreaRescue/Content/RescueQuest.asset"); if (book) QuestEditorWindow.Open(book); }
        }
    }
}
