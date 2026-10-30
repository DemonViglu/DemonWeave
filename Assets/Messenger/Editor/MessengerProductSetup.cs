using System.Collections.Generic;
using System.IO;
using DemonViglu.Dialogue.Unity;
using DemonViglu.UI;
using DemonWeave.Presentation;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

namespace DemonWeave.Messenger.Editor
{
    public static class MessengerProductSetup
    {
        public const string ScenePath = "Assets/Messenger/Scenes/MissingEnvoyProduct.unity";
        private const string Folder = "Assets/Messenger/Content/Presentation/";
        private static Font font;
        private static readonly Color Ink = new Color(.045f, .065f, .085f, .97f);
        private static readonly Color Gold = new Color(.88f, .75f, .48f);
        [MenuItem("DemonWeave/打开使者剧情体验")]
        public static void Open()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            Build(); EditorSceneManager.OpenScene(ScenePath);
        }
        public static void Build()
        {
            if (File.Exists(ScenePath)) return;
            Directory.CreateDirectory(Folder); AssetDatabase.Refresh();
            MessengerSetup.Build();
            var scene = EditorSceneManager.OpenScene(MessengerSetup.ScenePath);
            var host = Object.FindAnyObjectByType<MessengerScenario>();
            host.GetComponent<MessengerInteraction>().enabled = false;
            host.GetComponent<MessengerInteraction>().Document.gameObject.SetActive(false);
            font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            var presentation = host.gameObject.AddComponent<MessengerPresentation>();
            presentation.Scenario = host; presentation.WorldCamera = host.GetComponent<MessengerInteraction>().Camera;
            presentation.Content = Conversations();
            presentation.Content.AuthoringBook = host.Book; EditorUtility.SetDirty(presentation.Content);

            var canvasObject = new GameObject("Presentation · HUD / Screen / Modal", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster), typeof(UiRoot));
            var canvas = canvasObject.GetComponent<Canvas>(); canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            var scaler = canvasObject.GetComponent<CanvasScaler>(); scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize; scaler.referenceResolution = new Vector2(1920, 1080); scaler.matchWidthOrHeight = .5f;
            var hud = Rect("HUD", canvasObject.transform, Vector2.zero, Vector2.one);
            var screens = Rect("Screens", canvasObject.transform, Vector2.zero, Vector2.one);
            var modals = Rect("Modals", canvasObject.transform, Vector2.zero, Vector2.one);
            var blocker = Panel("Modal input shield", modals, Vector2.zero, Vector2.one, new Color(0, 0, 0, .24f));
            var events = new GameObject("UI EventSystem", typeof(EventSystem), typeof(InputSystemUIInputModule));
            var module = events.GetComponent<InputSystemUIInputModule>(); module.AssignDefaultActions();
            var ui = canvasObject.GetComponent<UiRoot>(); var serialized = new SerializedObject(ui);
            Assign(serialized, "_canvas", canvas); Assign(serialized, "_raycaster", canvasObject.GetComponent<GraphicRaycaster>());
            Assign(serialized, "_hudLayer", hud); Assign(serialized, "_screenLayer", screens); Assign(serialized, "_modalLayer", modals);
            Assign(serialized, "_modalInputBlocker", blocker.gameObject); Assign(serialized, "_eventSystem", events.GetComponent<EventSystem>()); Assign(serialized, "_inputModule", module);
            serialized.ApplyModifiedPropertiesWithoutUndo(); presentation.Ui = ui;
            var catalog = ScriptableObject.CreateInstance<UiCatalog>(); AssetDatabase.CreateAsset(catalog, Folder + "UiCatalog.asset");
            var catalogData = new SerializedObject(catalog); var entries = catalogData.FindProperty("_entries"); entries.arraySize = 2;
            Entry(entries.GetArrayElementAtIndex(0), MessengerPresentation.DialogueId, DialoguePrefab(), UiLayer.Modal, true);
            Entry(entries.GetArrayElementAtIndex(1), MessengerPresentation.JournalId, JournalPrefab(), UiLayer.Screen, false);
            catalogData.ApplyModifiedPropertiesWithoutUndo(); presentation.Catalog = catalog;

