using System.Collections.Generic;
using System.IO;
using DemonViglu.Startup.Unity;
using DemonWeave.Quests;
using DemonWeave.Startup;
using DemonWeave.Unity;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UIElements;

namespace DemonWeave.Lighthouse.Editor
{
    public static class LighthouseSetup
    {
        public const string ScenePath = "Assets/Lighthouse/Scenes/BeforeTheStorm.unity";
        public const string WorldPrefab = "Assets/Lighthouse/Content/LighthouseWorld.prefab";
        private const string Content = "Assets/Lighthouse/Content/";
        [MenuItem("DemonWeave/打开灯塔任务样本")]
        public static void Open()
        { if (EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) EditorSceneManager.OpenScene(ScenePath); }

        public static List<QuestDefinition> Definitions()
        {
            QuestCondition State(string id, QuestStatus state = QuestStatus.Completed) => new QuestCondition { Kind = ConditionKind.QuestState, Subject = id, State = state };
            QuestContent Fact(string id, string role, string key) => new QuestContent { Id = id, Label = key, Kind = ContentKind.Fact, Subject = role, Key = key };
            QuestContent Event(string id, string role) => new QuestContent { Id = id, Label = "Interact with " + role, Kind = ContentKind.EventCount, Subject = role, Key = "interacted" };
            return new List<QuestDefinition>
            {
                new QuestDefinition { Id = "lighthouse", Title = "Before the storm", CancelChildrenOnEnd = true,
                    Accept = new QuestCondition { Kind = ConditionKind.Fact, Subject = "harbour", Key = "accepted" },
                    Finish = new List<QuestContent> { new QuestContent { Id = "report-delivered", Kind = ContentKind.QuestState, Subject = "report" } },
                    Fail = new List<QuestContent> { Fact("storm-arrived", "weather", "storm") } },
                new QuestDefinition { Id = "inspect", ParentId = "lighthouse", Title = "Inspect the tower",
                    Accept = State("lighthouse", QuestStatus.Active), Finish = new List<QuestContent> { Event("inspection", "tower") } },
                new QuestDefinition { Id = "battery", ParentId = "lighthouse", Title = "Borrow the battery",
                    Accept = State("inspect"), Finish = new List<QuestContent> { Fact("battery-installed", "keeper", "batteryInstalled") } },
                new QuestDefinition { Id = "generator", ParentId = "lighthouse", Title = "Repair the generator",
                    Accept = State("inspect"), Finish = new List<QuestContent> { Fact("generator-running", "generator", "running") } },
                new QuestDefinition { Id = "light", ParentId = "lighthouse", Title = "Restore real light",
                    Accept = new QuestCondition { Kind = ConditionKind.All, Children = new List<QuestCondition> {
                        State("inspect"), new QuestCondition { Kind = ConditionKind.Any, Children = new List<QuestCondition> { State("battery"), State("generator") } } } },
                    Finish = new List<QuestContent> { Fact("lamp-on", "tower", "lit") },
                    BeginExec = new List<QuestExec> { new QuestExec { Id = "activate-lamp", CapabilityId = "world.light.set", ArgumentsJson = "{\"TargetRole\":\"tower\",\"Intensity\":5}" } } },
                new QuestDefinition { Id = "report", ParentId = "lighthouse", Title = "Notify the harbour",
                    Accept = State("light"), Finish = new List<QuestContent> { Event("notify", "harbour") },
                    FinishExec = new List<QuestExec> { new QuestExec { Id = "raise-harbour-flag", CapabilityId = "world.transform.raise", ArgumentsJson = "{\"TargetRole\":\"flag\",\"Height\":1}" } } }
            };
        }

