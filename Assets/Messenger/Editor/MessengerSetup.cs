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

namespace DemonWeave.Messenger.Editor
{
    public static class MessengerSetup
    {
        public const string ScenePath = "Assets/Messenger/Scenes/MissingEnvoy.unity";
        public const string PrefabPath = "Assets/Messenger/Content/MessengerWorld.prefab";
        private const string Content = "Assets/Messenger/Content/";
        public static List<QuestDefinition> Definitions(bool fallback = true)
        {
            QuestCondition Fact(string role, string key) => new QuestCondition { Kind = ConditionKind.Fact, Subject = role, Key = key };
            QuestContent Goal(string id, string label, string role, string key) => new QuestContent { Id = id, Label = label, Kind = ContentKind.Fact, Subject = role, Key = key };
            return new List<QuestDefinition> { new QuestDefinition {
                Id = "missing-envoy", Title = "The missing envoy", Accept = Fact("contact", "accepted"),
                Fail = new List<QuestContent> { Goal("envoy-dead", "Envoy must survive", "envoy", "dead") },
                Stages = new List<QuestStage> {
                    new QuestStage { Id = "investigate", Title = "Investigate", Objectives = new List<QuestContent> {
                        Goal("clue-a", "Inspect the belongings", "clue-a", "found"), Goal("clue-b", "Confirm the witness account", "clue-b", "found") } },
                    new QuestStage { Id = "rescue", Title = "Rescue", After = new List<string> { "investigate" },
                        Objectives = new List<QuestContent> { Goal("safe", "Envoy actually reaches safety", "envoy", "safe") },
                        Routes = new List<QuestRoute> {
                            new QuestRoute { Id = "negotiate", Title = "Negotiate with the guard", SelectedExec = new List<QuestExec> {
                                new QuestExec { Id = "negotiate-on-choice", CapabilityId = "messenger.negotiate" } } },
                            new QuestRoute { Id = "infiltrate", Title = "Infiltrate after refusal", AvailableWhen = Fact("guard", "rejected"),
                                Objectives = new List<QuestContent> { Goal("hostility", "Guard becomes hostile", "guard", "hostile") },
                                SelectedExec = fallback ? new List<QuestExec> { new QuestExec { Id = "infiltration-consequence", CapabilityId = "messenger.make-hostile" } } : new List<QuestExec>() }
                        }, Switches = fallback ? new List<RouteSwitch> {
                            new RouteSwitch { Id = "refusal-opens-infiltration", From = "negotiate", To = "infiltrate", When = Fact("guard", "rejected") }
                        } : new List<RouteSwitch>() },
                    new QuestStage { Id = "handover", Title = "Safe handover", After = new List<string> { "rescue" },
                        Objectives = new List<QuestContent> { Goal("handover", "Envoy safely delivered to contact", "envoy", "handed-over") } }
                }
            } };
        }
        [MenuItem("DemonWeave/打开失踪使者样本")]
        public static void Open()
        { if (EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) { Build(); EditorSceneManager.OpenScene(ScenePath); } }
        public static void Build()
        {
            if (File.Exists(ScenePath)) return;
            Directory.CreateDirectory(Content); Directory.CreateDirectory("Assets/Messenger/Scenes"); AssetDatabase.Refresh();
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var appObject = new GameObject("Startup · DemonWeave"); var composition = appObject.AddComponent<QuestComposition>(); var app = appObject.AddComponent<AppHost>();
            var serialized = new SerializedObject(app); serialized.FindProperty("composition").objectReferenceValue = composition; serialized.ApplyModifiedPropertiesWithoutUndo();
            Material Mat(string name, Color color) { var material = new Material(Shader.Find("Standard")) { color = color }; AssetDatabase.CreateAsset(material, Content + name + ".mat"); return material; }
            var stone = Mat("Stone", new Color(.4f, .46f, .5f)); var ground = Mat("Ground", new Color(.17f, .24f, .23f));
            var blue = Mat("Blue", new Color(.1f, .35f, .6f)); var gold = Mat("Gold", new Color(.9f, .6f, .2f)); var white = Mat("White", new Color(.8f, .82f, .75f));
            var root = new GameObject("Messenger world"); var host = root.AddComponent<MessengerScenario>();
            GameObject Shape(string name, Vector3 position, Vector3 scale, Material material, PrimitiveType type = PrimitiveType.Cube)
            { var obj = GameObject.CreatePrimitive(type); obj.name = name; obj.transform.SetParent(root.transform); obj.transform.position = position; obj.transform.localScale = scale; obj.GetComponent<Renderer>().sharedMaterial = material; return obj; }
            QuestEntity Actor(string role, string name, Vector3 pos, Vector3 scale, Material material, PrimitiveType type = PrimitiveType.Cube)
            {
                var obj = Shape(name, pos, scale, material, type); var entity = obj.AddComponent<QuestEntity>(); entity.DisplayName = name;
                host.Bindings.Add(new EntityBinding { Role = role, Entity = entity });
                var labelObject = new GameObject("Label"); labelObject.transform.SetParent(root.transform); labelObject.transform.position = pos + Vector3.up * (scale.y + .6f);
                labelObject.transform.rotation = Quaternion.Euler(32, -35, 0);
                var label = labelObject.AddComponent<TextMesh>(); label.text = name.ToUpperInvariant(); label.anchor = TextAnchor.MiddleCenter; label.fontSize = 48; label.characterSize = .06f;
                return entity;
            }
            Shape("Island", new Vector3(0, -.35f, 0), new Vector3(24, .7f, 18), ground);
            Shape("Prison back wall", new Vector3(4, 1.2f, 6), new Vector3(10, 2.4f, .3f), stone);
            Shape("Prison side wall", new Vector3(9, 1.2f, 2), new Vector3(.3f, 2.4f, 8), stone);
            Actor("contact", "Contact notice", new Vector3(-6, .8f, -5), new Vector3(2, 1.6f, .3f), white);
            Actor("clue-a", "Belongings", new Vector3(-4, .45f, .5f), new Vector3(1.3f, .9f, 1.3f), gold);
            Actor("clue-b", "Witness record", new Vector3(-2, .65f, 4), new Vector3(1.2f, 1.3f, .4f), white);
            Actor("guard", "Guard", new Vector3(3, 1, 2), Vector3.one, blue, PrimitiveType.Capsule);
            Actor("envoy", "Envoy", new Vector3(6, 1, 4), Vector3.one, gold, PrimitiveType.Capsule);
            Actor("tunnel", "Tunnel entrance", new Vector3(7, .3f, -2), new Vector3(2, .6f, 2), stone);
            var safe = Shape("Safe area", new Vector3(0, .05f, -4), new Vector3(3, .1f, 3), blue);
            var point = new GameObject("Safe point"); point.transform.SetParent(safe.transform); point.transform.position = new Vector3(0, 1, -4); host.SafePoint = point.transform;
            var book = ScriptableObject.CreateInstance<QuestBookAsset>(); book.Id = "missing-envoy-book"; book.Title = "The missing envoy"; book.Quests = Definitions();
            AssetDatabase.CreateAsset(book, Content + "MissingEnvoy.asset"); host.Book = book;
            var camera = new GameObject("Camera").AddComponent<Camera>(); camera.transform.SetParent(root.transform); camera.tag = "MainCamera";
            camera.transform.position = new Vector3(18, 19, -24); camera.transform.LookAt(Vector3.zero); camera.orthographic = true; camera.orthographicSize = 14;
            camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = new Color(.04f, .07f, .1f); camera.gameObject.AddComponent<AudioListener>();
            var sun = new GameObject("Sun").AddComponent<Light>(); sun.transform.SetParent(root.transform); sun.type = LightType.Directional; sun.intensity = 1.1f; sun.shadows = LightShadows.Soft; sun.transform.rotation = Quaternion.Euler(45, -30, 0);
            RenderSettings.ambientLight = new Color(.4f, .45f, .5f);
            var panel = ScriptableObject.CreateInstance<PanelSettings>(); panel.scaleMode = PanelScaleMode.ScaleWithScreenSize; panel.referenceResolution = new Vector2Int(1440, 900); AssetDatabase.CreateAsset(panel, Content + "JournalPanel.asset");
            var document = new GameObject("Quest journal").AddComponent<UIDocument>(); document.transform.SetParent(root.transform); document.panelSettings = panel;
            var input = root.AddComponent<MessengerInteraction>(); input.Scenario = host; input.Camera = camera; input.Document = document;
            PrefabUtility.SaveAsPrefabAssetAndConnect(root, PrefabPath, InteractionMode.AutomatedAction);
            host.ApplicationHost = app; PrefabUtility.RecordPrefabInstancePropertyModifications(host);
            EditorSceneManager.SaveScene(scene, ScenePath); AssetDatabase.SaveAssets();
        }
    }
}