            var tracker = Panel("Quest tracker", hud, new Vector2(.025f, .69f), new Vector2(.29f, .96f), new Color(.03f, .05f, .07f, .8f)); tracker.GetComponent<Image>().raycastTarget = false;
            Text("Eyebrow", tracker, "FIELD JOURNAL  /  01", new Vector2(.07f, .78f), new Vector2(.93f, .94f), 16, Gold);
            presentation.Tracker = Text("Current objective", tracker, "", new Vector2(.07f, .08f), new Vector2(.93f, .77f), 23, Color.white);
            presentation.Notification = Text("Result feedback", hud, "", new Vector2(.3f, .86f), new Vector2(.76f, .97f), 23, Gold);
            presentation.Notification.alignment = TextAnchor.MiddleCenter;
            presentation.InteractionHint = Text("Interaction hint", hud, "", new Vector2(.32f, .08f), new Vector2(.68f, .14f), 22, Color.white);
            presentation.InteractionHint.alignment = TextAnchor.MiddleCenter;
            presentation.JournalButton = Button("Journal", hud, "J   JOURNAL", new Vector2(.84f, .9f), new Vector2(.97f, .96f));
            Text("Controls", hud, "Click an object to interact  ·  Use CLOSE / RETURN to go back", new Vector2(.32f, .025f), new Vector2(.76f, .065f), 16, new Color(.7f, .75f, .8f));
            EditorSceneManager.SaveScene(scene, ScenePath); AssetDatabase.SaveAssets();
        }
        private static void Assign(SerializedObject data, string field, Object value) => data.FindProperty(field).objectReferenceValue = value;
        private static void Entry(SerializedProperty entry, string id, GameObject prefab, UiLayer layer, bool modal)
        {
            entry.FindPropertyRelative("_uiId").stringValue = id; entry.FindPropertyRelative("_prefab").objectReferenceValue = prefab;
            entry.FindPropertyRelative("_layer").enumValueIndex = (int)layer; entry.FindPropertyRelative("_modal").boolValue = modal;
            entry.FindPropertyRelative("_blocksInput").boolValue = true; entry.FindPropertyRelative("_stackPolicy").enumValueIndex = (int)UiStackPolicy.Stack;
        }
        private static RectTransform Rect(string name, Transform parent, Vector2 min, Vector2 max)
        {
            var obj = new GameObject(name, typeof(RectTransform)); obj.transform.SetParent(parent, false);
            var rect = (RectTransform)obj.transform; rect.anchorMin = min; rect.anchorMax = max; rect.offsetMin = rect.offsetMax = Vector2.zero; return rect;
        }
        private static RectTransform Panel(string name, Transform parent, Vector2 min, Vector2 max, Color color)
        { var rect = Rect(name, parent, min, max); rect.gameObject.AddComponent<Image>().color = color; return rect; }
        private static Text Text(string name, Transform parent, string text, Vector2 min, Vector2 max, int size, Color color)
        {
            var rect = Rect(name, parent, min, max); var label = rect.gameObject.AddComponent<Text>(); label.font = font;
            label.text = text; label.fontSize = size; label.color = color; label.raycastTarget = false; label.horizontalOverflow = HorizontalWrapMode.Wrap; return label;
        }
        private static Button Button(string name, Transform parent, string text, Vector2 min, Vector2 max)
        {
            var rect = Panel(name, parent, min, max, new Color(.16f, .20f, .24f, .98f)); var button = rect.gameObject.AddComponent<Button>();
            var label = Text("Label", rect, text, new Vector2(.04f, .06f), new Vector2(.96f, .94f), 22, Color.white); label.alignment = TextAnchor.MiddleCenter;
            var colors = button.colors; colors.highlightedColor = new Color(1, .9f, .65f); colors.selectedColor = Color.white; button.colors = colors; return button;
        }
        private static void BindView(UiView view)
        { var group = view.gameObject.AddComponent<CanvasGroup>(); var data = new SerializedObject(view); Assign(data, "_canvasGroup", group); data.ApplyModifiedPropertiesWithoutUndo(); }
        private static GameObject DialoguePrefab()
        {
            var root = Rect("Dialogue window", null, Vector2.zero, Vector2.one); var view = root.gameObject.AddComponent<DialoguePanel>(); BindView(view);
            var bottom = Panel("Dialogue frame", root, new Vector2(.06f, .055f), new Vector2(.94f, .35f), Ink);
            Panel("Gold rule", bottom, new Vector2(0, .985f), Vector2.one, Gold);
            view.Speaker = Text("Speaker", bottom, "", new Vector2(.04f, .76f), new Vector2(.57f, .92f), 28, Gold);
            view.Line = Text("Dialogue text", bottom, "", new Vector2(.04f, .24f), new Vector2(.60f, .72f), 26, Color.white);
            view.Status = Text("Choice feedback", bottom, "", new Vector2(.04f, .04f), new Vector2(.60f, .20f), 18, Gold);
            view.Choices = Rect("Choices", bottom, new Vector2(.65f, .10f), new Vector2(.97f, .92f));
            var layout = view.Choices.gameObject.AddComponent<VerticalLayoutGroup>(); layout.spacing = 10; layout.childControlWidth = true; layout.childForceExpandWidth = true; layout.childControlHeight = true; layout.childForceExpandHeight = false;
            var template = Button("Choice template", root, "", Vector2.zero, Vector2.one); template.gameObject.AddComponent<LayoutElement>().preferredHeight = 62; template.gameObject.SetActive(false); view.ChoicePrefab = template;
            view.ContinueButton = Button("Continue", bottom, "CONTINUE  ›", new Vector2(.72f, .08f), new Vector2(.97f, .31f));
            view.CancelButton = Button("Cancel dialogue", root, "CLOSE", new Vector2(.86f, .36f), new Vector2(.94f, .41f));
            var prefab = PrefabUtility.SaveAsPrefabAsset(root.gameObject, Folder + "DialogueWindow.prefab"); Object.DestroyImmediate(root.gameObject); return prefab;
        }
        private static GameObject JournalPrefab()
        {
            var root = Panel("Quest journal", null, new Vector2(.12f, .10f), new Vector2(.88f, .9f), Ink); var view = root.gameObject.AddComponent<MessengerJournalView>(); BindView(view);
            view.Heading = Text("Title", root, "JOURNAL  /  THE MISSING ENVOY", new Vector2(.05f, .85f), new Vector2(.88f, .95f), 30, Gold);
            view.Body = Text("Objectives and route evidence", root, "", new Vector2(.05f, .17f), new Vector2(.94f, .83f), 20, Color.white);
            view.CloseButton = Button("Close", root, "RETURN", new Vector2(.76f, .04f), new Vector2(.95f, .12f));
            view.TalkButton = Button("Talk", root, "RESUME CONVERSATION", new Vector2(.05f, .04f), new Vector2(.42f, .12f));
            var prefab = PrefabUtility.SaveAsPrefabAsset(root.gameObject, Folder + "QuestJournal.prefab"); Object.DestroyImmediate(root.gameObject); return prefab;
        }
        private static MessengerConversations Conversations()
        {
            var content = ScriptableObject.CreateInstance<MessengerConversations>();
            DialogueCharacter Character(string name)
            { var c = ScriptableObject.CreateInstance<DialogueCharacter>(); c.DisplayName = name; c.NameColor = Gold; AssetDatabase.CreateAsset(c, Folder + name + ".asset"); return c; }
            var contact = Character("Mara"); var guard = Character("Gatekeeper");
            void Add(string role, DialogueCharacter speaker, string text, params DialogueAsset.Option[] options)
            {
                var asset = ScriptableObject.CreateInstance<DialogueAsset>(); asset.Lines = new List<DialogueAsset.Line> { new DialogueAsset.Line { Id = "opening", Speaker = speaker, Text = text, Options = new List<DialogueAsset.Option>(options) } };
                AssetDatabase.CreateAsset(asset, Folder + role + "-conversation.asset"); content.Conversations.Add(new MessengerConversations.Entry { Role = role, Conversation = asset });
            }
            DialogueAsset.Option Option(string text, string command = null, string next = null) => new DialogueAsset.Option { Text = text, Command = command, Condition = command, Next = next };
            Add("contact", contact, "The watch post has kept its silence. All I ask is that our envoy comes home alive.",
                new DialogueAsset.Option { Text = "I'll follow the trail and find them.", Condition = "assignment.new", Command = "assignment.accept" },
                new DialogueAsset.Option { Text = "The envoy is safe. I'll leave them in your care.", Condition = "envoy.ready", Command = "assignment.handover" }, Option("Not yet."));
            Add("guard", guard, "The envoy stays here. If you wish to negotiate, there is a price. Otherwise, keep away from the tunnel.", Option("Offer a deposit and negotiate.", "route.negotiate", "decision"), Option("Take the tunnel route.", "route.infiltrate"), Option("Leave."));
            var conversation = content.Conversations.Find(e => e.Role == "guard").Conversation;
            conversation.Lines.Add(new DialogueAsset.Line { Id = "decision", Speaker = guard, Text = "You have my answer. What will you do?", Options = new List<DialogueAsset.Option> { Option("Use the tunnel instead.", "route.infiltrate"), Option("Leave and assess the situation.") } }); EditorUtility.SetDirty(conversation);
            content.Commands.Add(new MessengerConversations.Command { Id = "contact.interact", Kind = ConversationCommandKind.Interact, Role = "contact" });
            content.Commands.Add(new MessengerConversations.Command { Id = "assignment.accept", Kind = ConversationCommandKind.AcceptAssignment });
            content.Commands.Add(new MessengerConversations.Command { Id = "assignment.handover", Kind = ConversationCommandKind.HandOverEnvoy });
            content.Conditions.Add(new MessengerConversations.Condition { Id = "assignment.new", All = new List<MessengerConversations.FactRequirement> { new MessengerConversations.FactRequirement { Role = "contact", Key = "accepted", Expected = false } } });
            content.Conditions.Add(new MessengerConversations.Condition { Id = "envoy.ready", All = new List<MessengerConversations.FactRequirement> { new MessengerConversations.FactRequirement { Role = "envoy", Key = "safe" }, new MessengerConversations.FactRequirement { Role = "envoy", Key = "handed-over", Expected = false } } });
            foreach (var route in new[] { "negotiate", "infiltrate" }) content.Commands.Add(new MessengerConversations.Command { Id = "route." + route, Kind = ConversationCommandKind.ChooseRoute, QuestId = "missing-envoy", StageId = "rescue", RouteId = route });
            AssetDatabase.CreateAsset(content, Folder + "Conversations.asset"); return content;
        }
        public static void UpgradePresentation()
        {
            font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            var path = Folder + "DialogueWindow.prefab";
            var root = PrefabUtility.LoadPrefabContents(path);
            try
            {
                var view = root.GetComponent<DialoguePanel>();
                if (!view.CancelButton) view.CancelButton = Button("Cancel dialogue", root.transform, "CLOSE", new Vector2(.86f, .36f), new Vector2(.94f, .41f));
                PrefabUtility.SaveAsPrefabAsset(root, path);
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
            AssetDatabase.SaveAssets();
        }
    }
}