        public static void Build()
        {
            if (File.Exists(ScenePath)) return;
            Directory.CreateDirectory(Content); Directory.CreateDirectory("Assets/Lighthouse/Scenes"); AssetDatabase.Refresh();
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var appObject = new GameObject("Startup · DemonWeave"); var composition = appObject.AddComponent<QuestComposition>();
            var app = appObject.AddComponent<AppHost>();
            var appData = new SerializedObject(app); appData.FindProperty("composition").objectReferenceValue = composition; appData.ApplyModifiedPropertiesWithoutUndo();
            var root = new GameObject("Lighthouse world"); var scenario = root.AddComponent<LighthouseScenario>();
            var stone = Material("Stone", new Color(.48f, .55f, .59f));
            var sand = Material("Sand", new Color(.34f, .4f, .36f));
            var white = Material("Tower", new Color(.8f, .83f, .78f));
            var blue = Material("Sea", new Color(.07f, .2f, .27f));
            var red = Material("Red", new Color(.65f, .23f, .13f));
            var brass = Material("Brass", new Color(.85f, .55f, .16f));
            var bulb = Material("Lantern", new Color(.2f, .25f, .27f)); bulb.EnableKeyword("_EMISSION");
            Shape(PrimitiveType.Cube, "Sea", new Vector3(0, -.6f, 0), new Vector3(35, .5f, 28), blue, root.transform);
            Shape(PrimitiveType.Cube, "Island", new Vector3(0, -.2f, 0), new Vector3(23, .6f, 18), sand, root.transform);
            var tower = Entity("Tower A", new Vector3(4, 0, 2), root.transform);
            Shape(PrimitiveType.Cylinder, "Lighthouse", new Vector3(4, 3, 2), new Vector3(2.5f, 3, 2.5f), white, tower.transform);
            Shape(PrimitiveType.Cylinder, "Gallery", new Vector3(4, 6, 2), new Vector3(3.4f, .18f, 3.4f), red, tower.transform);
            var lantern = Shape(PrimitiveType.Sphere, "Light", new Vector3(4, 6.8f, 2), new Vector3(1.5f, 1.5f, 1.5f), bulb, tower.transform);
            var light = lantern.AddComponent<Light>(); light.type = LightType.Point; light.range = 14; light.intensity = 0;
            Label("LIGHTHOUSE", new Vector3(4, 8, 2), tower.transform);
            var keeper = Entity("Keeper", new Vector3(-4, 0, -.5f), root.transform);
            Shape(PrimitiveType.Capsule, "Keeper body", new Vector3(-4, 1, -.5f), Vector3.one, red, keeper.transform);
            Label("KEEPER / BATTERY", new Vector3(-4, 2.5f, -.5f), keeper.transform);
            scenario.Battery = Shape(PrimitiveType.Cube, "Battery", new Vector3(-5.2f, .5f, -.5f), new Vector3(.7f, 1, .7f), brass, root.transform).transform;
            var socket = new GameObject("Battery socket"); socket.transform.SetParent(tower.transform); socket.transform.localPosition = new Vector3(0, .6f, -1.8f);
            var receiver = tower.gameObject.AddComponent<LighthouseReceiver>(); receiver.BatterySocket = socket.transform; receiver.Lamp = light;
            Shape(PrimitiveType.Cube, "Battery platform", new Vector3(4, .1f, .2f), new Vector3(1.2f, .2f, 1.2f), stone, tower.transform);
            var generator = Entity("Generator", new Vector3(-3, 0, 5), root.transform);
            Shape(PrimitiveType.Cube, "Generator housing", new Vector3(-3, .8f, 5), new Vector3(2.6f, 1.6f, 1.5f), stone, generator.transform);
            scenario.GeneratorRotor = Shape(PrimitiveType.Cube, "Rotor", new Vector3(-3, .9f, 4.18f), new Vector3(1.3f, .15f, .15f), brass, generator.transform).transform;
            Label("GENERATOR / REPAIR", new Vector3(-3, 2.4f, 5), generator.transform);
            var harbour = Entity("Harbour notice", new Vector3(-6, 0, -5), root.transform);
            Shape(PrimitiveType.Cube, "Board", new Vector3(-6, 1.1f, -5), new Vector3(2.3f, 1.6f, .3f), white, harbour.transform);
            Label("HARBOUR / ACCEPT & REPORT", new Vector3(-6, 2.5f, -5), harbour.transform);
            var flag = Entity("Harbour flag", new Vector3(-8, 1, -4), root.transform);
            Shape(PrimitiveType.Cube, "Flag", new Vector3(-8, 1.3f, -4), new Vector3(1, .6f, .15f), brass, flag.transform);
            var weather = Entity("Storm marker", new Vector3(8, 0, -4), root.transform);
            Shape(PrimitiveType.Cylinder, "Storm marker", new Vector3(8, .2f, -4), new Vector3(1.4f, .2f, 1.4f), blue, weather.transform);
            Label("STORM / FAIL PATH", new Vector3(8, 1.2f, -4), weather.transform);
            foreach (var binding in new[] { ("tower", tower), ("keeper", keeper), ("generator", generator), ("harbour", harbour), ("flag", flag), ("weather", weather) })
                scenario.Bindings.Add(new EntityBinding { Role = binding.Item1, Entity = binding.Item2 });
            var book = ScriptableObject.CreateInstance<QuestBookAsset>(); book.Id = "before-the-storm"; book.Title = "Before the storm"; book.Quests = Definitions();
            AssetDatabase.CreateAsset(book, Content + "BeforeTheStorm.asset"); scenario.Book = book;
            var camera = new GameObject("Camera").AddComponent<Camera>(); camera.transform.SetParent(root.transform);
            camera.tag = "MainCamera"; camera.transform.position = new Vector3(19, 19, -26); camera.transform.LookAt(new Vector3(0, 2, 0));
            camera.orthographic = true; camera.orthographicSize = 15; camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = new Color(.04f, .08f, .12f);
            camera.gameObject.AddComponent<AudioListener>();
            var sun = new GameObject("Sun").AddComponent<Light>(); sun.transform.SetParent(root.transform); sun.type = LightType.Directional;
            sun.intensity = 1; sun.shadows = LightShadows.Soft; sun.transform.rotation = Quaternion.Euler(45, -30, 0);
            RenderSettings.ambientLight = new Color(.35f, .4f, .48f);
            var panel = ScriptableObject.CreateInstance<PanelSettings>(); panel.scaleMode = PanelScaleMode.ScaleWithScreenSize; panel.referenceResolution = new Vector2Int(1440, 900);
            AssetDatabase.CreateAsset(panel, Content + "LighthousePanel.asset");
            var document = new GameObject("Quest journal").AddComponent<UIDocument>(); document.transform.SetParent(root.transform); document.panelSettings = panel;
            var input = root.AddComponent<LighthouseInteraction>(); input.Scenario = scenario; input.Camera = camera; input.Document = document;
            EnsureAlternativeTower(root);
            PrefabUtility.SaveAsPrefabAssetAndConnect(root, WorldPrefab, InteractionMode.AutomatedAction);
            scenario.ApplicationHost = app; PrefabUtility.RecordPrefabInstancePropertyModifications(scenario);
            EditorSceneManager.SaveScene(scene, ScenePath); AssetDatabase.SaveAssets();
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };
        }
        // Additive sample upgrade only. Existing definitions/bindings/materials are preserved.
        public static void PrepareBindingSample()
        {
            Build();
            var root = PrefabUtility.LoadPrefabContents(WorldPrefab);
            try { EnsureAlternativeTower(root); PrefabUtility.SaveAsPrefabAsset(root, WorldPrefab); }
            finally { PrefabUtility.UnloadPrefabContents(root); }
        }
        private static void EnsureAlternativeTower(GameObject root)
        {
            var entities = root.GetComponentsInChildren<QuestEntity>(true);
            var tower = System.Array.Find(entities, x => x.DisplayName == "Tower A");
            if (!tower) throw new System.InvalidOperationException("Original sample tower missing; do not guess a replacement.");
            var receiver = tower.GetComponent<LighthouseReceiver>();
            if (!receiver)
            {
                var socket = tower.transform.Find("Battery socket"); var lamp = tower.GetComponentInChildren<Light>(true);
                if (!socket || !lamp) throw new System.InvalidOperationException("Original tower socket/lamp missing; sample upgrade stopped.");
                receiver = tower.gameObject.AddComponent<LighthouseReceiver>(); receiver.BatterySocket = socket; receiver.Lamp = lamp;
            }
            if (System.Array.Exists(entities, x => x.DisplayName == "Tower B")) return;
            var stone = AssetDatabase.LoadAssetAtPath<Material>(Content + "Stone.mat");
            var red = AssetDatabase.LoadAssetAtPath<Material>(Content + "Red.mat");
            var bulb = AssetDatabase.LoadAssetAtPath<Material>(Content + "Lantern.mat");
            var position = new Vector3(8, 0, 4.5f);
            var alternate = Entity("Tower B", position, root.transform);
            Shape(PrimitiveType.Cylinder, "Lighthouse B", position + Vector3.up * 2.3f, new Vector3(2, 2.3f, 2), stone, alternate.transform);
            Shape(PrimitiveType.Cylinder, "Gallery B", position + Vector3.up * 4.7f, new Vector3(2.8f, .18f, 2.8f), red, alternate.transform);
            var lantern = Shape(PrimitiveType.Sphere, "Light B", position + Vector3.up * 5.4f, Vector3.one * 1.3f, bulb, alternate.transform);
            var light = lantern.AddComponent<Light>(); light.type = LightType.Point; light.range = 14; light.intensity = 0;
            var anchor = new GameObject("Battery socket"); anchor.transform.SetParent(alternate.transform); anchor.transform.localPosition = new Vector3(0, .6f, -1.6f);
            var target = alternate.gameObject.AddComponent<LighthouseReceiver>(); target.BatterySocket = anchor.transform; target.Lamp = light;
            Shape(PrimitiveType.Cube, "Battery platform B", position + new Vector3(0, .1f, -1.6f), new Vector3(1.2f, .2f, 1.2f), stone, alternate.transform);
            Label("TOWER B / ALTERNATE", position + Vector3.up * 6.5f, alternate.transform);
        }
        private static Material Material(string name, Color color)
        { var material = new Material(Shader.Find("Standard")) { color = color }; AssetDatabase.CreateAsset(material, Content + name + ".mat"); return material; }
        private static QuestEntity Entity(string name, Vector3 position, Transform parent)
        { var obj = new GameObject(name); obj.transform.SetParent(parent); obj.transform.position = position; var entity = obj.AddComponent<QuestEntity>(); entity.DisplayName = name; return entity; }
        private static GameObject Shape(PrimitiveType type, string name, Vector3 position, Vector3 scale, Material material, Transform parent)
        { var obj = GameObject.CreatePrimitive(type); obj.name = name; obj.transform.position = position; obj.transform.localScale = scale; obj.transform.SetParent(parent, true); obj.GetComponent<Renderer>().sharedMaterial = material; return obj; }
        private static void Label(string text, Vector3 position, Transform parent)
        { var obj = new GameObject(text); obj.transform.SetParent(parent); obj.transform.position = position; obj.transform.rotation = Quaternion.Euler(30, -35, 0); var label = obj.AddComponent<TextMesh>(); label.text = text; label.anchor = TextAnchor.MiddleCenter; label.characterSize = .07f; label.fontSize = 48; }
    }
}
