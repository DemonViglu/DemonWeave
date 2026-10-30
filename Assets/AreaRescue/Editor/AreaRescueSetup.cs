using System.IO;
using DemonViglu.Dialogue.Unity;
using DemonViglu.Startup.Unity;
using DemonViglu.UI;
using DemonWeave.Startup;
using DemonWeave.Unity;
using DemonWeave.World;
using DemonWeave.Presentation;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.Playables;
using UnityEngine.Timeline;
using UnityEngine.UI;

namespace DemonWeave.AreaRescue.Editor
{
    public static class AreaRescueSetup
    {
        public const string ScenePath = "Assets/AreaRescue/Scenes/CourtyardRescue.unity";
        private const string Folder = "Assets/AreaRescue/Content/";
        [MenuItem("DemonWeave/示例场景")]
        public static void Open()
        { if (EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) { Build(); EditorSceneManager.OpenScene(ScenePath); } }
        public static void Build()
        {
            if (File.Exists(ScenePath)) return;
            Directory.CreateDirectory(Folder); Directory.CreateDirectory("Assets/AreaRescue/Scenes"); AssetDatabase.Refresh();
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            Material Material(string name, Color color) { var m = new Material(Shader.Find("Standard")) { color = color }; AssetDatabase.CreateAsset(m, Folder + name + ".mat"); return m; }
            var stone = Material("Stone", new Color(.33f, .39f, .43f)); var floor = Material("Courtyard", new Color(.18f, .25f, .25f));
            var gold = Material("Companion", new Color(.95f, .65f, .2f)); var red = Material("Guard", new Color(.7f, .12f, .1f)); var blue = Material("Player", new Color(.12f, .42f, .8f));
            GameObject Shape(string name, Vector3 pos, Vector3 scale, Material mat, PrimitiveType type = PrimitiveType.Cube)
            {
                var obj = GameObject.CreatePrimitive(type); obj.name = name; obj.transform.position = pos; obj.transform.localScale = scale; obj.GetComponent<Renderer>().sharedMaterial = mat; return obj;
            }
            Shape("Courtyard foundation", new Vector3(0, -.5f, 3), new Vector3(26, 1, 28), floor);
            Shape("West gate wall", new Vector3(-7, 2, 3), new Vector3(10, 4, .6f), stone);
            Shape("East gate wall", new Vector3(7, 2, 3), new Vector3(10, 4, .6f), stone);
            Shape("Rear prison wall", new Vector3(0, 2, 15), new Vector3(24, 4, .6f), stone);
            Shape("West boundary", new Vector3(-12, 1.5f, 6), new Vector3(.5f, 3, 18), stone);
            Shape("East boundary", new Vector3(12, 1.5f, 6), new Vector3(.5f, 3, 18), stone);
            for (int i = 0; i < 5; i++) Object.DestroyImmediate(Shape("Entry path " + i, new Vector3(0, .025f, -7 + i * 2), new Vector3(2.6f, .05f, 1.2f), stone).GetComponent<Collider>());
            var gate = Shape("Prison gate", new Vector3(0, 2, 3), new Vector3(4, 4, .65f), stone); gate.AddComponent<Animator>();
            var appObject = new GameObject("Startup"); var app = appObject.AddComponent<WeaveApplication>();
            var root = new GameObject("Courtyard quest host"); var scenario = root.AddComponent<AreaRescueScenario>(); scenario.ApplicationHost = app;
            var book = ScriptableObject.CreateInstance<QuestBookAsset>(); book.Id = "courtyard-rescue"; book.Title = "Break the silence"; book.Quests = AreaRescueDefinitions.Create(); AssetDatabase.CreateAsset(book, Folder + "RescueQuest.asset"); scenario.Book = book;
            void Bind(string role, GameObject obj)
            { var entity = obj.AddComponent<QuestEntity>(); entity.DisplayName = obj.name; scenario.Bindings.Add(new EntityBinding { Role = role, Entity = entity }); }
            Bind("door", gate); Bind("encounter", root);
            var zoneObject = new GameObject("Courtyard entry region", typeof(BoxCollider), typeof(RescueZone)); zoneObject.transform.position = new Vector3(0, 1, 0);
            var trigger = zoneObject.GetComponent<BoxCollider>(); trigger.isTrigger = true; trigger.size = new Vector3(8, 3, 3);
            var zoneBody = zoneObject.GetComponent<Rigidbody>(); zoneBody.isKinematic = true; zoneBody.useGravity = false;
            zoneObject.GetComponent<RescueZone>().Scenario = scenario; Bind("zone", zoneObject);
            Object.DestroyImmediate(Shape("Entry region marker", new Vector3(0, .04f, 0), new Vector3(8, .08f, 3), blue).GetComponent<Collider>());
            var player = Shape("Player", new Vector3(0, 1, -7), Vector3.one, blue, PrimitiveType.Capsule);
            Object.DestroyImmediate(player.GetComponent<Collider>()); var controller = player.AddComponent<CharacterController>(); controller.height = 2; controller.radius = .45f; controller.minMoveDistance = 0;
            var health = player.AddComponent<CombatActor>(); health.MaxHealth = 12; scenario.Player = health;
            var input = player.AddComponent<RescuePlayerController>(); input.Scenario = scenario;
            var enemy = Shape("Encounter guard", new Vector3(0, -100, 0), Vector3.one, red, PrimitiveType.Capsule); enemy.AddComponent<CombatActor>().MaxHealth = 3; enemy.AddComponent<CombatActorVisual>();
            var enemyPrefab = PrefabUtility.SaveAsPrefabAsset(enemy, Folder + "Guard.prefab").GetComponent<CombatActor>(); Object.DestroyImmediate(enemy);
            var captive = Shape("Captured companion", new Vector3(0, -100, 0), Vector3.one, gold, PrimitiveType.Capsule);
            var companionPrefab = PrefabUtility.SaveAsPrefabAsset(captive, Folder + "Companion.prefab"); Object.DestroyImmediate(captive);
            var definition = ScriptableObject.CreateInstance<EncounterDefinition>(); definition.EnemyPrefab = enemyPrefab; definition.CompanionPrefab = companionPrefab; definition.EnemyCount = 3; AssetDatabase.CreateAsset(definition, Folder + "PrisonEncounter.asset"); scenario.Encounter = definition;
            var spawner = root.AddComponent<EncounterSpawner>(); scenario.Spawner = spawner; spawner.SpawnPoints = new Transform[5];
            for (int i = 0; i < 5; i++) { var point = new GameObject("Guard spawn " + (i + 1)); point.transform.position = new Vector3(-4 + i * 2, 1, 8 + i % 2 * 2); spawner.SpawnPoints[i] = point.transform; }
            var companionPoint = new GameObject("Companion spawn"); companionPoint.transform.position = new Vector3(0, 1, 13); spawner.CompanionPoint = companionPoint.transform;
            var outsider = Object.Instantiate(enemyPrefab.gameObject); outsider.name = "Unrelated sentry (not in quest encounter)"; outsider.transform.position = new Vector3(-7, 1, -5);
            var combat = root.AddComponent<RescueCombat>(); combat.Scenario = scenario;
            var director = root.AddComponent<PlayableDirector>(); director.playOnAwake = false;
            var operation = root.AddComponent<TimelineOperation>(); operation.Director = director; scenario.Timeline = operation;
            var timeline = ScriptableObject.CreateInstance<TimelineAsset>(); AssetDatabase.CreateAsset(timeline, Folder + "OpenGate.playable");
            var track = timeline.CreateTrack<AnimationTrack>(null, "Prison gate lift"); track.trackOffset = TrackOffset.ApplySceneOffsets;
            var animation = new AnimationClip { name = "Lift gate", frameRate = 60 };
            animation.SetCurve("", typeof(Transform), "m_LocalPosition.y", AnimationCurve.EaseInOut(0, 2, 3, 7)); AssetDatabase.CreateAsset(animation, Folder + "LiftGate.anim");
            var clip = track.CreateClip<AnimationPlayableAsset>(); ((AnimationPlayableAsset)clip.asset).clip = animation; clip.start = 0; clip.duration = 3;
            var signal = ScriptableObject.CreateInstance<SignalAsset>(); AssetDatabase.CreateAsset(signal, Folder + "ReleaseGuards.signal");
            timeline.CreateMarkerTrack(); var marker = timeline.markerTrack.CreateMarker<SignalEmitter>(2.6); marker.asset = signal; marker.emitOnce = true;
            EditorUtility.SetDirty(timeline); scenario.Opening = timeline; scenario.DoorTrack = track; scenario.ReleaseSignal = signal;
            var camera = new GameObject("Camera").AddComponent<Camera>(); camera.transform.position = new Vector3(20, 25, -27); camera.transform.LookAt(new Vector3(0, 0, 4)); camera.orthographic = true; camera.orthographicSize = 18; camera.tag = "MainCamera"; camera.gameObject.AddComponent<AudioListener>(); input.Camera = camera;
            camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = new Color(.025f, .04f, .065f);
            var light = new GameObject("Sun").AddComponent<Light>(); light.type = LightType.Directional; light.intensity = 1.2f; light.shadows = LightShadows.Soft; light.transform.rotation = Quaternion.Euler(45, -30, 0); RenderSettings.ambientLight = new Color(.4f, .45f, .5f);
            BuildUi(scenario, input);
            EditorSceneManager.SaveScene(scene, ScenePath); AssetDatabase.SaveAssets();
            AreaRescueArtPass.Apply(); AreaRescueArtPass.RefreshSentry();
        }
        private static void BuildUi(AreaRescueScenario scenario, RescuePlayerController input)
        {
            var canvasObject = new GameObject("Rescue UI", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster), typeof(UiRoot));
            var canvas = canvasObject.GetComponent<Canvas>(); canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            var scaler = canvasObject.GetComponent<CanvasScaler>(); scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize; scaler.referenceResolution = new Vector2(1920, 1080); scaler.matchWidthOrHeight = .5f;
            RectTransform Rect(string name, Transform parent, Vector2 min, Vector2 max) { var r = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>(); r.SetParent(parent, false); r.anchorMin = min; r.anchorMax = max; r.offsetMin = r.offsetMax = Vector2.zero; return r; }
            var hud = Rect("HUD", canvasObject.transform, Vector2.zero, Vector2.one); var screen = Rect("Screen", canvasObject.transform, Vector2.zero, Vector2.one); var modal = Rect("Modal", canvasObject.transform, Vector2.zero, Vector2.one);
            var blocker = Rect("Modal shield", modal, Vector2.zero, Vector2.one); blocker.gameObject.AddComponent<Image>().color = new Color(0, 0, 0, .25f);
            var events = new GameObject("EventSystem", typeof(EventSystem), typeof(InputSystemUIInputModule), typeof(LegacyUiPointerBridge)); events.GetComponent<InputSystemUIInputModule>().AssignDefaultActions();
            var ui = canvasObject.GetComponent<UiRoot>(); var data = new SerializedObject(ui);
            void Assign(string name, Object value) => data.FindProperty(name).objectReferenceValue = value;
            Assign("_canvas", canvas); Assign("_raycaster", canvasObject.GetComponent<GraphicRaycaster>()); Assign("_hudLayer", hud); Assign("_screenLayer", screen); Assign("_modalLayer", modal); Assign("_modalInputBlocker", blocker.gameObject); Assign("_eventSystem", events.GetComponent<EventSystem>()); Assign("_inputModule", events.GetComponent<InputSystemUIInputModule>()); data.ApplyModifiedPropertiesWithoutUndo();
            var viewPath = Folder + "RescueDialogueWindow.prefab";
            if (!AssetDatabase.CopyAsset("Packages/com.demon.weave/Editor/Templates/DialogueWindow.prefab", viewPath)) throw new IOException("Could not copy the dialogue view template.");
            var catalog = ScriptableObject.CreateInstance<UiCatalog>(); AssetDatabase.CreateAsset(catalog, Folder + "UiCatalog.asset");
            var catalogData = new SerializedObject(catalog); var entries = catalogData.FindProperty("_entries"); entries.arraySize = 1; var e = entries.GetArrayElementAtIndex(0);
            e.FindPropertyRelative("_uiId").stringValue = RescuePresentation.DialogueId; e.FindPropertyRelative("_prefab").objectReferenceValue = AssetDatabase.LoadAssetAtPath<GameObject>(viewPath); e.FindPropertyRelative("_layer").enumValueIndex = (int)UiLayer.Modal; e.FindPropertyRelative("_modal").boolValue = true; e.FindPropertyRelative("_blocksInput").boolValue = true; e.FindPropertyRelative("_stackPolicy").enumValueIndex = (int)UiStackPolicy.Stack; catalogData.ApplyModifiedPropertiesWithoutUndo();
            var character = ScriptableObject.CreateInstance<DialogueCharacter>(); character.DisplayName = "Lio"; AssetDatabase.CreateAsset(character, Folder + "CompanionCharacter.asset");
            var dialogue = ScriptableObject.CreateInstance<DialogueAsset>(); dialogue.Lines = new System.Collections.Generic.List<DialogueAsset.Line> {
                new DialogueAsset.Line { Id = "release", Speaker = character, Text = "You made it. Cut these bonds, quickly.", Options = new System.Collections.Generic.List<DialogueAsset.Option> { new DialogueAsset.Option { Text = "You're safe. Come with me.", Command = "companion.release", Next = "thanks" } } },
                new DialogueAsset.Line { Id = "thanks", Speaker = character, Text = "I thought nobody would find me. Let's get out of here." }
            }; AssetDatabase.CreateAsset(dialogue, Folder + "RescueConversation.asset");
            var presentation = canvasObject.AddComponent<RescuePresentation>(); presentation.Scenario = scenario; presentation.PlayerInput = input; presentation.Ui = ui; presentation.Catalog = catalog; presentation.RescueDialogue = dialogue; input.Ui = ui;
            var font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            Text Label(string name, Vector2 min, Vector2 max, int size, Color color) { var label = Rect(name, hud, min, max).gameObject.AddComponent<Text>(); label.font = font; label.fontSize = size; label.color = color; label.raycastTarget = false; return label; }
            presentation.Objective = Label("Task objective", new Vector2(.03f, .82f), new Vector2(.4f, .96f), 27, new Color(1, .83f, .5f));
            presentation.Status = Label("Health and encounter", new Vector2(.03f, .74f), new Vector2(.48f, .82f), 23, Color.white);
            presentation.Feedback = Label("World feedback", new Vector2(.3f, .91f), new Vector2(.78f, .98f), 21, Color.white);
            var controls = Label("Controls", new Vector2(.22f, .02f), new Vector2(.8f, .09f), 20, Color.white); controls.text = "WASD / click ground: move  •  Click nearby guards to attack  •  Click companion to speak\nDeath restores the courtyard checkpoint. The side sentry does not count."; controls.alignment = TextAnchor.MiddleCenter;
            var retry = Rect("Retry checkpoint", hud, new Vector2(.83f, .9f), new Vector2(.97f, .96f)); retry.gameObject.AddComponent<Image>().color = new Color(.15f, .2f, .25f); presentation.Retry = retry.gameObject.AddComponent<Button>();
            var retryText = new GameObject("Label", typeof(RectTransform), typeof(Text)).GetComponent<Text>(); retryText.transform.SetParent(retry, false); var rect = (RectTransform)retryText.transform; rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one; rect.offsetMin = rect.offsetMax = Vector2.zero; retryText.font = font; retryText.text = "RETRY CHECKPOINT"; retryText.fontSize = 20; retryText.alignment = TextAnchor.MiddleCenter; retryText.raycastTarget = false;
        }
        public static void UpgradeZonePhysics()
        {
            var scene = EditorSceneManager.OpenScene(ScenePath);
            foreach (var zone in Object.FindObjectsByType<RescueZone>())
            {
                var body = zone.GetComponent<Rigidbody>();
                if (!body) body = zone.gameObject.AddComponent<Rigidbody>();
                body.isKinematic = true; body.useGravity = false;
                EditorUtility.SetDirty(body);
            }
            foreach (var collider in Object.FindObjectsByType<Collider>())
                if (collider.name.StartsWith("Entry path ") || collider.name == "Entry region marker") Object.DestroyImmediate(collider);
            foreach (var controller in Object.FindObjectsByType<CharacterController>()) { controller.minMoveDistance = 0; EditorUtility.SetDirty(controller); }
            EditorSceneManager.SaveScene(scene); AssetDatabase.SaveAssets();
        }
    }
}
