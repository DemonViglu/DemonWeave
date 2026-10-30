using System;
using System.Linq;
using DemonViglu.Dialogue;
using DemonViglu.UI;
using DemonWeave.Presentation;
using DemonWeave.Quests;
using DemonWeave.Unity;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace DemonWeave.Messenger
{
    public sealed class MessengerPresentation : MonoBehaviour, IDialogueConditions, IDialogueCommands
    {
        public MessengerScenario Scenario;
        public Camera WorldCamera;
        public UiRoot Ui;
        public UiCatalog Catalog;
        public MessengerConversations Content;
        public Text Tracker;
        public Text Notification;
        public Text InteractionHint;
        public Button JournalButton;
        public const string DialogueId = "messenger.dialogue";
        public const string JournalId = "messenger.journal";
        private QuestEntity hovered;
        private string lastRole;
        private string lastFeedback;
        private float feedbackUntil;
        private bool ready;
        private void Start()
        {
            if (!Scenario || !WorldCamera || !Ui || !Catalog || !Content) throw new InvalidOperationException("Bind messenger presentation references.");
            ValidateContent();
            ready = Ui.Initialize(Catalog);
            if (!ready) return;
            JournalButton.onClick.AddListener(OpenJournal);
        }
        private void ValidateContent()
        {
            if (Content.Commands.Any(c => c == null || string.IsNullOrWhiteSpace(c.Id)) || Content.Commands.Select(c => c.Id).Distinct().Count() != Content.Commands.Count)
                throw new ArgumentException("对白命令需要唯一稳定 ID。");
            var allIds = Content.Commands.Select(c => c.Id).Concat(Content.Conditions.Select(c => c.Id)).ToArray();
            if (allIds.Any(string.IsNullOrWhiteSpace) || allIds.Distinct().Count() != allIds.Length) throw new ArgumentException("Condition and command IDs must be distinct.");
            foreach (var condition in Content.Conditions)
                foreach (var fact in condition.All)
                    if (!Scenario.Signals.Any(s => !s.IsEvent && s.Role == fact.Role && s.Key == fact.Key)) throw new ArgumentException("Unknown dialogue fact: " + fact.Role + "." + fact.Key);
            foreach (var command in Content.Commands)
            {
                if (command.Kind == ConversationCommandKind.Interact && !Scenario.Bindings.Any(b => b.Role == command.Role)) throw new ArgumentException("Unknown interaction role: " + command.Role);
                if (command.Kind == ConversationCommandKind.ChooseRoute && !(Scenario.Book.Quests.FirstOrDefault(q => q.Id == command.QuestId)?.Stages.FirstOrDefault(s => s.Id == command.StageId)?.Routes.Any(r => r.Id == command.RouteId) ?? false))
                    throw new ArgumentException("Missing route for command: " + command.Id);
            }
            foreach (var entry in Content.Conversations)
            {
                if (!entry.Conversation || !Scenario.Bindings.Any(b => b.Role == entry.Role)) throw new ArgumentException("Dialogue role/asset missing.");
                entry.Conversation.BuildGraph();
                foreach (var option in entry.Conversation.Lines.SelectMany(l => l.Options))
                {
                    if (!string.IsNullOrWhiteSpace(option.Command) && !Content.Commands.Any(c => c.Id == option.Command)) throw new ArgumentException("Unknown dialogue command: " + option.Command);
                    if (!string.IsNullOrWhiteSpace(option.Condition) && !allIds.Contains(option.Condition)) throw new ArgumentException("Unknown dialogue condition: " + option.Condition);
                }
            }
        }
        public bool Evaluate(string id)
        {
            if (Scenario.Run == null || Scenario.Run.IsClosed) return false;
            var condition = Content.Conditions.SingleOrDefault(c => c.Id == id);
            if (condition != null) return condition.All.All(f => Scenario.ReadFact(f.Role, f.Key) is bool value && value == f.Expected);
            var command = Content.Commands.SingleOrDefault(c => c.Id == id);
            if (command == null || Scenario.Run == null || Scenario.Run.IsClosed) return false;
            if (command.Kind == ConversationCommandKind.Interact) return Scenario.Bindings.Any(b => b.Role == command.Role && b.Entity);
            if (command.Kind == ConversationCommandKind.AcceptAssignment) return !Scenario.Accepted;
            if (command.Kind == ConversationCommandKind.HandOverEnvoy) return Scenario.ReadFact("envoy", "safe") == true && Scenario.ReadFact("envoy", "handed-over") == false;
            return Scenario.Run.CanChooseRoute(command.QuestId, command.StageId, command.RouteId, out _);
        }
        public bool TryExecute(string id, out string reason)
        {
            reason = "This choice is no longer available.";
            if (!Evaluate(id)) return false;
            var command = Content.Commands.Single(c => c.Id == id);
            if (command.Kind == ConversationCommandKind.ChooseRoute)
            {
                if (!Scenario.SelectRoute(command.QuestId, command.StageId, command.RouteId)) return false;
            }
            else if (command.Kind == ConversationCommandKind.Interact) Scenario.Interact(Scenario.Bindings.Single(b => b.Role == command.Role).Entity);
            else if (!Scenario.ConfirmAssignment(command.Kind == ConversationCommandKind.HandOverEnvoy)) return false;
            reason = ""; return true;
        }
        public bool OpenConversation(string role)
        {
            if (!ready || Ui.IsShown(DialogueId)) return false;
            var entry = Content.Conversations.FirstOrDefault(e => e.Role == role);
            if (entry == null) return false;
            if (!Ui.Show(DialogueId) || !Ui.TryGetView(DialogueId, out var view)) return false;
            lastRole = role;
            try
            {
                if (((DialoguePanel)view).Begin(entry.Conversation, this, this, _ => Ui.CloseOrReturn(DialogueId))) return true;
            }
            catch { Ui.CloseOrReturn(DialogueId); throw; }
            Ui.CloseOrReturn(DialogueId); return false;
        }
        public void OpenJournal()
        {
            if (!ready || Ui.IsShown(DialogueId)) return;
            if (!Ui.Show(JournalId) || !Ui.TryGetView(JournalId, out var view)) return;
            var journal = (MessengerJournalView)view;
            journal.CloseButton.onClick.RemoveAllListeners(); journal.CloseButton.onClick.AddListener(() => Ui.CloseOrReturn(JournalId));
            journal.TalkButton.onClick.RemoveAllListeners(); journal.TalkButton.onClick.AddListener(() => OpenConversation(lastRole));
            journal.TalkButton.gameObject.SetActive(!string.IsNullOrEmpty(lastRole));
            journal.Body.text = JournalText();
        }
        private string JournalText()
        {
            if (Scenario.Run == null) return "Preparing the journey…";
            return string.Join("\n\n", Scenario.Run.Instances.Select(q => {
                var definition = q.DefinitionSnapshot();
                return q.Title + "\n\n" + string.Join("\n\n", q.Stages.Select(s => {
                    var stage = definition.Stages.Single(d => d.Id == s.Id);
                    var state = s.State == QuestStatus.Completed ? "COMPLETE" : s.State == QuestStatus.Active ? "CURRENT OBJECTIVE" : s.State == QuestStatus.Waiting ? "TO COME" : "CLOSED";
                    var route = stage.Routes.FirstOrDefault(r => r.Id == s.SelectedRouteId);
                    return stage.Title + "  /  " + state + "\n" + string.Join("\n", stage.Objectives.Select(o => "  " + o.Label + "    " + s.Progress(o.Id) + "/" + o.Required))
                        + (route == null ? "" : "\n  Your approach: " + route.Title);
                }));
            }));
        }
        private void Update()
        {
            if (!ready) return;
            Tracker.transform.parent.gameObject.SetActive(!Ui.BlocksGameplayInput);
            JournalButton.gameObject.SetActive(!Ui.BlocksGameplayInput);
            var run = Scenario.Run;
            Tracker.text = run == null ? "Preparing the journey…" : string.Join("\n", run.Instances.Select(q => {
                var current = q.Stages.FirstOrDefault(s => s.State == QuestStatus.Active);
                var definition = q.DefinitionSnapshot().Stages.FirstOrDefault(s => s.Id == current?.Id);
                return q.Title + "\n" + (current == null ? q.State == QuestStatus.Completed ? "Completed — envoy delivered safely" : "Speak with the contact" : current.Title + "\n" + string.Join("\n", definition.Objectives.Select(o => "• " + o.Label + "  " + current.Progress(o.Id) + "/" + o.Required)));
            }));
            if (lastFeedback != Scenario.Feedback) { lastFeedback = Scenario.Feedback; feedbackUntil = Time.unscaledTime + 7; }
            Notification.text = Time.unscaledTime < feedbackUntil ? lastFeedback : "";
            if (Keyboard.current?.jKey.wasPressedThisFrame == true) OpenJournal();
            hovered = null;
            if (!Ui.BlocksGameplayInput && Mouse.current != null && !(EventSystem.current?.IsPointerOverGameObject() ?? false)
                && Physics.Raycast(WorldCamera.ScreenPointToRay(Mouse.current.position.ReadValue()), out var hit, 200))
                hovered = hit.collider.GetComponentInParent<QuestEntity>();
            InteractionHint.text = hovered ? "[ CLICK ]  " + hovered.DisplayName : "";
            if (hovered && Mouse.current.leftButton.wasPressedThisFrame)
            {
                var role = Scenario.Bindings.FirstOrDefault(b => b.Entity == hovered)?.Role;
                if (!OpenConversation(role)) Scenario.Interact(hovered);
            }
        }
        private void OnDisable()
        {
            if (Ui) { Ui.CloseOrReturn(DialogueId); Ui.CloseOrReturn(JournalId); }
            JournalButton?.onClick.RemoveAllListeners(); ready = false;
        }
    }
}
